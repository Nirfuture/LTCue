using System.Text.Json;
using Avalonia.Controls;
using LtcPlayer.App;
namespace LtcPlayer.Avalonia;
public static class AudioConnectedVerification
{
 public static async Task RunAsync(MainWindow w,string folder)
 {
  Directory.CreateDirectory(folder);bool failed=false;var results=new List<object>();int receiving=0,frames=15;Action? poll=null;var resumeSamples=new List<object>();
  void Check(bool ok,string message){if(!ok)throw new IOException(message);}
  async Task Until(Func<bool> condition){double end=MasterClock.Timestamp()+6;while(!condition()){poll?.Invoke();if(MasterClock.Timestamp()>end)throw new TimeoutException(JsonSerializer.Serialize(new{Audio=w.SharedAudio,Video=w.Preview.Snapshot,Sync=w.PreviewObservation}));await Task.Delay(10);}}
  async Task Test(string name,Func<Task> body){try{await body();results.Add(new{test=name,status="PASS"});}catch(Exception e){failed=true;results.Add(new{test=name,status="FAIL",detail=e.ToString()});}File.WriteAllText(Path.Combine(folder,"results.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));}
  try
  {
   var display=VideoRenderer.Displays().First(d=>d.Primary!=0);await w.ApplySettingsAsync(new(new(Rate:"29.97 DF",OutputMode:OutputPresentationMode.Compatibility),display));
   var row=new CueRow{FilePath=Path.Combine(Program.Option("--fixtures")!,"h264-aac.mp4")};await ShowProgramService.RefreshAsync(row,CancellationToken.None);w.Editor.Add(row);w.FindControl<DataGrid>("CueGrid")!.SelectedItem=row;await Until(()=>w.Preview.Snapshot.State==PreviewState.Ready);
   await w.SetSharedAudioAsync(false,silenceForVerification:true);
   await Test("manual uses video clock and independent shared audio",async()=>{
    await w.Preview.PlayAsync();await Until(()=>w.Preview.Snapshot.Position>.2);Check(PreviewAudio.ActiveStreams==0&&w.Preview.Snapshot.Audio==null,"legacy audio clock retained");
    await w.SetSharedAudioAsync(true,silenceForVerification:true);await Until(()=>w.SharedAudio.State==SharedAudioState.Playing);Check(Math.Abs(w.SharedAudio.PlayedPts!.Value-w.Preview.Snapshot.Position)<.12,"manual timeline mismatch");
   });
   await Test("external video and cover do not restart audio",async()=>{
    var a=w.SharedAudio;var video=w.Preview.PlaybackSessionId;await w.ArmAsync();w.Cover.Set(true,.1);await Task.Delay(150);await w.DisarmAsync();w.Cover.Set(false,0);await Until(()=>w.SharedAudio.PlayedPts>a.PlayedPts+.1);
    Check(w.SharedAudio.SinkSession==a.SinkSession&&w.SharedAudio.PreparationStarts==a.PreparationStarts&&w.Preview.PlaybackSessionId==video,"output/cover reset source");
   });
   await Test("audio off leaves manual video running and reenable follows it",async()=>{
    var video=w.Preview.PlaybackSessionId;double at=w.Preview.Snapshot.Position;await w.SetSharedAudioAsync(false);await Task.Delay(150);Check(w.Preview.Snapshot.Position>at+.1&&w.Preview.PlaybackSessionId==video&&AudioOutputSink.ActiveStreams==0,"audio off stopped video");
    await w.SetSharedAudioAsync(true);await Until(()=>w.SharedAudio.State==SharedAudioState.Playing);await w.Preview.PauseAsync();await Until(()=>w.SharedAudio.State==SharedAudioState.Waiting);await w.Preview.SeekAsync(.3);await w.Preview.PlayAsync();await Until(()=>w.SharedAudio.State==SharedAudioState.Playing&&w.SharedAudio.PlayedPts<1);
   });
   await Test("manual audio survives a temporary UI reconfiguration delay",async()=>{
    await Until(()=>w.SharedAudio.State==SharedAudioState.Playing);var before=w.SharedAudio;
    Thread.Sleep(450);await Until(()=>w.SharedAudio.State==SharedAudioState.Playing);var after=w.SharedAudio;
    File.WriteAllText(Path.Combine(folder,"ui-gap-observation.json"),JsonSerializer.Serialize(new{before,after},new JsonSerializerOptions{WriteIndented=true}));
    Check(after.SinkSession==before.SinkSession&&after.Generation==before.Generation&&after.PreparationStarts==before.PreparationStarts&&after.Reseeks==before.Reseeks&&after.PlayedPts>before.PlayedPts+.3&&after.HardwarePadding>0,"UI stall interrupted or restarted continuous manual audio");
   });
   await w.Monitor.ConfigureAsync(new(Rate:"29.97 DF"),async(clock,token)=>{while(!token.IsCancellationRequested){if(Volatile.Read(ref receiving)!=0){int f=Interlocked.Increment(ref frames);clock.Accept(new TcFrame{Seconds=f/30,Frame=f%30,Drop=1},MasterClock.Timestamp());}await Task.Delay(33,token);}});
   await Test("LTC preempts manual and drives audio from scheduled cue time",async()=>{
    Volatile.Write(ref receiving,1);await Until(()=>w.SharedAudio.State==SharedAudioState.Playing&&w.SharedAudio.CueId==row.CueId);Check(w.Preview.Armed&&w.Preview.Snapshot.Audio==null,"manual remained active");
    Check(Math.Abs(w.SharedAudio.PlayedPts!.Value-(w.PreviewObservation!.Target))<.12,"LTC target mismatch");
   });
   await Test("LTC jump flushes generation while output remains independent",async()=>{
    var a=w.SharedAudio;Interlocked.Exchange(ref frames,70);await Until(()=>w.SharedAudio.State==SharedAudioState.Playing&&w.SharedAudio.Generation>a.Generation&&w.SharedAudio.PlayedPts>2);
    var b=w.SharedAudio;await w.ArmAsync();await Task.Delay(80);await w.DisarmAsync();Check(w.SharedAudio.SinkSession==b.SinkSession&&w.SharedAudio.Generation==b.Generation,"output changed LTC audio");
   });
   await Test("LTC loss silences and resume does not replay old audio",async()=>{
    Volatile.Write(ref receiving,0);await Until(()=>w.SharedAudio.State==SharedAudioState.Waiting&&w.PreviewObservation?.State==SyncState.LOST);Check(AudioDecodeSession.ActiveDecoders==0,"lost decoder remained");
    poll=()=>{if(resumeSamples.Count<700)resumeSamples.Add(new{Host=MasterClock.Timestamp(),Audio=w.SharedAudio,Sync=w.PreviewObservation});};
    int old=w.SharedAudio.Generation;double resumeHost=MasterClock.Timestamp();Interlocked.Exchange(ref frames,35);Volatile.Write(ref receiving,1);
    // Recovery follows the current clock, not an absolute one-second media window
    // that may already have elapsed while the decoder was preparing.
    await Until(()=>w.SharedAudio is {State:SharedAudioState.Playing,ErrorSeconds:{} error} a&&a.CueId==row.CueId&&a.Generation>old&&a.Generation==w.PreviewObservation?.InputGeneration&&Math.Abs(error)<=.1);
    Check(w.SharedAudio.Generation>old,"resume did not advance epoch");
    File.WriteAllText(Path.Combine(folder,"resume-observation.json"),JsonSerializer.Serialize(new{RecoverySeconds=MasterClock.Timestamp()-resumeHost,Audio=w.SharedAudio,Sync=w.PreviewObservation},new JsonSerializerOptions{WriteIndented=true}));poll=null;
   });
   await Test("LTC audio survives video observation delay while the input clock remains valid",async()=>{
    await Until(()=>w.SharedAudio.State==SharedAudioState.Playing);var before=w.SharedAudio;
    Thread.Sleep(450);await Until(()=>w.SharedAudio.State==SharedAudioState.Playing);var after=w.SharedAudio;
    File.WriteAllText(Path.Combine(folder,"ltc-ui-gap-observation.json"),JsonSerializer.Serialize(new{before,after},new JsonSerializerOptions{WriteIndented=true}));
    Check(after.SinkSession==before.SinkSession&&after.Generation==before.Generation&&after.PreparationStarts==before.PreparationStarts&&after.Reseeks==before.Reseeks&&after.PlayedPts>before.PlayedPts+.3&&after.HardwarePadding>0,"video observation delay interrupted LTC audio");
   });
  }
  catch(Exception e){failed=true;File.WriteAllText(Path.Combine(folder,"fatal.txt"),e.ToString());}
  finally{File.WriteAllText(Path.Combine(folder,"resume-trace.json"),JsonSerializer.Serialize(resumeSamples,new JsonSerializerOptions{WriteIndented=true}));Volatile.Write(ref receiving,0);await w.DisarmAsync();await w.StopSyncPreviewAsync();await w.DisposeSharedAudioAsync();await w.Preview.DisposeAsync();await w.Monitor.DisposeAsync();MainWindow.Shutdown(failed?1:0);}
 }
}
