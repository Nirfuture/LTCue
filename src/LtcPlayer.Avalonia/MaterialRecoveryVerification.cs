using System.Text.Json;
using Avalonia.Controls;
using LtcPlayer.App;
namespace LtcPlayer.Avalonia;

public static class MaterialRecoveryVerification
{
 public static async Task RunAsync(MainWindow w,string folder)
 {
  Directory.CreateDirectory(folder);var results=new List<object>();bool failed=false;
  void Check(bool value,string reason){if(!value)throw new IOException(reason);}
  async Task Until(Func<bool> condition){for(int i=0;i<2500;i++){if(condition())return;await Task.Delay(10);}throw new TimeoutException(JsonSerializer.Serialize(new{w.Preview.Snapshot,w.LatestObservation,Rows=w.Editor.Rows.Select(r=>new{r.Name,r.IssueText})}));}
  async Task Test(string name,Func<Task> body){try{await body();results.Add(new{test=name,status="PASS"});}catch(Exception e){failed=true;results.Add(new{test=name,status="FAIL",detail=e.ToString()});}File.WriteAllText(Path.Combine(folder,"results.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));}
  int frames=30;
  var a=new CueRow{FilePath=Program.Option("--media")!};
  var b=new CueRow{FilePath=Path.Combine(folder,"restored.mov"),Duration=10,FrameRate=29.97,Codec="hap"};b.SetStart("00:00:20:00");
  try{
   await w.ApplySettingsAsync(new(new(Rate:"29.97 DF"),VideoRenderer.Displays().First(d=>d.Primary!=0)));
   await Test("empty queue permits external connection",async()=>{await w.ArmAsync();Check(w.IsOutputActive,"empty connection failed");await w.DisarmAsync();});
   await ShowProgramService.RefreshAsync(a,CancellationToken.None);w.Editor.Add(a);w.Editor.Add(b);
   await w.ArmAsync(async(clock,token)=>{while(!token.IsCancellationRequested){int f=Volatile.Read(ref frames);clock.Accept(new TcFrame{Seconds=f/30,Frame=f%30,Drop=1},MasterClock.Timestamp());await Task.Delay(20,token);}});
   await Test("missing next source does not disconnect current output",async()=>{
    await Until(()=>b.SourceError!=""&&w.LatestObservation is {CueId:var id,FramePts:>0}&&id==a.CueId);
    Check(w.IsOutputActive&&a.SourceError=="","next source affected current");
   });
   await Test("restored source is automatically verified without replacing sync session",async()=>{
    var session=w.SynchronizationSession;File.Copy(a.FilePath,b.FilePath);await Until(()=>b.SourceError==""&&b.SourceRevision>0);
    Check(w.SynchronizationSession==session&&w.IsOutputActive,"recovery restarted session");
   });
   await Test("restored source joins at current LTC position",async()=>{
    Volatile.Write(ref frames,21*30);await Until(()=>w.LatestObservation?.CueId==b.CueId&&w.LatestObservation.FramePts>.8);Check(w.IsOutputActive,"restored output stopped");
   });
   await Test("verified current failure becomes black and silent without disconnect",async()=>{
    var cue=w.Editor.Timeline.Single(c=>c.CueId==b.CueId);w.Coordinator.FailMaterialForTest(cue,new IOException("Synthetic active read failure"));
    await Until(()=>b.SourceError!=""&&w.LatestObservation is {CueId:null,FramePts:null});
    Check(w.IsOutputActive&&!w.Coordinator.ReadAudioPlayhead().Advancing,"failed material still advancing");
    Check(w.Surface.Canvas.Readback().Where((_,i)=>i%4!=3).All(x=>x==0),"failed output was not black");
   });
   await w.DisarmAsync();await w.StopSynchronizationForTest();
   await Test("duplicate TC remains enabled but both excluded",()=>{
    b.SetStart(a.StartTimecode);w.Editor.Validate();Check(a.Enabled&&b.Enabled&&a.Error.Contains("重複")&&b.Error.Contains("重複")&&w.Editor.Timeline.Length==0,"duplicate semantics");
    Check(w.FindControl<Button>("ArmButton")!.IsEnabled,"duplicates blocked connection");return Task.CompletedTask;
   });
  }finally{await w.DisarmAsync();MainWindow.Shutdown(failed?1:0);}
 }
}
