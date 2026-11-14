using Avalonia.Controls;
using Avalonia.VisualTree;
using System.Text.Json;
using LtcPlayer.App;
namespace LtcPlayer.Avalonia;
public static class LostEditingVerification
{
 public static async Task RunAsync(MainWindow w,string folder)
 {
  Directory.CreateDirectory(folder);var results=new List<object>();bool failed=false;int receiving=1;
  void Check(bool value,string why){if(!value)throw new IOException(why);}
  async Task Until(Func<bool> condition){double end=MasterClock.Timestamp()+10;while(!condition()){if(MasterClock.Timestamp()>end)throw new TimeoutException(JsonSerializer.Serialize(w.LatestObservation));await Task.Delay(10);}}
  async Task Test(string name,Func<Task> body){try{await body();results.Add(new{test=name,status="PASS"});}catch(Exception e){failed=true;results.Add(new{test=name,status="FAIL",detail=e.ToString()});}File.WriteAllText(Path.Combine(folder,"results.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));}
  var a=new CueRow{FilePath=Path.Combine(Program.Option("--fixtures")!,"hap-pcm.mov")};var b=a.CloneDisabled();b.SetStart("00:00:20:00");
  try{
   await w.ApplySettingsAsync(new(new(Rate:"25"),VideoRenderer.Displays().First(d=>d.Primary!=0)));
   foreach(var row in new[]{a,b}){await ShowProgramService.RefreshAsync(row,CancellationToken.None);w.Editor.Add(row);}
   await w.SetSharedAudioAsync(true,silenceForVerification:true);
   await w.ArmAsync(async(clock,t)=>{double start=MasterClock.Timestamp();while(!t.IsCancellationRequested){if(Volatile.Read(ref receiving)!=0){int f=30+(int)((MasterClock.Timestamp()-start)*25)%30;clock.Accept(new TcFrame{Seconds=f/25,Frame=f%25},MasterClock.Timestamp());}await Task.Delay(20,t);}});
   await Until(()=>w.LatestObservation?.CueId==a.CueId&&w.LatestObservation.FramePts>0);
   await Test("LOST unlocks TC without manual play and keeps held seek disabled",async()=>{
    Check(!w.ProgramEditAccess.Allowed,"LOCKED edit allowed");Volatile.Write(ref receiving,0);await Until(()=>w.Monitor.Clock.Read().State==SyncState.LOST&&w.ProgramEditAccess.Allowed);await Task.Delay(100);
    var box=w.FindControl<DataGrid>("CueGrid")!.GetVisualDescendants().OfType<TextBox>().First(x=>x.DataContext==a);
    Check(box.IsEnabled&&box.Focusable&&!w.SeekAccess.Allowed&&w.Preview.IsSyncOwned,"LOST permissions wrong");
   });
   await Test("TC and enabled edits keep held source identity pixels and position",async()=>{
    var sync=w.SynchronizationSession;var pts=w.LatestObservation!.FramePts;var pixels=w.Surface.Canvas.Readback();
    w.Editor.SetTimecode(a,["00","00","20","00"]);w.Editor.SetTimecode(b,["00","00","00","00"]);w.Editor.SetEnabled(b,true);await Task.Delay(220);
    Check(w.SynchronizationSession==sync&&w.LatestObservation?.CueId==a.CueId&&w.LatestObservation.FramePts==pts&&pixels.AsSpan().SequenceEqual(w.Surface.Canvas.Readback()),"schedule edit changed held image");
    w.Editor.SetEnabled(b,false);
   });
   await Test("deleting a different cue keeps held image",async()=>{
    var pixels=w.Surface.Canvas.Readback();var sync=w.SynchronizationSession;await w.DeleteCueAsync(b);await Task.Delay(100);
    Check(!w.Editor.Rows.Contains(b)&&w.SynchronizationSession==sync&&w.LatestObservation?.CueId==a.CueId&&pixels.AsSpan().SequenceEqual(w.Surface.Canvas.Readback()),"unrelated delete cleared hold");
   });
   await Test("resumed LTC uses edited schedule and locks editing again",async()=>{
    Volatile.Write(ref receiving,1);await Until(()=>w.Monitor.Clock.Read().State is SyncState.LOCKED or SyncState.RESYNC&&w.LatestObservation is {CueId:null,FramePts:null});Check(!w.ProgramEditAccess.Allowed,"resume did not lock editing");
    Volatile.Write(ref receiving,0);await Until(()=>w.Monitor.Clock.Read().State==SyncState.LOST);w.Editor.SetTimecode(a,["00","00","01","00"]);
    Volatile.Write(ref receiving,1);await Until(()=>w.LatestObservation?.ScheduledCue?.Start==1&&w.LatestObservation.FramePts!=null);Check(w.LatestObservation?.CueId==a.CueId,"resume ignored edited start");
   });
   await Test("deleting held cue clears source and audio but preserves output and cover",async()=>{
    Volatile.Write(ref receiving,0);await Until(()=>w.Monitor.Clock.Read().State==SyncState.LOST);await Task.Delay(100);var canvas=w.Surface.Canvas.SessionId;
    w.SetFadeSeconds(0);w.SetCoverVisible(true);await w.DeleteCueAsync(a);await Until(()=>!w.Coordinator.OwnsSurface&&w.Coordinator.Audio.State==SharedAudioState.Waiting);
    Check(!w.Editor.Rows.Contains(a)&&w.Preview.LoadedPath==""&&w.IsOutputActive&&w.Cover.IsCovered&&w.Surface.Canvas.SessionId==canvas,"held deletion lost resources or retained source");w.SetCoverVisible(false);
    Check(w.Surface.Canvas.Readback().Where((_,i)=>i%4!=3).All(v=>v==0),"held deletion not black");
   });
   w.Editor.SetTimecode(a,["00","00","00","00"]);w.Editor.Add(a);Volatile.Write(ref receiving,1);await Until(()=>w.LatestObservation?.CueId==a.CueId&&w.Coordinator.OwnsSurface);
   Volatile.Write(ref receiving,0);await Until(()=>w.Monitor.Clock.Read().State==SyncState.LOST);await Task.Delay(100);
   await Test("LTC resuming during held deletion cancels deletion and rejects late clear",async()=>{
    int generation=w.Preview.SourceGeneration;
    w.Coordinator.BeforeHeldClearForTest=async()=>{Volatile.Write(ref receiving,1);await Until(()=>w.Monitor.Clock.Read().State is SyncState.LOCKED or SyncState.RESYNC);};
    try{await w.DeleteCueAsync(a);}finally{w.Coordinator.BeforeHeldClearForTest=null;}
    await Until(()=>w.Coordinator.OwnsSurface&&w.LatestObservation?.CueId==a.CueId&&w.LatestObservation.FramePts>0);
    Check(w.Editor.Rows.Contains(a)&&w.Preview.SourceGeneration==generation&&w.IsOutputActive&&!w.ProgramEditAccess.Allowed,"late clear or deletion overrode new LTC");
   });
  }catch(Exception e){failed=true;File.WriteAllText(Path.Combine(folder,"fatal.txt"),e.ToString());}
  finally{w.Coordinator.BeforeHeldClearForTest=null;Volatile.Write(ref receiving,0);await w.DisarmAsync();MainWindow.Shutdown(failed?1:0);}
 }
}
