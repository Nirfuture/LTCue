using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using System.Text.Json;
using LtcPlayer.App;
namespace LtcPlayer.Avalonia;
public static class CueEditingVerification
{
 public static async Task RunAsync(MainWindow w,string folder)
 {
  Directory.CreateDirectory(folder);var results=new List<object>();bool failed=false;int receiving=0;
  void Check(bool value,string why){if(!value)throw new IOException(why);}
  async Task Until(Func<bool> condition){double end=MasterClock.Timestamp()+12;while(!condition()){if(MasterClock.Timestamp()>end)throw new TimeoutException(JsonSerializer.Serialize(new{w.Preview.Snapshot,w.LatestObservation}));await Task.Delay(10);}}
  async Task Test(string name,Func<Task> body){try{await body();results.Add(new{test=name,status="PASS"});}catch(Exception e){failed=true;results.Add(new{test=name,status="FAIL",detail=e.ToString()});}File.WriteAllText(Path.Combine(folder,"results.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));}
  var grid=w.FindControl<DataGrid>("CueGrid")!;
  TextBox Box(CueRow row,string part)=>grid.GetVisualDescendants().OfType<TextBox>().First(b=>b.DataContext==row&&Equals(b.Tag,part));
  void PressKey(TextBox box,Key key)=>box.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Key=key});
  var a=new CueRow{FilePath=Path.Combine(Program.Option("--fixtures")!,"hap-pcm.mov")};
  var b=new CueRow{FilePath=Path.Combine(Program.Option("--fixtures")!,"h264-aac.mp4"),Enabled=false};b.SetStart("00:00:20:00");
  var c=b.CloneDisabled();c.SetStart("00:00:40:00");
  try{
   await w.ApplySettingsAsync(new(new(Rate:"25"),VideoRenderer.Displays().First(d=>d.Primary!=0)));
   foreach(var row in new[]{a,b,c}){await ShowProgramService.RefreshAsync(row,CancellationToken.None);w.Editor.Add(row);}
   await w.SetSharedAudioAsync(true,silenceForVerification:true);
   await w.Monitor.ConfigureAsync(new(Rate:"25"),async(clock,t)=>{int f=25;while(!t.IsCancellationRequested){if(Volatile.Read(ref receiving)!=0){int n=Interlocked.Increment(ref f);clock.Accept(new TcFrame{Seconds=n/25,Frame=n%25},MasterClock.Timestamp());}await Task.Delay(40,t);}});
   await w.PlayCueForTestAsync(a);await w.ArmAsync();await Task.Delay(150);
   await Test("manual external output enables TC and structural edit commands",()=>{
    Check(w.ProgramEditAccess.Allowed&&!grid.IsReadOnly&&Box(b,"Seconds").IsEnabled&&Box(b,"Seconds").Focusable&&w.FindControl<Button>("AddCueButton")!.IsEnabled,"manual editing disabled");return Task.CompletedTask;
   });
   await Test("TC typing remains detached until Enter and publishes once",async()=>{
    var box=Box(b,"Seconds");box.Focus();box.Text="2";await Task.Delay(40);Check(b.Seconds=="20","partial TC published");box.Text="25";await Task.Delay(40);
    int changes=0;void Changed()=>changes++;w.Editor.PlaybackChanged+=Changed;PressKey(box,Key.Enter);w.Editor.PlaybackChanged-=Changed;Check(b.Seconds=="25"&&changes<=1,"commit failed or published intermediate fields");
   });
   await Test("Escape cancels TC draft",async()=>{var box=Box(b,"Seconds");box.Focus();box.Text="55";await Task.Delay(30);PressKey(box,Key.Escape);Check(b.Seconds=="25"&&box.Text=="25","cancel failed");});
   await Test("editing another cue preserves current video audio and output",async()=>{
    var video=w.Preview.PlaybackSessionId;var sink=w.Coordinator.Audio.SinkSession;var canvas=w.Surface.Canvas.SessionId;int preps=w.Coordinator.Audio.PreparationStarts;
    w.Editor.SetTimecode(b,["00","00","26","00"]);w.Editor.SetEnabled(b,true);w.Editor.SetEnabled(b,false);w.Editor.Clone(b);await w.DeleteCueAsync(c);await Task.Delay(100);
    Check(w.Preview.PlaybackSessionId==video&&w.Coordinator.Audio.SinkSession==sink&&w.Coordinator.Audio.PreparationStarts==preps&&w.Surface.Canvas.SessionId==canvas&&w.Preview.Snapshot.State==PreviewState.Playing,"unrelated edit interrupted playback");
   });
   await Test("LTC acquisition cancels draft removes focus and disables TC controls",async()=>{
    var box=Box(b,"Seconds");box.Focus();box.Text="58";await Task.Delay(30);Volatile.Write(ref receiving,1);await Until(()=>w.Preview.IsSyncOwned&&!box.IsEnabled);
    Check(b.Seconds=="26"&&box.Text=="26"&&!box.Focusable&&!box.IsFocused&&!w.ProgramEditAccess.Allowed,"draft survived LTC ownership");Check(!box.Focus(),"disabled TC accepted focus");
    using var image=new global::Avalonia.Media.Imaging.RenderTargetBitmap(new global::Avalonia.PixelSize((int)w.ClientSize.Width,(int)w.ClientSize.Height),new global::Avalonia.Vector(96,96));image.Render(w);image.Save(Path.Combine(folder,"disabled-tc.png"));
   });
   await Test("LTC locks definition changes in UI and mutation layer",()=>{
    bool rejected=false;try{w.Editor.Delete(b);}catch(InvalidOperationException){rejected=true;}
    Check(rejected&&w.Editor.Rows.Contains(b)&&!w.FindControl<Button>("DeleteCueButton")!.IsEnabled,"delete bypassed lock");
    rejected=false;try{w.Editor.SetTimecode(b,["00","00","59","00"]);}catch(InvalidOperationException){rejected=true;}Check(rejected&&b.Seconds=="26","TC bypassed lock");return Task.CompletedTask;
   });
   await Test("LTC reorder preserves schedule sync video and audio sessions",async()=>{
    await Until(()=>w.LatestObservation?.CueId==a.CueId&&w.Coordinator.Audio.State==SharedAudioState.Playing&&w.Coordinator.Audio.Generation==w.Coordinator.ReadAudioPlayhead().Generation&&w.Coordinator.Audio.CueId==a.CueId);await Task.Delay(120);
    var sync=w.SynchronizationSession;var canvas=w.Surface.Canvas.SessionId;var sink=w.Coordinator.Audio.SinkSession;int preps=w.Coordinator.Audio.PreparationStarts;
    Check(w.ReorderAccess.Allowed,"LTC reorder disabled");w.MoveCue(b,0);await Task.Delay(150);
    File.WriteAllText(Path.Combine(folder,"reorder.json"),JsonSerializer.Serialize(new{Before=new{sync,canvas,sink,preps},After=new{Sync=w.SynchronizationSession,Canvas=w.Surface.Canvas.SessionId,Audio=w.Coordinator.Audio,Head=w.Coordinator.ReadAudioPlayhead(),First=w.Editor.Rows[0].Name,Observation=w.LatestObservation}},new JsonSerializerOptions{WriteIndented=true}));
    Check(w.Editor.Rows[0]==b&&w.SynchronizationSession==sync&&w.Surface.Canvas.SessionId==canvas&&w.Coordinator.Audio.SinkSession==sink&&w.Coordinator.Audio.PreparationStarts==preps&&w.LatestObservation?.CueId==a.CueId,"reorder restarted synchronization");
   });
   await Test("LOST hold allows editing without enabling held-image seek",async()=>{
    Volatile.Write(ref receiving,0);await Until(()=>w.Monitor.Clock.Read().State==SyncState.LOST);await Task.Delay(120);Check(w.ProgramEditAccess.Allowed&&!w.SeekAccess.Allowed&&Box(b,"Seconds").IsEnabled,"LOST editing or held seek permission wrong");
    await w.PlayCueForTestAsync(a);await Until(()=>w.ProgramEditAccess.Allowed&&Box(b,"Seconds").IsEnabled);Check(Box(b,"Seconds").Focusable,"TC did not unlock");
   });
   await Test("import prepared before LTC acquisition is rejected at apply",async()=>{
    int count=w.Editor.Rows.Count;var entered=new TaskCompletionSource();var release=new TaskCompletionSource();w.Editor.BeforeImportApplyForTest=async()=>{entered.SetResult();await release.Task;};
    var import=w.Editor.ImportAsync([b.FilePath],"00:01:00:00");await entered.Task;Volatile.Write(ref receiving,1);await Until(()=>w.Preview.IsSyncOwned);release.SetResult();bool rejected=false;
    try{await import;}catch(InvalidOperationException){rejected=true;}finally{w.Editor.BeforeImportApplyForTest=null;}
    Check(rejected&&w.Editor.Rows.Count==count,"late import changed LTC schedule");
   });
   Volatile.Write(ref receiving,0);await Until(()=>w.Monitor.Clock.Read().State==SyncState.LOST);await w.PlayCueForTestAsync(a);await Until(()=>w.ProgramEditAccess.Allowed);
   await Test("deleting displayed source unloads to black silence and preserves connection",async()=>{
    var canvas=w.Surface.Canvas.SessionId;await w.DeleteCueAsync(a);await Until(()=>w.Coordinator.Audio.State==SharedAudioState.Waiting);
    Check(!w.Editor.Rows.Contains(a)&&w.Preview.LoadedPath==""&&w.Preview.Snapshot.State==PreviewState.Empty&&!w.SeekAccess.Allowed&&w.IsOutputActive&&w.Surface.Canvas.SessionId==canvas,"delete did not unload source");Check(w.Surface.Canvas.Readback().Where((_,i)=>i%4!=3).All(v=>v==0),"delete left visible frame");
   });
   await Test("deleting source under cover preserves cover state",async()=>{
    await w.PlayCueForTestAsync(b);w.SetFadeSeconds(0);w.SetCoverVisible(true);await w.DeleteCueAsync(b);Check(w.Cover.IsCovered&&w.IsOutputActive&&w.Preview.LoadedPath=="","delete removed cover or output");w.SetCoverVisible(false);
   });
   await Test("output off editing and reorder remain available",async()=>{await w.DisarmAsync();Check(w.ProgramEditAccess.Allowed&&w.ReorderAccess.Allowed&&!grid.IsReadOnly,"output off editing blocked");});
  }catch(Exception e){failed=true;File.WriteAllText(Path.Combine(folder,"fatal.txt"),e.ToString());}
  finally{Volatile.Write(ref receiving,0);await w.DisarmAsync();MainWindow.Shutdown(failed?1:0);}
 }
}

