using System.Text.Json;
using LtcPlayer.App;
namespace LtcPlayer.Avalonia;
public static class LiveStartupVerification
{
 public static async Task RunAsync(MainWindow window,string folder){
  Directory.CreateDirectory(folder);Exception? failure=null;var stages=new List<object>();OutputBounds? bounds=null;
  async Task Until(Func<bool> condition,int milliseconds){double start=MasterClock.Timestamp();while(!condition()){if((MasterClock.Timestamp()-start)*1000>milliseconds)throw new TimeoutException("Live input/output condition timed out");await Task.Delay(20);}}
  void Record(string name){stages.Add(new{Stage=name,Clock=window.Monitor.Clock.Read(),Observation=window.LatestObservation,WindowBounds=bounds is {} b?new{b.Left,b.Top,b.Right,b.Bottom}:null,HostSeconds=MasterClock.Timestamp()});File.WriteAllText(Path.Combine(folder,"stages.json"),JsonSerializer.Serialize(stages,new JsonSerializerOptions{WriteIndented=true}));}
  try{
   if(!Program.VerificationOutput&&!(InputSettings.Load(Program.Option("--settings")).OutputMode==OutputPresentationMode.Compatibility&&Program.Arguments.Contains("--diagnostic-fullscreen-test")))throw new InvalidOperationException("Full-screen test requires the explicitly authorized diagnostic candidate");
   ProgramPlayback.ProbeObserver=r=>{if(bounds==null)bounds=r.Bounds();};
   if(window.Editor.Rows.Count==0)await DefaultTestShow.LoadAsync(window.Editor);
   Record("waiting for physical LTC around 24 seconds");
   await Until(()=>window.Monitor.Clock.Read() is {State:SyncState.LOCKED,Seconds:>=24 and <28},90000);
   Record("start requested while LTC already received / cue in progress");
   await window.ArmAsync();
   await Until(()=>window.LatestObservation is {Presented:>20,Monitor.Presented:>0},20000);
   Record("video and embedded monitor updating");
   await Task.Delay(8000);Record("eight seconds of continued output");
   window.ConfirmStopForTest=_=>Task.FromResult(true);await window.RequestStopAsync(false);Record("output stopped normally");
  }catch(Exception e){failure=e;}
  finally{ProgramPlayback.ProbeObserver=null;await window.DisarmAsync();await window.Monitor.DisposeAsync();File.WriteAllText(Path.Combine(folder,"results.json"),JsonSerializer.Serialize(new{Passed=failure==null,Error=failure?.ToString(),Stages=stages},new JsonSerializerOptions{WriteIndented=true}));MainWindow.Shutdown(failure==null?0:1);}
 }
}
