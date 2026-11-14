using System.Text.Json;
using LtcPlayer.App;
using LtcPlayer.Avalonia;
var results=new List<object>();bool failed=false;
void Check(bool ok,string why){if(!ok)throw new IOException(why);}
async Task Test(string name,Func<Task> body){try{MediaDecoder.Active=MediaDecoder.Peak=0;MediaDecoder.DelayMs=2;MediaDecoder.PreparationMs=0;MediaDecoder.FailRecovery=false;MediaDecoder.Opened=null;MediaDecoder.Disposing=null;await body();Check(MediaDecoder.Active==0,"decoder leaked");results.Add(new{test=name,status="PASS"});}catch(Exception e){failed=true;results.Add(new{test=name,status="FAIL",detail=e.ToString()});}finally{MediaDecoder.Disposing=null;MediaDecoder.Opened=null;}}
void Accept(MasterClock clock,int seconds)=>clock.Accept(new TcFrame{Seconds=seconds},MasterClock.Timestamp());
await Test("authority comparison distinguishes acquisition errors and jumps while allowing freewheel",()=>{
 var live=new ClockSnapshot(SyncState.LOCKED,0,1,"");Check(live.MatchesPlaybackAuthority(new(SyncState.FREEWHEEL,0,1,"")),"freewheel rejected");Check(!live.MatchesPlaybackAuthority(new(SyncState.LOCKED,0,2,""))&&!live.MatchesPlaybackAuthority(new(SyncState.LOST,0,1,""))&&!live.MatchesPlaybackAuthority(new(SyncState.ERROR,0,1,"")),"authority change accepted");Check(!new ClockSnapshot(SyncState.WAITING,0,0,"").MatchesPlaybackAuthority(new(SyncState.LOCKED,0,0,"")),"first acquisition accepted as manual");return Task.CompletedTask;
});
async Task QueuedJump(int from,int to,bool jumpAgainOnDispose=false){
 var clock=new MasterClock(TimecodeRate.Modes[1],10);Accept(clock,from);using var stop=new CancellationTokenSource(TimeSpan.FromSeconds(2));bool armed=false,fired=false,repeated=false,arrived=false;var dispatcher=new UiDispatcher();int firstGeneration=clock.Read().Generation;
 dispatcher.BeforeInvoke=()=>{if(armed&&!fired){fired=true;Accept(clock,to);}};
 if(jumpAgainOnDispose)MediaDecoder.Disposing=()=>{if(fired&&!repeated){repeated=true;Accept(clock,10);}return Task.CompletedTask;};
 var presentation=new RecordingPresentation(frame=>{var current=clock.Read();Check(Math.Abs(frame.Seconds-current.Seconds)<.18,"stale frame crossed presentation boundary");});
 try{await new ProgramPlayback(dispatcher).RunAsync([new(Guid.NewGuid(),"fixture.mp4",0,20,25,"h264")],new(),clock,0,p=>{Check(p.InputGeneration==clock.Read().Generation,"stale observation accepted");if(p.FramePts!=null)armed=true;int desired=firstGeneration+(jumpAgainOnDispose?2:1);if(p.InputGeneration==desired&&p.FramePts!=null&&Math.Abs(p.FramePts.Value-(jumpAgainOnDispose?10:to))<.3){arrived=true;stop.Cancel();}},stop.Token,presentation:presentation);}catch(OperationCanceledException)when(stop.IsCancellationRequested){}
 Check(fired&&arrived&&(!jumpAgainOnDispose||repeated),"jump never reached final target");Check(MediaDecoder.Peak<=2,"decoder resource budget exceeded");
}
await Test("queued forward jump rejects stale presentation and observations",()=>QueuedJump(0,5));
await Test("queued backward jump rejects stale presentation and observations",()=>QueuedJump(5,1));
await Test("jump during old decoder disposal skips superseded target",()=>QueuedJump(0,5,true));
await Test("slow old-source cleanup uses latest position rather than pre-wait target",async()=>{
 var clock=new MasterClock(TimecodeRate.Modes[1],10);Accept(clock,0);using var stop=new CancellationTokenSource(TimeSpan.FromSeconds(2));bool armed=false,fired=false,waited=false,opened=false,arrived=false;
 var dispatcher=new UiDispatcher{BeforeInvoke=()=>{if(armed&&!fired){fired=true;Accept(clock,5);}}};
 MediaDecoder.Disposing=async()=>{if(fired&&!waited){waited=true;await Task.Delay(250);}};
 MediaDecoder.Opened=(_,seek)=>{if(waited&&!opened){opened=true;Check(clock.Read().Seconds-seek<.12,"new decoder used target from before cleanup wait");}};
 try{await new ProgramPlayback(dispatcher).RunAsync([new(Guid.NewGuid(),"fixture.mp4",0,20,25,"h264")],new(),clock,0,p=>{if(p.FramePts!=null)armed=true;if(p.InputGeneration>0&&p.FramePts>5.15){arrived=true;stop.Cancel();}},stop.Token,presentation:new RecordingPresentation(_=>{}));}catch(OperationCanceledException)when(stop.IsCancellationRequested){}
 Check(waited&&opened&&arrived,"latest-position cleanup path not exercised");
});
await Test("first LTC acquisition rejects queued WAITING black without requiring a generation increment",async()=>{
 var clock=new MasterClock(TimecodeRate.Modes[1],10);using var stop=new CancellationTokenSource(TimeSpan.FromSeconds(2));int calls=0;bool arrived=false;var dispatcher=new UiDispatcher{BeforeInvoke=()=>{if(++calls==2)Accept(clock,5);}};
 var presentation=new RecordingPresentation(frame=>Check(frame.Seconds>4,"queued WAITING black overwrote acquired source"));
 try{await new ProgramPlayback(dispatcher).RunAsync([new(Guid.NewGuid(),"fixture.mp4",0,20,25,"h264")],new(),clock,0,p=>{if(p.FramePts>4){arrived=true;stop.Cancel();}},stop.Token,presentation:presentation);}catch(OperationCanceledException)when(stop.IsCancellationRequested){}
 Check(arrived&&clock.Read().Generation==0,"first acquisition failed");
});
await Test("input fault during dispatch preserves previously adopted frame and counters",async()=>{
 var clock=new MasterClock(TimecodeRate.Modes[1],10);Accept(clock,0);using var stop=new CancellationTokenSource(TimeSpan.FromSeconds(2));bool armed=false,fired=false,held=false;int count=0,heldCount=0;double? heldPts=null;
 var dispatcher=new UiDispatcher{BeforeInvoke=()=>{if(armed&&!fired){fired=true;clock.Fault();}}};var presentation=new RecordingPresentation(frame=>{Check(clock.Read().State!=SyncState.ERROR,"frame adopted after input fault");count++;heldPts=frame.Seconds;heldCount=count;});
 try{await new ProgramPlayback(dispatcher).RunAsync([new(Guid.NewGuid(),"fixture.mp4",0,20,25,"h264")],new(),clock,0,p=>{if(p.FramePts!=null)armed=true;if(p.State==SyncState.ERROR){Check(p.FramePts==heldPts&&p.Presented==heldCount&&count==heldCount,"unadopted frame changed hold telemetry");held=true;stop.Cancel();}},stop.Token,presentation:presentation);}catch(OperationCanceledException)when(stop.IsCancellationRequested){}
 Check(fired&&held,"fault hold missing");
});
await Test("brief startup preparation catches up without extra recovery decoder",async()=>{
 MediaDecoder.PreparationMs=100;var clock=new MasterClock(TimecodeRate.Modes[1],10);Accept(clock,5);using var stop=new CancellationTokenSource(TimeSpan.FromSeconds(1));bool ready=false;int seeks=0,recoveries=0;
 try{await new ProgramPlayback(new()).RunAsync([new(Guid.NewGuid(),"fixture.mp4",0,20,25,"h264")],new(),clock,0,p=>{recoveries=Math.Max(recoveries,p.RecoveryAttempts);seeks=Math.Max(seeks,p.Seeks);if(p.FramePts!=null&&p.LagSeconds<.1)ready=true;},stop.Token,presentation:new RecordingPresentation(_=>{}));}catch(OperationCanceledException)when(stop.IsCancellationRequested){}
 Check(ready&&recoveries==0&&seeks==1,"brief preparation triggered redundant recovery");
});
await Test("slow startup still invokes bounded recovery and preserves source",async()=>{
 MediaDecoder.PreparationMs=500;var clock=new MasterClock(TimecodeRate.Modes[1],10);Accept(clock,0);using var stop=new CancellationTokenSource(TimeSpan.FromSeconds(3));int recoveries=0;bool ready=false;
 try{await new ProgramPlayback(new()).RunAsync([new(Guid.NewGuid(),"fixture.mp4",0,20,25,"h264")],new(),clock,0,p=>{recoveries=Math.Max(recoveries,p.RecoveryAttempts);if(p.FramePts!=null&&p.LagSeconds<.1&&recoveries>0)ready=true;},stop.Token,presentation:new RecordingPresentation(_=>{}));}catch(OperationCanceledException)when(stop.IsCancellationRequested){}
 Check(ready&&recoveries>0&&recoveries<=3&&MediaDecoder.Peak<=2,"genuine slow preparation lost bounded recovery");
});
await Test("preloaded old source revision is not promoted by matching cue ID",async()=>{
 var clock=new MasterClock(TimecodeRate.Modes[1],10);using var stop=new CancellationTokenSource(TimeSpan.FromSeconds(2));var id=Guid.NewGuid();var old=new ScheduledCue(id,"old.mp4",0,20,25,"h264");var newer=old with{FilePath="new.mp4",SourceRevision=1};var source=new PlaybackTimeline();source.Update([old]);bool switched=false,arrived=false;
 var presentation=new RecordingPresentation(frame=>{if(clock.Read().State==SyncState.WAITING)return;Check(frame.Pixels[0]==2,"old preloaded source revision was adopted");arrived=true;stop.Cancel();});
 try{await new ProgramPlayback(new()).RunAsync([old],new(),clock,0,p=>{if(!switched){switched=true;source.Update([newer]);Accept(clock,0);}},stop.Token,presentation:presentation,sourceTimeline:source);}catch(OperationCanceledException)when(stop.IsCancellationRequested){}
 Check(arrived,"new source not presented");
});
string path=args.FirstOrDefault()??"results.json";File.WriteAllText(path,JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine($"{results.Count} checks; failures={failed}");return failed?1:0;
sealed class RecordingPresentation(Action<VideoFrame> present):IProgramPresentation{public void Present(VideoFrame frame)=>present(frame);public void Refresh(){}public void Dispose(){}}
