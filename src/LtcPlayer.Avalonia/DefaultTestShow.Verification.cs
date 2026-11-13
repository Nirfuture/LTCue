using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using LtcPlayer.App;
namespace LtcPlayer.Avalonia;
public static partial class DefaultTestShow
{
 public static async Task VerifyAsync(MainWindow w,string folder)
 {
  Directory.CreateDirectory(folder);var results=new List<object>();bool failed=false;var button=w.FindControl<Button>("ArmButton")!;int position=5;
  void Check(bool ok,string message){if(!ok)throw new IOException(message);}
  async Task Test(string name,Func<Task<object>> body){try{results.Add(new{test=name,status="PASS",detail=await body()});}catch(Exception e){failed=true;results.Add(new{test=name,status="FAIL",detail=e.ToString()});}File.WriteAllText(Path.Combine(folder,"results.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));}
  async Task Until(Func<bool> condition){for(int i=0;i<600;i++){if(condition())return;await Task.Delay(10);}throw new TimeoutException(JsonSerializer.Serialize(w.LatestObservation));}
  try{
   await Test("diagnostic I/O failure does not stop caller / session cap",async()=>{string blocked=Path.Combine(folder,"not-a-directory");File.WriteAllText(blocked,"fixture");using(var log=new SyncRunLog(blocked)){log.Write(new{test="continue"});Check(log.Error!=null,"I/O failure not observed");}string dir=Path.Combine(folder,"bounded-log");string path;using(var log=new SyncRunLog(dir)){path=log.FileName;for(int i=0;i<4;i++){log.Write(new{data=new string('x',4*1024*1024)});await Task.Delay(120);}}Check(new FileInfo(path).Length<=8*1024*1024,"session log exceeded cap");return new{Bytes=new FileInfo(path).Length,IoFailureContained=true};});
   await Test("normal startup has four enabled numbered HAP cues and live delivery button",()=>{
    Check(w.Editor.Rows.Count==4,"wrong default count");for(int i=0;i<4;i++){var row=w.Editor.Rows[i];Check(row.Enabled&&row.Name==$"HAP_{i+1}_1080p_2997_10s.mov"&&row.StartTimecode==$"00:00:{Starts[i]:00}:00"&&row.Error=="","wrong default cue");}Check(w.Editor.CanArm&&button.IsEnabled,"default cannot deliver");return Task.FromResult<object>(w.Editor.Rows.Select(r=>new{r.Name,r.StartTimecode,r.Enabled,r.Duration,r.FrameRate,r.Codec}).ToArray());
   });
   await Test("schedule selects all four clips and black gaps",()=>{
    var cues=ShowProgramService.Validate(w.Editor.Rows,w.Editor.Rate);var schedule=new CueScheduler(cues);
    for(int i=0;i<4;i++){Check(schedule.Select(cues[i].Start+.1)?.CueId==cues[i].CueId,"cue selection wrong");Check(schedule.Select(cues[i].Start+cues[i].Duration+.1)==null,"gap not black");}Check(schedule.Select(0)==null,"pre-roll not black");return Task.FromResult<object>(new{Cues=4,BlackGaps=5});
   });
   await Test("delivery button starts / four synthetic LTC positions / cancel stop / stop",async()=>{
    var display=VideoRenderer.Displays().First(d=>d.Primary!=0);await w.ApplySettingsAsync(new(new(Rate:"29.97 DF"),display));
    // Advance continuously within each clip. The previous %15 source jumped
    // backward every half second, repeatedly canceling cold decoder startup.
    // Intentional discontinuities are exercised by SyncRecoveryVerification.
    await w.Monitor.ConfigureAsync(new(Rate:"29.97 DF"),async(clock,token)=>{int index=0,last=-1;while(!token.IsCancellationRequested){int start=Volatile.Read(ref position);if(start!=last){index=0;last=start;}int frame=start*30+index++;if(frame/1800%10!=0&&frame/30%60==0&&frame%30<2){int skipped=2-frame%30;frame+=skipped;index+=skipped;}clock.Accept(new TcFrame{Hours=frame/108000,Minutes=frame/1800%60,Seconds=frame/30%60,Frame=frame%30,Drop=1},MasterClock.Timestamp());await Task.Delay(33,token);}});
    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Until(()=>w.IsArmed&&w.LatestObservation?.FramePts>0);
    var observed=new List<ProgramObservation>();for(int i=0;i<4;i++){Volatile.Write(ref position,Starts[i]);Guid id=w.Editor.Rows[i].CueId;await Until(()=>w.LatestObservation?.CueId==id&&w.LatestObservation?.Monitor is{Presented:>2,Error:0});observed.Add(w.LatestObservation!);}
    w.ConfirmStopForTest=_=>Task.FromResult(false);button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Until(()=>w.Delivery==DeliveryState.Streaming&&button.IsEnabled);Check(w.IsArmed,"cancel stopped");
    w.ConfirmStopForTest=_=>Task.FromResult(true);button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Until(()=>!w.IsArmed&&button.IsEnabled&&!w.Surface.ProgramMode);Check(!w.FindControl<TextBlock>("PreviewInfo")!.Text!.Contains("本番映像モニター"),"monitor label left after stop");
    return new{Observed=observed,Stopped=true};
   });
   await Test("restart in a black gap preserves the clock and later follows the next cue",async()=>{
    Volatile.Write(ref position,0);await Until(()=>w.LatestObservation is {CueId:null,FramePts:null,State:SyncState.LOCKED});
    var canvas=w.Surface.Canvas.SessionId;var sync=w.SynchronizationSession;var clock=w.Monitor.Clock;int starts=w.Monitor.Starts;
    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Until(()=>w.IsArmed&&w.Delivery==DeliveryState.Streaming);await Task.Delay(150);
    Check(w.LatestObservation is {CueId:null,FramePts:null}&&w.Surface.Canvas.SessionId==canvas&&w.SynchronizationSession==sync&&ReferenceEquals(clock,w.Monitor.Clock)&&w.Monitor.Starts==starts,"output restart rewound or recreated the source");
    Check(w.Surface.Canvas.Readback().Where((b,i)=>i%4!=3).All(b=>b==0),"black gap not black");
    Volatile.Write(ref position,Starts[0]);await Until(()=>w.LatestObservation?.CueId==w.Editor.Rows[0].CueId&&w.LatestObservation.FramePts>0);
    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Until(()=>!w.IsArmed&&button.IsEnabled);return new{Canvas=canvas,Sync=sync,BlackGap=true,NextCue=true};
   });
  }finally{w.ConfirmStopForTest=null;await w.DisarmAsync();await w.Monitor.DisposeAsync();File.WriteAllText(Path.Combine(folder,"exit-code.txt"),failed?"1":"0");MainWindow.Shutdown(failed?1:0);}
 }
}
