using LtcPlayer.App;
using Avalonia.Controls;
using System.Text.Json;
namespace LtcPlayer.Avalonia;
public static class HapPreparationVerification
{
 public static async Task RunAsync(MainWindow w,string folder)
 {
  Directory.CreateDirectory(folder);var results=new List<object>();var samples=new List<object>();bool failed=false;int receiving=0;
  void Check(bool value,string why){if(!value)throw new IOException(why);}
  async Task Until(Func<bool> predicate){double end=MasterClock.Timestamp()+15;while(!predicate()){if(MasterClock.Timestamp()>end)throw new TimeoutException(JsonSerializer.Serialize(new{w.Preview.Snapshot,w.Preview.IsBusy,w.LatestObservation}));await Task.Delay(10);}}
  async Task Test(string name,Func<Task> body){try{await body();results.Add(new{test=name,status="PASS"});}catch(Exception e){failed=true;results.Add(new{test=name,status="FAIL",detail=e.ToString()});}File.WriteAllText(Path.Combine(folder,"results.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));}
  var a=new CueRow{FilePath=Program.Option("--media")!};var b=new CueRow{FilePath=Program.Option("--switch-media")??Path.Combine(Program.Option("--fixtures")!,"hap-q-silent.mov"),Enabled=false};b.SetStart("00:00:20:00");
  async Task Play(CueRow row){StartupMeasurementTrace.Begin("play-"+samples.Count);double start=MasterClock.Timestamp();long before=w.Preview.IndexCache.Builds;await w.PlayCueForTestAsync(row);await Until(()=>w.Preview.Snapshot.State==PreviewState.Playing&&!w.Preview.IsBusy);double elapsed=MasterClock.Timestamp()-start;await Task.Delay(60);Check(!w.Preview.IsBusy,"late preparation notification restored busy state");samples.Add(new{File=Path.GetFileName(row.FilePath),RequestToPlayingMs=elapsed*1000,Builds=w.Preview.IndexCache.Builds-before,w.Preview.IndexCache.Count,w.Preview.IndexCache.RetainedBytes});}
  try{
   await w.ApplySettingsAsync(new(new(Rate:"25"),VideoRenderer.Displays().First(d=>d.Primary!=0)));
   await w.Monitor.ConfigureAsync(new(Rate:"25"),async(clock,t)=>{double start=MasterClock.Timestamp();while(!t.IsCancellationRequested){if(Volatile.Read(ref receiving)!=0){int f=30+(int)((MasterClock.Timestamp()-start)*25)%30;clock.Accept(new TcFrame{Seconds=f/25,Frame=f%25},MasterClock.Timestamp());}await Task.Delay(20,t);}});
   foreach(var row in new[]{a,b}){await ShowProgramService.RefreshAsync(row,CancellationToken.None);w.Editor.Add(row);}
   await w.SetSharedAudioAsync(true,silenceForVerification:true);await w.ArmAsync();
   await Test("selection performs no HAP preparation",async()=>{var grid=w.FindControl<DataGrid>("CueGrid")!;long builds=w.Preview.IndexCache.Builds;grid.SelectedItem=a;grid.SelectedItem=b;await Task.Delay(120);Check(w.Preview.IndexCache.Builds==builds&&w.Preview.LoadedPath=="","selection prepared source");});
   await Test("first HAP play finishes preparation and clears activity",async()=>{await Play(a);Check(w.Preview.IndexCache.Builds==1&&w.Preview.LoadedPath==a.FilePath,"first index not retained");});
   await Test("stop then different HAP file plays on same output",async()=>{var canvas=w.Surface.Canvas.SessionId;await w.Preview.StopAsync();await Play(b);Check(w.Preview.LoadedPath==b.FilePath&&w.IsOutputActive&&w.Surface.Canvas.SessionId==canvas,"stop blocked different source");});
   await Test("A B A switching reuses analysis without another index build",async()=>{long builds=w.Preview.IndexCache.Builds;await w.Preview.StopAsync();await Play(a);Check(w.Preview.IndexCache.Builds==builds,"switch rebuilt A index");});
   await Test("independent decoder reuses session analysis and matches fresh frame",async()=>{
    long builds=w.Preview.IndexCache.Builds;VideoFrame? shared=null,fresh=null;
    await foreach(var frame in new MediaDecoder(reuseAnalysis:true,indexCache:w.Preview.IndexCache).DecodeAsync(a.FilePath,320,180,CancellationToken.None,1)){shared=frame;break;}
    await foreach(var frame in new MediaDecoder().DecodeAsync(a.FilePath,320,180,CancellationToken.None,1)){fresh=frame;break;}
    Check(w.Preview.IndexCache.Builds==builds&&shared!=null&&fresh!=null&&shared.Seconds==fresh.Seconds&&shared.Pixels.AsSpan().SequenceEqual(fresh.Pixels),"shared index changed frame or rebuilt");
   });
   await Test("LTC acquisition cancels old load and its progress notifications",async()=>{
    var entered=new TaskCompletionSource();var release=new TaskCompletionSource();w.Preview.LoadBeforeFrameForTest=async _=>{entered.SetResult();await release.Task;};var pending=w.PlayCueForTestAsync(b);
    try{await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));Volatile.Write(ref receiving,1);await Until(()=>w.Preview.IsSyncOwned);}finally{release.TrySetResult();await pending;w.Preview.LoadBeforeFrameForTest=null;}
    await Until(()=>w.LatestObservation?.CueId==a.CueId&&w.LatestObservation.FramePts!=null&&!w.Preview.IsBusy);await Task.Delay(100);Check(!w.Preview.IsBusy,"old activity survived ownership change");
   });
   await Test("output toggles preserve analysis and held synchronized image",async()=>{
    Volatile.Write(ref receiving,0);await Until(()=>w.Monitor.Clock.Read().State==SyncState.LOST);var canvas=w.Surface.Canvas.SessionId;var sync=w.SynchronizationSession;long builds=w.Preview.IndexCache.Builds;
    await w.DisarmAsync();await w.ArmAsync();Check(w.Surface.Canvas.SessionId==canvas&&w.SynchronizationSession==sync&&w.Preview.IndexCache.Builds==builds&&w.IsOutputActive,"output toggle discarded analysis or hold");
   });
  }catch(Exception e){failed=true;File.WriteAllText(Path.Combine(folder,"fatal.txt"),e.ToString());}
  finally{File.WriteAllText(Path.Combine(folder,"trace.json"),JsonSerializer.Serialize(StartupMeasurementTrace.Snapshot()));StartupMeasurementTrace.End();File.WriteAllText(Path.Combine(folder,"timings.json"),JsonSerializer.Serialize(samples,new JsonSerializerOptions{WriteIndented=true}));w.Preview.LoadBeforeFrameForTest=null;Volatile.Write(ref receiving,0);await w.DisarmAsync();MainWindow.Shutdown(failed?1:0);}
 }
}
