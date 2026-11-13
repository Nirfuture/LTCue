using System.Text.Json;
using Avalonia.Controls;
using LtcPlayer.App;
namespace LtcPlayer.Avalonia;
public static class CoordinatorVerification
{
 public static async Task RunAsync(MainWindow w,string folder)
 {
  Directory.CreateDirectory(folder);bool failed=false;var results=new List<object>();int receiving=1,frames=15;
  var c=w.Coordinator;
  void Check(bool ok,string message){if(!ok)throw new IOException(message);}
  async Task Until(Func<bool> condition){double end=MasterClock.Timestamp()+6;while(!condition()){if(MasterClock.Timestamp()>end)throw new TimeoutException(JsonSerializer.Serialize(new{c.Observation,c.Audio}));await Task.Delay(10);}}
  async Task Test(string name,Func<Task> body){try{await body();results.Add(new{test=name,status="PASS"});}catch(Exception e){failed=true;results.Add(new{test=name,status="FAIL",detail=e.ToString()});}File.WriteAllText(Path.Combine(folder,"results.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));}
  try{
   var row=new CueRow{FilePath=Path.Combine(Program.Option("--fixtures")!,"h264-aac.mp4")};await ShowProgramService.RefreshAsync(row,CancellationToken.None);w.Editor.Add(row);
   await c.ConfigureAudioAsync(true,"",.5f,false,true);
   var other=new CueRow{FilePath=Path.Combine(Program.Option("--fixtures")!,"hap-pcm.mov")};other.SetStart("00:00:20:00");await ShowProgramService.RefreshAsync(other,CancellationToken.None);w.Editor.Add(other);
   await Test("manual reorder preserves playback decoder and audio stream",async()=>{
    await w.Preview.LoadAsync(row.FilePath);await w.Preview.PlayAsync();await Until(()=>c.Audio.State==SharedAudioState.Playing&&w.Preview.Snapshot.Position>.15);
    var video=w.Preview.PlaybackSessionId;var media=w.Preview.MediaSessionId;int decodes=w.Preview.DecodeStarts;var sink=c.Audio.SinkSession;int preparations=c.Audio.PreparationStarts;double before=w.Preview.Snapshot.Position;
    var trace=new List<object>();void Trace(PreviewSnapshot p)=>trace.Add(new{p.State,p.Position,IsBusy=w.Preview.IsBusy,w.Preview.PlaybackSessionId,w.Preview.MediaSessionId,w.Preview.DecodeStarts,Rebinding=typeof(MainWindow).GetField("rebinding",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(w)});w.Preview.Changed+=Trace;
    w.MoveCue(other,0);w.Editor.Validate();await Task.Delay(180);w.Preview.Changed-=Trace;
    File.WriteAllText(Path.Combine(folder,"manual-reorder-trace.json"),JsonSerializer.Serialize(new{Before=new{video,media,decodes,before},After=w.Preview.Snapshot,w.Preview.PlaybackSessionId,w.Preview.MediaSessionId,w.Preview.DecodeStarts,Trace=trace},new JsonSerializerOptions{WriteIndented=true}));
    Check(w.Preview.PlaybackSessionId==video&&w.Preview.MediaSessionId==media&&w.Preview.DecodeStarts==decodes&&w.Preview.Snapshot.State==PreviewState.Playing&&w.Preview.Snapshot.Position>before,"display reorder restarted manual source");
    Check(c.Audio.SinkSession==sink&&c.Audio.PreparationStarts==preparations,"display reorder restarted manual audio");await w.Preview.StopAsync();
   });
   await Test("explicit selection still prepares the chosen media after reorder",async()=>{
    var grid=w.FindControl<DataGrid>("CueGrid")!;grid.SelectedItem=row;await Until(()=>w.Preview.Snapshot.State==PreviewState.Ready&&w.Preview.LoadedPath==row.FilePath);
    grid.SelectedItem=other;await Until(()=>w.Preview.Snapshot.State==PreviewState.Ready&&w.Preview.LoadedPath==other.FilePath);
   });
   await w.Monitor.ConfigureAsync(new(Rate:"29.97 DF"),async(clock,token)=>{while(!token.IsCancellationRequested){if(Volatile.Read(ref receiving)!=0){int f=Interlocked.Increment(ref frames);if(f>110){Interlocked.Exchange(ref frames,15);f=15;}clock.Accept(new TcFrame{Seconds=f/30,Frame=f%30,Drop=1},MasterClock.Timestamp());}await Task.Delay(33,token);}});
   await Test("LTC session owns one generation-scoped playhead",async()=>{
    await Until(()=>c.Observation?.FramePts>0&&c.Audio.State==SharedAudioState.Playing);var head=c.ReadAudioPlayhead();Check(head.SessionId==c.SynchronizationSession&&head.CueId==row.CueId&&head.LtcDriven,"source identities diverged");Check(head.Generation==c.Observation!.InputGeneration&&w.Preview.Armed,"ownership mismatch");
   });
   await Test("LTC reorder and unchanged validation preserve generation and audio",async()=>{
    Interlocked.Exchange(ref frames,15);await Task.Delay(100);await Until(()=>c.Audio.State==SharedAudioState.Playing&&c.Audio.Generation==c.ReadAudioPlayhead().Generation&&c.Observation?.InputGeneration==c.ReadAudioPlayhead().Generation&&Math.Abs((c.Observation?.FramePts??-10)-c.ReadAudioPlayhead().Position)<.15);
    var sync=c.SynchronizationSession;var canvas=w.Surface.Canvas.SessionId;var clock=w.Monitor.Clock;var head=c.ReadAudioPlayhead();var sink=c.Audio.SinkSession;int preparations=c.Audio.PreparationStarts;int inputStarts=w.Monitor.Starts;double? before=c.Observation!.FramePts;
    w.MoveCue(row,0);w.Editor.Validate();await Task.Delay(220);
    Check(c.SynchronizationSession==sync&&w.Surface.Canvas.SessionId==canvas&&ReferenceEquals(clock,w.Monitor.Clock)&&w.Monitor.Starts==inputStarts,"reorder replaced sync/canvas/input");
    File.WriteAllText(Path.Combine(folder,"ltc-reorder-observation.json"),JsonSerializer.Serialize(new{Before=new{head,sink,preparations,FramePts=before},After=new{Head=c.ReadAudioPlayhead(),Audio=c.Audio,Observation=c.Observation}},new JsonSerializerOptions{WriteIndented=true}));
    Check(c.ReadAudioPlayhead().Generation==head.Generation&&c.Audio.SinkSession==sink&&c.Audio.PreparationStarts==preparations&&c.Observation!.FramePts>before,"reorder interrupted LTC audio/video");
   });
   await Test("actual schedule edit still invalidates the LTC run",async()=>{
    var sync=c.SynchronizationSession;row.Frames="01";await Until(()=>c.SynchronizationSession!=sync&&c.Observation?.FramePts>0);Check(w.Editor.CanArm,"valid edit invalidated show");
   });
   await Test("enabled state edits still update cue selection and silence",async()=>{
    var old=c.SynchronizationSession;row.Enabled=false;await Until(()=>c.SynchronizationSession!=old&&c.Observation?.CueId==null&&c.Audio.State==SharedAudioState.Waiting);
    old=c.SynchronizationSession;row.Enabled=true;await Until(()=>c.SynchronizationSession!=old&&c.Observation?.CueId==row.CueId&&c.Audio.State==SharedAudioState.Playing);
   });
   // Disable automatic reconciliation while explicitly racing lifecycle commands below.
   var ready=typeof(MainWindow).GetField("presentationReady",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;ready.SetValue(w,false);
   var size=w.Surface.CanvasSize;var display=new OutputDisplay{Width=size.Width,Height=size.Height};List<ScheduledCue> Cues()=>ShowProgramService.Validate(w.Editor.Rows,w.Editor.Rate);
   await Test("revision invalidation replaces run without replacing canvas",async()=>{
    var old=c.SynchronizationSession;var canvas=w.Surface.Canvas.SessionId;await c.ReconcileAsync(500,true,Cues,display,0);await Until(()=>c.Observation?.FramePts>0);Check(c.SynchronizationSession!=old&&w.Surface.Canvas.SessionId==canvas,"revision not isolated");
   });
   await Test("concurrent stop rejects late observations and clears audio",async()=>{
    await Task.WhenAll(c.StopSynchronizationAsync(),c.StopSynchronizationAsync());await Task.Delay(160);Check(!c.OwnsSurface&&c.Observation==null&&c.SynchronizationSession==null&&!w.Preview.Armed,"stopped run returned");Check(c.ReadAudioPlayhead()==AudioPlayhead.Empty&&AudioDecodeSession.ActiveDecoders==0,"old audio returned");
   });
   await Test("rapid start stop does not resurrect an old session",async()=>{
    for(int i=0;i<4;i++){await c.ReconcileAsync(600+i,true,Cues,display,0);await c.StopSynchronizationAsync();}await Task.Delay(150);Check(!c.OwnsSurface&&c.Observation==null&&c.ReadAudioPlayhead()==AudioPlayhead.Empty,"stale callback accepted");
    await c.ReconcileAsync(700,true,Cues,display,0);await Until(()=>c.Audio.State==SharedAudioState.Playing);Check(c.ReadAudioPlayhead().SessionId==c.SynchronizationSession,"restart epoch mismatch");
   });
   await Test("fault silences audio without requiring observation update",async()=>{
    Volatile.Write(ref receiving,0);w.Monitor.Clock.Fault();Check(!c.ReadAudioPlayhead().Advancing,"fault did not gate playhead");Volatile.Write(ref receiving,0);await Until(()=>c.Audio.State==SharedAudioState.Waiting);Check(AudioDecodeSession.ActiveDecoders==0,"fault left decoder");
   });
   await Test("dispose racing audio configuration releases resources and rejects restart",async()=>{
    var configure=c.ConfigureAudioAsync(false,"",.5f,false,true);var dispose=c.DisposeAsync().AsTask();await Task.WhenAll(configure,dispose);Check(!c.OwnsSurface&&AudioOutputSink.ActiveStreams==0&&AudioDecodeSession.ActiveDecoders==0,"shutdown leaked");bool rejected=false;try{await c.ConfigureAudioAsync(true,"",.5f,false,true);}catch(ObjectDisposedException){rejected=true;}Check(rejected,"disposed audio reopened");await c.ReconcileAsync(800,true,Cues,display,0);Check(!c.OwnsSurface,"disposed run restarted");
   });
  }catch(Exception e){failed=true;File.WriteAllText(Path.Combine(folder,"fatal.txt"),e.ToString());}
  finally{Volatile.Write(ref receiving,0);await c.DisposeAsync();await w.Preview.DisposeAsync();await w.Monitor.DisposeAsync();MainWindow.Shutdown(failed?1:0);}
 }
}
