using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Input;
using LtcPlayer.App;
namespace LtcPlayer.Avalonia;

public static class OperationAccessVerification
{
 public static async Task RunAsync(MainWindow w,string folder)
 {
  Directory.CreateDirectory(folder);var results=new List<object>();bool failed=false;int receiving=0;
  var seek=w.FindControl<Slider>("Seek")!;var grid=w.FindControl<DataGrid>("CueGrid")!;
  void Check(bool value,string reason){if(!value)throw new IOException(reason);}
  async Task Until(Func<bool> condition){double end=MasterClock.Timestamp()+12;while(!condition()){if(MasterClock.Timestamp()>end)throw new TimeoutException(JsonSerializer.Serialize(new{w.Preview.Snapshot,w.LatestObservation,w.Coordinator.Audio}));await Task.Delay(10);}}
  async Task Test(string name,Func<Task> body){try{await body();results.Add(new{test=name,status="PASS"});}catch(Exception e){failed=true;results.Add(new{test=name,status="FAIL",detail=e.ToString()});}File.WriteAllText(Path.Combine(folder,"results.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));}
  async Task KeyboardSeek(double position){seek.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Key=Key.Right});seek.Value=position;seek.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyUpEvent,Key=Key.Right});await Until(()=>!w.Preview.IsBusy&&Math.Abs(w.Preview.Snapshot.Position-position)<.4);}
  var a=new CueRow{FilePath=Path.Combine(Program.Option("--fixtures")!,"hap-pcm.mov")};
  var b=new CueRow{FilePath=Path.Combine(Program.Option("--fixtures")!,"h264-aac.mp4"),Enabled=false};b.SetStart("00:00:20:00");
  var q=new CueRow{FilePath=Path.Combine(Program.Option("--fixtures")!,"hap-q-silent.mov"),Enabled=false};q.SetStart("00:00:40:00");
  string path=Path.Combine(folder,"saved.json");
  try{
   await w.ApplySettingsAsync(new(new(Rate:"25"),VideoRenderer.Displays().First(d=>d.Primary!=0)));
   foreach(var row in new[]{a,b,q}){await ShowProgramService.RefreshAsync(row,CancellationToken.None);w.Editor.Add(row);}
   await w.SetSharedAudioAsync(true,silenceForVerification:true);
   await w.Monitor.ConfigureAsync(new(Rate:"25"),async(clock,token)=>{double start=MasterClock.Timestamp();while(!token.IsCancellationRequested){if(Volatile.Read(ref receiving)!=0){int f=25+(int)((MasterClock.Timestamp()-start)*25)%50;clock.Accept(new TcFrame{Seconds=f/25,Frame=f%25},MasterClock.Timestamp());}await Task.Delay(20,token);}});
   await Test("rapid HAP HAPQ and H264 selection performs no load or still decode",async()=>{
    int loads=w.Preview.LoadRequests,stills=w.Preview.StillDecodeStarts,decodes=w.Preview.DecodeStarts;
    for(int i=0;i<20;i++){grid.SelectedItem=a;grid.SelectedItem=q;grid.SelectedItem=b;}grid.SelectedItem=null;await Task.Delay(150);
    Check(w.Preview.LoadRequests==loads&&w.Preview.StillDecodeStarts==stills&&w.Preview.DecodeStarts==decodes&&w.Preview.Snapshot.State==PreviewState.Empty,"selection started media I/O");Check(!seek.IsEnabled,"empty preview seek enabled");
   });
   await Test("explicit row play loads chosen source and enables manual seek",async()=>{
    await w.PlayCueForTestAsync(a);await Until(()=>w.Preview.Snapshot.State==PreviewState.Playing&&w.Coordinator.Audio.State==SharedAudioState.Playing);Check(w.Preview.LoadedPath==a.FilePath&&seek.IsEnabled,"explicit play unavailable");
   });
   await Test("selection clearing and reorder leave active video and audio unchanged",async()=>{
    var video=w.Preview.PlaybackSessionId;var media=w.Preview.MediaSessionId;var sink=w.Coordinator.Audio.SinkSession;int loads=w.Preview.LoadRequests,preps=w.Coordinator.Audio.PreparationStarts;
    grid.SelectedItem=b;grid.SelectedItem=null;w.MoveCue(q,0);await Task.Delay(120);
    Check(w.Preview.LoadedPath==a.FilePath&&w.Preview.PlaybackSessionId==video&&w.Preview.MediaSessionId==media&&w.Preview.LoadRequests==loads,"selection changed source");Check(w.Coordinator.Audio.SinkSession==sink&&w.Coordinator.Audio.PreparationStarts==preps,"selection restarted audio");
   });
   await Test("output off keyboard seek preserves pause and addresses displayed source",async()=>{
    await w.Preview.PauseAsync();grid.SelectedItem=b;await KeyboardSeek(1.2);Check(w.Preview.Snapshot.State==PreviewState.Paused&&w.Preview.LoadedPath==a.FilePath,"paused seek followed selection or resumed");
   });
   await Test("output on permits playing seek without replacing output canvas or audio sink",async()=>{
    await w.ArmAsync();await w.Preview.PlayAsync();await Until(()=>w.Coordinator.Audio.State==SharedAudioState.Playing&&seek.IsEnabled);
    var canvas=w.Surface.Canvas.SessionId;var sink=w.Coordinator.Audio.SinkSession;await KeyboardSeek(.7);
    Check(w.IsOutputActive&&w.Surface.Canvas.OutputAttached&&w.Surface.Canvas.SessionId==canvas&&w.Coordinator.Audio.SinkSession==sink&&w.Preview.Snapshot.State==PreviewState.Playing,"seek interrupted output or failed to resume");
   });
   await Test("disabled cue plays manually without joining synchronization timeline",async()=>{
    await w.PlayCueForTestAsync(b);await Until(()=>w.Preview.Snapshot.State==PreviewState.Playing&&seek.IsEnabled);Check(!b.Enabled&&w.Editor.Timeline.All(c=>c.CueId!=b.CueId)&&w.Preview.LoadedPath==b.FilePath,"manual play changed scheduling");
   });
   await Test("manual output permits editing save and seek",()=>{
    Check(!grid.IsReadOnly&&w.FindControl<Button>("AddCueButton")!.IsEnabled&&w.FindControl<Button>("LoadProgramButton")!.IsEnabled&&w.FindControl<Button>("SaveProgramButton")!.IsEnabled&&seek.IsEnabled,"operation permissions diverged");return Task.CompletedTask;
   });
   await Test("slow manual save preserves progressing video audio and rejects duplicate save",async()=>{
    var gate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);w.Editor.BeforeSaveWriteForTest=t=>gate.Task.WaitAsync(t);
    var video=w.Preview.PlaybackSessionId;var sink=w.Coordinator.Audio.SinkSession;var canvas=w.Surface.Canvas.SessionId;int preps=w.Coordinator.Audio.PreparationStarts;double before=w.Preview.Snapshot.Position;
    var save=w.SaveProgramForTestAsync(path);try{
     Check(!w.SaveAccess.Allowed&&seek.IsEnabled,"save blocked manual control");bool rejected=false;try{await w.SaveProgramForTestAsync(path);}catch(InvalidOperationException){rejected=true;}Check(rejected,"duplicate save accepted");
     await Task.Delay(180);Check(w.Preview.Snapshot.Position>before&&w.Preview.PlaybackSessionId==video&&w.Coordinator.Audio.SinkSession==sink&&w.Coordinator.Audio.PreparationStarts==preps&&w.Surface.Canvas.SessionId==canvas,"save interrupted playback");
    }finally{gate.TrySetResult();await save;w.Editor.BeforeSaveWriteForTest=null;}Check(File.Exists(path)&&w.SaveAccess.Allowed,"save did not complete");
   });
   await Test("cancelled save preserves previous file and restores save command",async()=>{
    string before=File.ReadAllText(path);using var cancel=new CancellationTokenSource();w.Editor.BeforeSaveWriteForTest=t=>Task.Delay(Timeout.Infinite,t);
    var save=w.SaveProgramForTestAsync(path,cancel.Token);cancel.Cancel();bool cancelled=false;try{await save;}catch(OperationCanceledException){cancelled=true;}finally{w.Editor.BeforeSaveWriteForTest=null;}
    Check(cancelled&&File.ReadAllText(path)==before&&w.SaveAccess.Allowed&&!Directory.GetFiles(folder,"*.tmp").Any(),"cancel corrupted previous file or left busy");
   });
   await Test("write failure preserves previous file and leaves playback running",async()=>{
    string before=File.ReadAllText(path);var video=w.Preview.PlaybackSessionId;bool rejected=false;
    using(var locked=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)){try{await w.SaveProgramForTestAsync(path);}catch(Exception e)when(e is IOException or UnauthorizedAccessException){rejected=true;}}
    Check(rejected&&File.ReadAllText(path)==before&&w.SaveAccess.Allowed&&w.Preview.PlaybackSessionId==video&&w.Preview.Snapshot.State==PreviewState.Playing&&!Directory.GetFiles(folder,"*.tmp").Any(),"failed save damaged playback or destination");
   });
   await Test("LTC arriving during seek rejects the late manual frame",async()=>{
    await w.Preview.PauseAsync();var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    w.Preview.SeekBeforePresentForTest=async _=>{entered.TrySetResult();await release.Task;};int generation=w.Preview.SourceGeneration;var pending=w.Preview.SeekAsync(.5);
    try{await entered.Task.WaitAsync(TimeSpan.FromSeconds(8));Volatile.Write(ref receiving,1);await Until(()=>w.Preview.IsSyncOwned);}
    finally{release.TrySetResult();await pending;w.Preview.SeekBeforePresentForTest=null;}
    await Until(()=>w.LatestObservation?.CueId==a.CueId&&w.Coordinator.Audio.State==SharedAudioState.Playing);
    Check(w.Preview.SourceGeneration==generation&&w.Preview.Snapshot.State!=PreviewState.Error&&!seek.IsEnabled,"late seek overrode LTC");
   });
   await Test("LTC blocks save without disturbing synchronization and audio",async()=>{
    var sync=w.SynchronizationSession;var canvas=w.Surface.Canvas.SessionId;var sink=w.Coordinator.Audio.SinkSession;string before=File.ReadAllText(path);bool rejected=false;
    try{await w.SaveProgramForTestAsync(path);}catch(InvalidOperationException){rejected=true;}
    Check(rejected&&!w.SaveAccess.Allowed&&File.ReadAllText(path)==before&&w.SynchronizationSession==sync&&w.Surface.Canvas.SessionId==canvas&&w.Coordinator.Audio.SinkSession==sink&&w.IsOutputActive,"save bypassed LTC lock or disturbed playback");
   });
   await Test("after LTC loss held sync image rejects seek until explicit manual play",async()=>{
    Volatile.Write(ref receiving,0);await Until(()=>w.Monitor.Clock.Read().State==SyncState.LOST);await Task.Delay(150);Check(!seek.IsEnabled,"held sync seek enabled");
    await w.PlayCueForTestAsync(b);await Until(()=>seek.IsEnabled&&w.Preview.Snapshot.State==PreviewState.Playing);Check(w.Preview.LoadedPath==b.FilePath&&w.IsOutputActive&&!w.Preview.IsSyncOwned,"manual ownership did not return");
   });
   await Test("shutdown cancellation waits for pending save cleanup",async()=>{
    string before=File.ReadAllText(path);w.Editor.BeforeSaveWriteForTest=t=>Task.Delay(Timeout.Infinite,t);var save=w.SaveProgramForTestAsync(path);
    await w.CancelSaveForTestAsync();try{await save;}catch(OperationCanceledException){}finally{w.Editor.BeforeSaveWriteForTest=null;}
    Check(w.SaveAccess.Allowed&&File.ReadAllText(path)==before,"save lifetime escaped close cleanup");
   });
  }catch(Exception e){failed=true;File.WriteAllText(Path.Combine(folder,"fatal.txt"),e.ToString());}
  finally{Volatile.Write(ref receiving,0);w.Editor.BeforeSaveWriteForTest=null;await w.DisarmAsync();MainWindow.Shutdown(failed?1:0);}
 }
}
