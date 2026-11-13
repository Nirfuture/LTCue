using LtcPlayer.App;
using System.Text.Json;

// The clock, scheduler and bounded buffers are production source. Only decoding,
// rendering and UI dispatch are substituted; no physical sync claim is made.
var results=new List<object>();bool failed=false;
void Check(bool value,string reason){if(!value)throw new IOException(reason);}
foreach(var scenario in new[]{"continuous","slow","slow-preparation","load-recovers","jump","loss-resume","recovery-error"}) {
 var observations=new List<ProgramObservation>();
 MediaDecoder.DelayMs=scenario is "continuous" or "jump" or "loss-resume"?10:80;
 MediaDecoder.PreparationMs=scenario=="slow-preparation"?500:0;
 MediaDecoder.Active=MediaDecoder.Peak=0;MediaDecoder.FailRecovery=scenario=="recovery-error";
 double start=MasterClock.Timestamp();var clock=new MasterClock(TimecodeRate.Modes[1],.2);
 using var stop=new CancellationTokenSource(TimeSpan.FromSeconds(4));
 var source=Task.Run(async()=>{
  int previous=-1;
  try {while(!stop.IsCancellationRequested){
   double elapsed=MasterClock.Timestamp()-start;
   if(scenario=="load-recovers"&&elapsed>2)MediaDecoder.DelayMs=10;
   if(scenario=="loss-resume"&&elapsed>=1&&elapsed<2){await Task.Delay(8,stop.Token);continue;}
   int frame=(int)(elapsed*25);if(frame==previous){await Task.Delay(4,stop.Token);continue;}previous=frame;
   int delta=scenario=="jump"?(elapsed>=2?25:elapsed>=1?50:0):0;
   int tc=frame+delta;
   clock.Accept(new TcFrame{Seconds=tc/25,Frame=tc%25},start+(frame+1)/25.0);
   await Task.Delay(4,stop.Token);
  }}catch(OperationCanceledException)when(stop.IsCancellationRequested){}
 });
 Exception? error=null;double finish=0;
 try {
  try{await new ProgramPlayback(new LtcPlayer.Avalonia.UiDispatcher()).RunAsync([new(Guid.NewGuid(),"fixture.mp4",0,20,25,"h264"),new(Guid.NewGuid(),"next.mp4",20,10,25,"h264")],new(),clock,0,observations.Add,stop.Token);}
  catch(OperationCanceledException)when(stop.IsCancellationRequested){}
  catch(IOException e)when(scenario=="recovery-error"){Check(e.Message.Contains("Synthetic recovery failure"),"Wrong decoder error");error=e;}
  finish=MasterClock.Timestamp();stop.Cancel();await source;
  Check(MediaDecoder.Active==0,"Decoder leaked after completion");
  Check(observations.Count>0,"No telemetry");
  var last=observations.Last();
  if(scenario=="recovery-error")Check(error!=null,"Recovery failure was silently ignored");
  else {
   Check(observations.Any(o=>o.FramePts!=null),"No frame presented");
   if(scenario=="continuous")Check(last.RecoveryAttempts==0&&last.LagSeconds<.15,"Normal input triggered recovery or lagged");
   if(scenario is "slow" or "slow-preparation" or "load-recovers") {
    Check(observations.Any(o=>o.LagSeconds>.2&&o.Warning.Contains("遅れ")),"Lag was not reported");
    Check(last.RecoveryAttempts>0&&last.RecoveryAttempts<=5,"Recovery missing or restarted too often");
    Check(last.LagSeconds<1,"Lag continued accumulating");
    Check(observations.Where(o=>o.LagSeconds!=null).Max(o=>o.LagSeconds)<1.25,"Recovery allowed unbounded lag");
    Check(observations.Where(o=>o.Recovering).All(o=>o.FramePts!=null),"Recovery blanked an existing output");
   }
   if(scenario=="load-recovers")Check(last.LagSeconds<.15&&!last.Recovering&&last.Warning=="","Load recovery did not return to normal");
   if(scenario=="jump")Check(last.Seeks>=3&&last.LagSeconds<.15,"Forward/backward TC jump did not recover");
   if(scenario=="loss-resume"){
    var lost=observations.Where(o=>o.State==SyncState.LOST).ToArray();
    Check(lost.Length>0,"Input loss not detected");
    Check(lost.Select(o=>o.Presented).Distinct().Count()==1&&lost.Select(o=>o.FramePts).Distinct().Count()==1,"Output changed during loss");
    Check(last.State==SyncState.LOCKED&&last.LagSeconds<.15,"Input resume failed");
   }
  }
  Check(MediaDecoder.Peak<=2,"Too many simultaneous decoders");
  results.Add(new{Scenario=scenario,Passed=true,LastLagSeconds=last.LagSeconds,MaxLagSeconds=observations.Max(o=>o.LagSeconds),last.RecoveryAttempts,PeakDecoders=MediaDecoder.Peak,Elapsed=finish-start});
 }catch(Exception e){failed=true;results.Add(new{Scenario=scenario,Passed=false,Error=e.ToString()});}
 finally{stop.Cancel();await source;}
}
string json=JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true});Console.WriteLine(json);
if(args.Length>0)File.WriteAllText(args[0],json);
return failed?1:0;
