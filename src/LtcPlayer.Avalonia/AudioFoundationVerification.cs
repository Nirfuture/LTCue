using System.Text.Json;
using LtcPlayer.App;
namespace LtcPlayer.Avalonia;

public static class AudioFoundationVerification
{
    public static async Task RunAsync(MainWindow window,string folder)
    {
        Directory.CreateDirectory(folder);bool failed=false;
        await Task.Run(()=>
        {
            var results=new List<object>();var traces=new Dictionary<string,object>();int baseline=AudioOutputSink.ActiveStreams;
            void Check(bool condition,string message){if(!condition)throw new IOException(message);}
            void Save()=>File.WriteAllText(Path.Combine(folder,"results.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
            void Test(string name,Action body){try{body();results.Add(new{test=name,status="PASS"});}catch(Exception e){failed=true;results.Add(new{test=name,status="FAIL",detail=e.ToString()});}Save();}
            OutputAudioDevice[] devices=[];string selected="";
            var cue=Guid.NewGuid();
            AudioPcmBlock Block(Guid id,int epoch,double pts,int frames=1024,float sample=.4f)=>new(id,epoch,pts,Enumerable.Repeat(sample,frames*2).ToArray());
            void Fill(AudioOutputSink sink,Guid id,int epoch,double origin)
            {
                // One small block at a time; do not allocate an unbounded backlog.
                var state=sink.Snapshot();int free=(int)(state.Capacity-state.Padding);
                if(free<=0)return;int frames=Math.Min(free,1024);
                var block=Block(id,epoch,origin+state.Written/48000.0,frames);
                var write=sink.Write(block);Check(!write.Stale&&write.Frames>=0&&write.Frames<=frames,"invalid write result");
            }
            List<object> RunTrace(AudioOutputSink sink,string label,int epoch,double origin,double seconds,float ratio=1)
            {
                sink.BeginEpoch(cue,epoch,origin);sink.SetStreamGain(0);sink.SetRateRatio(ratio);
                while(sink.Snapshot().Padding<sink.Snapshot().Capacity)Fill(sink,cue,epoch,origin);
                sink.SetRunning(true);double start=MasterClock.Timestamp();var points=new List<object>();
                double sampleAt=0;
                while(MasterClock.Timestamp()-start<seconds)
                {
                    Fill(sink,cue,epoch,origin);double elapsed=MasterClock.Timestamp()-start;
                    if(elapsed>=sampleAt){var s=sink.Snapshot();points.Add(new{host=MasterClock.Timestamp(),elapsed,sourcePts=origin+s.ConsumedSeconds,s.Written,s.Consumed,s.Padding,s.Capacity,s.ClockPosition,s.ClockFrequency,s.ClockQpc,s.DeviceSeconds,s.QpcSeconds,s.Latency100Ns,s.RateRatio,s.ClockStatus});sampleAt=elapsed+.01;}
                    Thread.Sleep(2);
                }
                sink.SetRunning(false);traces[label]=points;return points;
            }
            Test("output enumeration resolves a real default endpoint",()=>{
                devices=AudioOutputSink.Devices();Check(devices.Length>0,"No active output devices available");
                Check(devices.Any(d=>d.IsDefault!=0),"No default endpoint available");selected=devices.First(d=>d.IsDefault!=0).Id;
                File.WriteAllText(Path.Combine(folder,"devices.json"),JsonSerializer.Serialize(devices,new JsonSerializerOptions{IncludeFields=true,WriteIndented=true}));
            });
            if(selected==""){failed=true;return;}
            Test("explicit endpoint is pinned; invalid endpoint never falls back",()=>{
                using(var sink=new AudioOutputSink(selected)){Check(sink.DeviceId==selected,"wrong endpoint");Check(AudioOutputSink.ActiveStreams==baseline+1,"unexpected stream count");}
                bool rejected=false;try{using var invalid=new AudioOutputSink("MISSING-AUDIO-OUTPUT-VERIFICATION");}catch(IOException){rejected=true;}
                Check(rejected&&AudioOutputSink.ActiveStreams==baseline,"invalid endpoint fell back or leaked");
                using var defaultSink=new AudioOutputSink("");Check(devices.Any(d=>d.Id==defaultSink.DeviceId),"default endpoint unresolved");
            });
            Test("capture endpoint is rejected as an audio output",()=>{
                var inputs=AudioInputService.Devices();Check(inputs.Length>0,"No capture endpoint for wrong-flow test");bool rejected=false;
                try{using var invalid=new AudioOutputSink(inputs[0].Id);}catch(IOException){rejected=true;}
                Check(rejected&&AudioOutputSink.ActiveStreams==baseline,"capture accepted or leaked");
            });
            Test("all stream operations stay on their owner thread",()=>{
                using var sink=new AudioOutputSink(selected);Exception? error=null;
                var thread=new Thread(()=>{try{sink.Snapshot();}catch(Exception e){error=e;}});thread.Start();thread.Join();
                Check(error is InvalidOperationException,"cross-thread access accepted");Check(sink.Snapshot().Capacity>0,"owner lost access");
            });
            Test("reset clears WASAPI queue and rejects old epoch samples",()=>{
                using var sink=new AudioOutputSink(selected);sink.SetStreamGain(0);sink.BeginEpoch(cue,1,10);
                var block=Block(cue,1,10,4096);var write=sink.Write(block,gain:.25f);var queued=sink.Snapshot();
                Check(write.Frames>0&&queued.Padding>0&&Math.Abs(queued.Peak-.1)<.001,"queue or gain missing");
                sink.BeginEpoch(cue,2,2);var cleared=sink.Snapshot();Check(cleared.Padding==0&&cleared.Written==0&&cleared.Consumed==0&&cleared.ClockPosition==0,"Reset retained pending samples or clock");
                Check(sink.Write(block).Stale,"old generation accepted");Check(sink.Write(Block(Guid.NewGuid(),2,2)).Stale,"wrong cue accepted");Check(sink.Snapshot().Written==0,"stale write reached WASAPI");
                Check(sink.Write(Block(cue,2,2)).Frames>0,"new generation rejected");sink.Suspend();Check(sink.Snapshot().Padding==0&&sink.Write(Block(cue,2,2)).Stale,"suspended output accepted PCM");
            });
            Test("device clock and QPC correlate during continuous playback",()=>{
                using var sink=new AudioOutputSink(selected);RunTrace(sink,"continuous",3,4,1.2);
                var s=sink.Snapshot();Check(s.ClockFrequency>0&&s.ClockPosition>0&&s.Consumed>20000,"clock did not advance");
                Check(Math.Abs(MasterClock.Timestamp()-s.QpcSeconds)<.2,"QPC units disagree");
                Check(Math.Abs(s.DeviceSeconds-1.2)<.25,"device clock scale is wrong");
                Check(s.Padding<=s.Capacity&&s.Consumed<=s.Written,"queue exceeded bounds");
                var position=s.ClockPosition;Thread.Sleep(60);Check(sink.Snapshot().ClockPosition==position,"stopped clock advanced");
            });
            Test("real device rate adjustment changes consumed source time",()=>{
                using var sink=new AudioOutputSink(selected);
                double Measure(float ratio,int epoch,string label)
                {
                    sink.BeginEpoch(cue,epoch,0);sink.SetStreamGain(0);sink.SetRateRatio(ratio);
                    while(sink.Snapshot().Padding<sink.Snapshot().Capacity)Fill(sink,cue,epoch,0);
                    sink.SetRunning(true);double warmUntil=MasterClock.Timestamp()+.2;
                    while(MasterClock.Timestamp()<warmUntil){Fill(sink,cue,epoch,0);Thread.Sleep(2);}
                    double begin=MasterClock.Timestamp(),source=sink.Snapshot().ConsumedSeconds;
                    while(MasterClock.Timestamp()-begin<2){Fill(sink,cue,epoch,0);Thread.Sleep(2);}
                    var last=sink.Snapshot();double elapsed=MasterClock.Timestamp()-begin;sink.SetRunning(false);
                    double slope=(last.ConsumedSeconds-source)/elapsed;traces[label]=new{ratio,elapsed,slope,last};return slope;
                }
                double slow=Measure(.99f,4,"rate-099"),fast=Measure(1.01f,5,"rate-101");
                Check(fast-slow>.006,"sample rate adjustment had no measurable direction: "+slow+" / "+fast);
            });
            Test("small-error controller converges without decode restarts (simulation)",()=>{
                var policy=new AudioSynchronizationPolicy();double actual=-.025,uncontrolled=-.025;int reseeks=0;
                for(int i=0;i<3000;i++){double target=i*.01;var decision=policy.Evaluate(target,actual,target,true);if(decision.Action==AudioSyncAction.Reseek)reseeks++;actual+=.01*.999*decision.RateRatio;uncontrolled+=.01*.999;}
                double controlledError=Math.Abs(30-actual),uncontrolledError=Math.Abs(30-uncontrolled);
                Check(reseeks==0&&controlledError<.005&&controlledError<uncontrolledError/4,"controller did not converge");
                traces["control-simulation"]=new{controlledError,uncontrolledError,reseeks,Seconds=30};
            });
            Test("real-device small-error control remains bounded",()=>{
                using var sink=new AudioOutputSink(selected);sink.SetStreamGain(0);sink.BeginEpoch(cue,6,0);
                while(sink.Snapshot().Padding<sink.Snapshot().Capacity)Fill(sink,cue,6,0);
                sink.SetRunning(true);double started=MasterClock.Timestamp();var policy=new AudioSynchronizationPolicy();var points=new List<object>();int rateChanges=0,reseeks=0;
                double next=0;float previous=1;var errors=new List<double>();
                while(MasterClock.Timestamp()-started<2.5)
                {
                    Fill(sink,cue,6,0);double elapsed=MasterClock.Timestamp()-started;
                    if(elapsed>=next){var s=sink.Snapshot();double target=elapsed+.025;var decision=policy.Evaluate(target,s.ConsumedSeconds,MasterClock.Timestamp(),true);
                        if(decision.Action==AudioSyncAction.Reseek)reseeks++;
                        if(decision.RateRatio!=previous){sink.SetRateRatio(decision.RateRatio);rateChanges++;previous=decision.RateRatio;}
                        errors.Add(decision.ErrorSeconds);points.Add(new{elapsed,target,played=s.ConsumedSeconds,decision.ErrorSeconds,decision.RateRatio,s.Padding});next=elapsed+.02;}
                    Thread.Sleep(2);
                }
                sink.SetRunning(false);Check(rateChanges>0&&reseeks==0,"unexpected reset or no rate control");
                Check(errors.All(e=>Math.Abs(e)<.1),"small error escaped reseek threshold");
                double first=errors.Take(20).Average(),last=errors.TakeLast(20).Average();
                traces["controlled-device"]=new{firstMeanError=first,lastMeanError=last,rateChanges,reseeks,points};
            });
            Test("LTC forward/back jumps flush previous generation; LOST is silent",()=>{
                using var sink=new AudioOutputSink(selected);sink.SetStreamGain(0);var clock=new MasterClock(TimecodeRate.Modes[3]);
                void Accept(int seconds,int frame)=>clock.Accept(new TcFrame{Seconds=seconds,Frame=frame,Drop=1},MasterClock.Timestamp());
                Accept(1,0);var initial=clock.Read();sink.BeginEpoch(cue,initial.Generation,initial.Seconds);var old=Block(cue,initial.Generation,initial.Seconds);sink.Write(old);sink.SetRunning(true);Thread.Sleep(15);
                Accept(5,0);var forward=clock.Read();Check(forward.Generation>initial.Generation,"forward jump not detected");sink.BeginEpoch(cue,forward.Generation,forward.Seconds);
                Check(sink.Snapshot().Padding==0&&sink.Write(old).Stale,"forward jump retained old samples");
                sink.Write(Block(cue,forward.Generation,forward.Seconds));sink.SetRunning(true);Accept(2,0);var back=clock.Read();Check(back.Generation>forward.Generation,"back jump not detected");sink.BeginEpoch(cue,back.Generation,back.Seconds);sink.Write(Block(cue,back.Generation,back.Seconds));sink.SetRunning(true);
                Thread.Sleep(75);Check(clock.Read().State==SyncState.FREEWHEEL,"short loss did not freewheel");Thread.Sleep(60);Check(clock.Read().State==SyncState.LOST,"long loss did not become LOST");sink.Suspend();
                Check(sink.Snapshot().Running==0&&sink.Snapshot().Padding==0,"LOST retained queued audio");
                Accept(3,0);var resumed=clock.Read();Check(resumed.Generation>back.Generation,"recovery generation missing");sink.BeginEpoch(cue,resumed.Generation,resumed.Seconds);sink.Write(Block(cue,resumed.Generation,resumed.Seconds));
                clock.Fault();sink.Suspend();Check(clock.Read().State==SyncState.ERROR&&sink.Snapshot().Padding==0,"ERROR retained samples");
            });
            Test("slow recovery preparation is not restarted as playhead advances",()=>{
                var policy=new AudioSynchronizationPolicy();Check(policy.Evaluate(4,1,100,true).Action==AudioSyncAction.Reseek,"large lag did not request recovery");
                for(int i=0;i<100;i++)Check(policy.Evaluate(4+i*.01,1,100+i*.01,true,true).Action==AudioSyncAction.KeepPreparing,"recovery preparation was restarted");
                Check(policy.Evaluate(1,1,101,false).Action==AudioSyncAction.Silence,"held playback should be silent");
            });
            Test("repeated open/reset/close releases all streams",()=>{
                for(int i=0;i<10;i++){using var sink=new AudioOutputSink(selected);sink.SetStreamGain(0);sink.BeginEpoch(cue,i,0);sink.Write(Block(cue,i,0));sink.Suspend();}
                Check(AudioOutputSink.ActiveStreams==baseline,"WASAPI stream leaked");
            });
            File.WriteAllText(Path.Combine(folder,"measurements.json"),JsonSerializer.Serialize(traces,new JsonSerializerOptions{IncludeFields=true,WriteIndented=true}));
            Check(AudioOutputSink.ActiveStreams==baseline,"Final audio stream leak");
        });
        await window.Monitor.DisposeAsync();MainWindow.Shutdown(failed?1:0);
    }
}
