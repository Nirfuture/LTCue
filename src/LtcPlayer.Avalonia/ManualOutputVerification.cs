using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using LtcPlayer.App;
namespace LtcPlayer.Avalonia;
public static class ManualOutputVerification
{
 static void Check(bool value,string message){if(!value)throw new IOException(message);}
 public static async Task RunAsync(MainWindow w,string folder){
  Directory.CreateDirectory(folder);var results=new List<object>();bool failed=false;int receiving=0;
  async Task Until(Func<bool> test){for(int i=0;i<1000;i++){if(test())return;await Task.Delay(10);}throw new TimeoutException(JsonSerializer.Serialize(w.LatestObservation));}
  async Task Test(string name,Func<Task> body){try{await body();results.Add(new{test=name,status="PASS"});}catch(Exception e){failed=true;results.Add(new{test=name,status="FAIL",detail=e.ToString()});}File.WriteAllText(Path.Combine(folder,"results.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));}
  var a=new CueRow{FilePath=Program.Option("--media")!};var b=new CueRow{FilePath=a.FilePath};b.SetStart("00:00:20:00");
  Button Button(CueRow cue,string cls)=>w.FindControl<DataGrid>("CueGrid")!.GetVisualDescendants().OfType<Button>().First(x=>ReferenceEquals(x.DataContext,cue)&&x.Classes.Contains(cls));
  void Click(CueRow cue,string cls)=>Button(cue,cls).RaiseEvent(new RoutedEventArgs(global::Avalonia.Controls.Button.ClickEvent));
  try{
   await ShowProgramService.RefreshAsync(a,CancellationToken.None);await ShowProgramService.RefreshAsync(b,CancellationToken.None);w.Editor.Add(a);w.Editor.Add(b);
   await w.ApplySettingsAsync(new(new(Rate:"29.97 DF",OutputMode:OutputPresentationMode.Compatibility),VideoRenderer.Displays().First()));
   await w.ArmAsync(async(clock,token)=>{int i=45;while(!token.IsCancellationRequested){if(Volatile.Read(ref receiving)!=0){int f=Interlocked.Increment(ref i);clock.Accept(new TcFrame{Seconds=f/30,Frame=f%30,Drop=1},MasterClock.Timestamp());}await Task.Delay(33,token);}});
   await Test("WAITING permits row play; editing remains locked",async()=>{await Until(()=>w.CanManualOutput&&Button(b,"cue-play").IsEnabled);Check(w.Editor.Locked&&w.FindControl<DataGrid>("CueGrid")!.IsReadOnly,"editing unlocked");Click(b,"cue-play");await Until(()=>w.LatestObservation is {Manual:true,FramePts:>.15} t&&t.CueId==b.CueId);});
   await Test("actual output cue highlighted independently of selection",async()=>{await Task.Delay(150);var grid=w.FindControl<DataGrid>("CueGrid")!;grid.SelectedItem=a;var row=grid.GetVisualDescendants().OfType<DataGridRow>().First(x=>ReferenceEquals(x.DataContext,b));Check(row.Background?.ToString()=="#ff205c43","output highlight absent: "+row.Background);if(Program.Arguments.Contains("--visual-manual")){grid.SelectedItem=b;w.Title="LTCue — manual verification";await Task.Delay(30000);}});
   await Test("monitor exceeds former 15fps ceiling",async()=>{await Until(()=>w.LatestObservation?.Monitor?.Presented>5);ulong before=w.LatestObservation!.Monitor!.Presented;double stamp=MasterClock.Timestamp();await Task.Delay(2300);ulong after=w.LatestObservation!.Monitor!.Presented;double fps=(after-before)/(MasterClock.Timestamp()-stamp);Check(fps>17,"monitor fps="+fps);File.WriteAllText(Path.Combine(folder,"monitor-rate.json"),JsonSerializer.Serialize(new{fps,before,after}));});
   await Test("stop holds same frame; replay restarts at beginning",async()=>{Click(b,"cue-stop");await Until(()=>w.LatestObservation is {Manual:true,Holding:true});double? pts=w.LatestObservation!.FramePts;await Task.Delay(220);Check(w.LatestObservation!.FramePts==pts,"stop advanced");Click(b,"cue-play");await Until(()=>w.LatestObservation is {Manual:true,Holding:false,FramePts:<.5} t&&t.CueId==b.CueId);});
   await Test("LTC resumption takes priority and disables manual buttons",async()=>{Volatile.Write(ref receiving,1);await Until(()=>w.LatestObservation is {Manual:false,State:SyncState.LOCKED} t&&t.CueId==a.CueId);await Until(()=>!Button(b,"cue-play").IsEnabled);w.PlayManual(b);await Task.Delay(180);Check(w.LatestObservation is {Manual:false} t&&t.CueId==a.CueId,"manual bypassed input");});
   await Test("LOST permits different manual cue; repeated receive resumes LTC",async()=>{Volatile.Write(ref receiving,0);await Until(()=>w.Monitor.Clock.Read().State==SyncState.LOST&&Button(b,"cue-play").IsEnabled);Click(b,"cue-play");await Until(()=>w.LatestObservation is {Manual:true,FramePts:>.1} t&&t.CueId==b.CueId);Volatile.Write(ref receiving,1);await Until(()=>w.LatestObservation is {Manual:false} t&&t.CueId==a.CueId);});
   await Test("manual end holds final frame instead of advancing schedule",async()=>{Volatile.Write(ref receiving,0);await Until(()=>w.CanManualOutput);var previous=w.Preview.PlaybackSessionId;Click(b,"cue-play");await Until(()=>w.Preview.PlaybackSessionId!=previous&&w.Preview.Snapshot.State==PreviewState.Playing&&w.LatestObservation is {Manual:true,Holding:false} t&&t.CueId==b.CueId);await Until(()=>w.Preview.Snapshot.State==PreviewState.Ended&&w.LatestObservation is {Manual:true,Holding:true} t&&t.CueId==b.CueId);Check(w.LatestObservation!.FramePts>b.Duration-.1,"not final frame");double? pts=w.LatestObservation.FramePts;await Task.Delay(200);Check(w.LatestObservation.FramePts==pts,"end did not hold");});
   await Test("output stop/restart preserves held manual frame",async()=>{var canvas=w.Surface.Canvas.SessionId;var pts=w.Preview.Snapshot.FramePts;var image=w.Surface.Canvas.Readback();await w.DisarmAsync();Check(!w.IsArmed,"stop failed");double requested=MasterClock.Timestamp();await w.ArmAsync();await Until(()=>w.LatestObservation is {Manual:true,Holding:true} t&&t.HostSeconds>requested);Check(w.Surface.Canvas.SessionId==canvas&&w.Preview.Snapshot.FramePts==pts&&w.Preview.Snapshot.State==PreviewState.Ended&&image.SequenceEqual(w.Surface.Canvas.Readback()),"output toggle changed held source/frame");await w.DisarmAsync();});
  }finally{await w.DisarmAsync();await w.Monitor.DisposeAsync();MainWindow.Shutdown(failed?1:0);}
 }
}
