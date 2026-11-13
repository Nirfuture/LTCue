using Avalonia.Controls;
using LtcPlayer.App;
using System.Text.Json;
namespace LtcPlayer.Avalonia;
public static class RenderingHealthVerification
{
 public static async Task RunAsync(MainWindow w,string folder)
 {
  Directory.CreateDirectory(folder);var results=new List<object>();bool failed=false;
  void Check(bool ok,string message){if(!ok)throw new IOException(message);}
  async Task Until(Func<bool> condition){double until=MasterClock.Timestamp()+6;while(!condition()){if(MasterClock.Timestamp()>until)throw new TimeoutException(JsonSerializer.Serialize(new{w.Delivery,Audio=w.SharedAudio,Preview=w.Preview.Snapshot}));await Task.Delay(10);}}
  async Task Test(string name,Func<Task> action){try{await action();results.Add(new{test=name,status="PASS"});}catch(Exception e){failed=true;results.Add(new{test=name,status="FAIL",detail=e.ToString()});}File.WriteAllText(Path.Combine(folder,"results.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));}
  try{
   if(!Program.VerificationOutput)throw new IOException("Rendering fault verification requires probe output");
   var display=VideoRenderer.Displays().First(d=>d.Primary!=0);await w.ApplySettingsAsync(new(new(),display));
   var row=new CueRow{FilePath=Path.Combine(Program.Option("--fixtures")!,"h264-aac.mp4")};await ShowProgramService.RefreshAsync(row,CancellationToken.None);w.Editor.Add(row);
   await w.Coordinator.ConfigureAudioAsync(true,"",.5f,false,true);await w.Preview.LoadAsync(row.FilePath);await w.Preview.PlayAsync();await Until(()=>w.SharedAudio.State==SharedAudioState.Playing);
   var canvas=w.Surface.Canvas;
   await Test("preview-only failure is reported without restarting playback or audio",async()=>{
    var video=w.Preview.PlaybackSessionId;int decodes=w.Preview.DecodeStarts;var sink=w.SharedAudio.SinkSession;long before=canvas.FrameId;
    canvas.PreviewResultForTest=_=>unchecked((int)0x80004005);try{canvas.UpdatePreview();}finally{canvas.PreviewResultForTest=null;}
    await Until(()=>w.FindControl<TextBlock>("PreviewInfo")!.Text!.Contains("プレビュー描画エラー"));await Task.Delay(160);
    Check(canvas.PreviewError==unchecked((int)0x80004005)&&canvas.SharedDeviceError==0,"wrong fault scope");
    Check(canvas.FrameId>before&&w.Preview.Snapshot.State==PreviewState.Playing&&w.Preview.PlaybackSessionId==video&&w.Preview.DecodeStarts==decodes,"preview-only failure stopped video source");
    Check(w.SharedAudio.State==SharedAudioState.Playing&&w.SharedAudio.SinkSession==sink,"preview-only failure stopped audio");
   });
   await Test("external output can attach and detach while preview is faulted",async()=>{
    var session=canvas.SessionId;var video=w.Preview.PlaybackSessionId;await w.ArmAsync();long before=canvas.FrameId;await Task.Delay(160);
    Check(w.IsArmed&&canvas.OutputAttached&&canvas.FrameId>before&&w.Preview.PlaybackSessionId==video,"faulted preview blocked external output");
    await w.DisarmAsync();Check(!canvas.OutputAttached&&canvas.SessionId==session&&w.SharedAudio.State==SharedAudioState.Playing,"detach changed audio/canvas");
   });
   // Explicit attachment exercises a successful preview reinitialization. Product
   // code does not automatically rebuild a preview or GPU device on failure.
   canvas.AttachPreview(w.Surface.ProgramWindowForVerification);
   await Test("preview-only failure during external output leaves output attached",async()=>{
    await w.Preview.StopAsync();await w.Preview.PlayAsync();await Until(()=>w.SharedAudio.State==SharedAudioState.Playing);await w.ArmAsync();
    long before=canvas.FrameId;var video=w.Preview.PlaybackSessionId;var sink=w.SharedAudio.SinkSession;
    canvas.PreviewResultForTest=_=>unchecked((int)0x8007000E);try{canvas.UpdatePreview();}finally{canvas.PreviewResultForTest=null;}
    await Task.Delay(200);Check(w.IsArmed&&canvas.OutputAttached&&canvas.FrameId>before&&w.Preview.PlaybackSessionId==video&&w.SharedAudio.SinkSession==sink&&w.SharedAudio.State==SharedAudioState.Playing,"local resource fault stopped external output/audio");
    Check(w.FindControl<TextBlock>("PreviewInfo")!.Text!.Contains("プレビュー描画エラー"),"local fault notification overwritten");await w.DisarmAsync();
   });
   canvas.AttachPreview(w.Surface.ProgramWindowForVerification);
   await Test("display topology change hides output and detaches without resetting video or audio",async()=>{
    foreach(var mode in new[]{OutputPresentationMode.Standard,OutputPresentationMode.Compatibility}){
     await w.ApplySettingsAsync(new(new(OutputMode:mode),display));
     await w.Preview.LoadAsync(row.FilePath);await w.Preview.PlayAsync();await Until(()=>w.SharedAudio.State==SharedAudioState.Playing);
     var active=w.Surface.Canvas;var session=active.SessionId;var video=w.Preview.PlaybackSessionId;var sink=w.SharedAudio.SinkSession;
     await w.ArmAsync();long before=active.FrameId;active.SimulateDisplayChangeForTest();active.RefreshOutput();
     Check(active.OutputDisconnected,"display change was not latched");
     await Until(()=>!w.IsArmed);await Task.Delay(180);
     Check(!active.OutputAttached&&w.Surface.Canvas.SessionId==session,"display change replaced canvas or left output attached");
     Check(active.FrameId>before&&w.Preview.PlaybackSessionId==video&&w.Preview.Snapshot.State==PreviewState.Playing,"display change reset video");
     Check(w.SharedAudio.SinkSession==sink&&w.SharedAudio.State==SharedAudioState.Playing,"display change stopped audio");
    }
   });
   await Test("lost shared GPU is reported and stops the affected video source and output",async()=>{
    await w.ArmAsync();var session=canvas.SessionId;const int removed=unchecked((int)0x887A0005);
    // A generic preview error with a removed-device reason must be treated as
    // a shared failure, not mistaken for an isolated swap-chain problem.
    canvas.PreviewResultForTest=_=>unchecked((int)0x80004005);canvas.DeviceReasonForTest=()=>removed;bool caught=false;
    try{canvas.UpdatePreview();}catch(SharedGraphicsException){caught=true;}finally{canvas.PreviewResultForTest=null;canvas.DeviceReasonForTest=null;}
    await Until(()=>!w.IsArmed&&w.Preview.Snapshot.State==PreviewState.Stopped&&w.SharedAudio.State==SharedAudioState.Waiting);
    Check(caught&&canvas.SharedDeviceError==removed&&!canvas.OutputAttached&&w.Surface.Canvas.SessionId==session,"shared fault handling failed");
    Check(w.FindControl<TextBlock>("PreviewInfo")!.Text!.Contains("共通GPU描画エラー")&&w.DeliveryBlockReason().Contains("GPU"),"shared fault scope/restart notice missing");
    Check(!w.FindControl<Button>("ArmButton")!.IsEnabled&&AudioDecodeSession.ActiveDecoders==0,"fault left output action or stale audio enabled");
   });
   await Test("shared GPU failure during LTC stops once and does not auto-restart",async()=>{
    // Rebuild only the isolated verification canvas to test another failure epoch.
    w.Surface.ConfigureCanvas(640,360);w.Surface.ConfigureCanvas(display.Width,display.Height);var ltcCanvas=w.Surface.Canvas;
    await w.Monitor.ConfigureAsync(new(Rate:"25"),async(clock,token)=>{int i=10;while(!token.IsCancellationRequested){int f=i++%100;clock.Accept(new TcFrame{Seconds=f/25,Frame=f%25},MasterClock.Timestamp());await Task.Delay(40,token);}});
    await Until(()=>w.Coordinator.Observation?.FramePts>0&&w.SharedAudio.State==SharedAudioState.Playing);await w.ArmAsync();
    ltcCanvas.PreviewResultForTest=_=>unchecked((int)0x80004005);try{ltcCanvas.UpdatePreview();}finally{ltcCanvas.PreviewResultForTest=null;}
    ltcCanvas.ExternalErrorForTest=_=>unchecked((int)0x887A0007);try{ltcCanvas.RefreshOutput();}catch(SharedGraphicsException){}finally{ltcCanvas.ExternalErrorForTest=null;}
    await Until(()=>!w.IsArmed&&!w.Coordinator.OwnsSurface&&w.SharedAudio.State==SharedAudioState.Waiting);await Task.Delay(250);
    Check(w.SynchronizationSession==null&&ltcCanvas.SharedDeviceError<0&&!ltcCanvas.OutputAttached&&w.Monitor.Clock.Read().State!=SyncState.LOST,"failed GPU session auto-restarted or stopped input monitoring");
    Check(w.FindControl<TextBlock>("PreviewInfo")!.Text!.Contains("共通GPU描画エラー"),"LTC failure notification overwritten");
   });
   await Test("legacy renderer accepts DXGI success statuses but rejects actual errors",()=>{
    var check=typeof(VideoRenderer).GetMethod("Check",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!;
    foreach(int status in new[]{0,1,0x087A0001})check.Invoke(null,new object[]{status});
    bool rejected=false;try{check.Invoke(null,new object[]{unchecked((int)0x80004005)});}catch(System.Reflection.TargetInvocationException e)when(e.InnerException is IOException){rejected=true;}Check(rejected,"actual DXGI failure was ignored");
    rejected=false;try{check.Invoke(null,new object[]{unchecked((int)0x800704C7)});}catch(System.Reflection.TargetInvocationException e)when(e.InnerException is OperationCanceledException){rejected=true;}Check(rejected,"output cancellation was ignored");return Task.CompletedTask;
   });
   await Test("device-loss codes are distinguished from local errors",()=>{
    foreach(uint code in new uint[]{0x887A0005,0x887A0006,0x887A0007,0x887A0020})Check(RenderSession.IsDeviceFailure(unchecked((int)code)),"device failure not recognized");
    Check(!RenderSession.IsDeviceFailure(unchecked((int)0x80004005))&&!RenderSession.IsDeviceFailure(unchecked((int)0x8007000E)),"local failure classified as shared without device evidence");return Task.CompletedTask;
   });
  }catch(Exception e){failed=true;File.WriteAllText(Path.Combine(folder,"fatal.txt"),e.ToString());}
  finally{await w.DisarmAsync();await w.Coordinator.DisposeAsync();await w.Preview.DisposeAsync();await w.Monitor.DisposeAsync();MainWindow.Shutdown(failed?1:0);}
 }
}
