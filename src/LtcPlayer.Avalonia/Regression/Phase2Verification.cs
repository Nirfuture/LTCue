using System.Text.Json;
using Window = LtcPlayer.Avalonia.RegressionWindow;
using Application = LtcPlayer.Avalonia.RegressionApplication;
namespace LtcPlayer.App;
public static class Phase2Verification
{
    public static async Task RunAsync(Window window,string folder)
    {
        Directory.CreateDirectory(folder); var results=new List<object>();bool failed=false;
        async Task Test(string name,Func<Task<object>> action)
        {
            try {results.Add(new{test=name,status="PASS",detail=await action()});}
            catch(Exception e){failed=true;results.Add(new{test=name,status="FAIL",detail=e.ToString()});}
            await File.WriteAllTextAsync(Path.Combine(folder,"results.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true,IncludeFields=true}));
        }
        try
        {
            await Test("WASAPI endpoint enumeration",()=>Task.FromResult<object>(AudioInputService.Devices()));
            await Test("input settings persistence",()=>
            {
                var setting=new InputSettings("synthetic-endpoint","Fixture",1,"29.97 DF","01:00:00:00",-2,0.3,3);
                var path=Path.Combine(folder,"settings-fixture.json");setting.Save(path);Assert(InputSettings.Load(path)==setting,"roundtrip");
                return Task.FromResult<object>("device ID, channel, mode, start, offset, freewheel and threshold persist");
            });
            await Test("stereo L/R isolation through WASAPI conversion routine",()=>
            {
                var signal=new float[100000];int n=AudioNative.tc_generate(25,0,0,0,0,0,50,signal,signal.Length);Assert(n>0,"fixture");
                var bytes=new byte[n*8];for(int i=0;i<n;++i)BitConverter.GetBytes(signal[i]).CopyTo(bytes,i*8+4);
                var mono=new float[n];AudioNative.Check(AudioNative.audio_convert_test(bytes,bytes.Length,32,1,2,0,n,mono));
                using var left=new LtcDecoder(48000,25);Assert(!left.Feed(mono,n,0).Any(),"L silence");
                AudioNative.Check(AudioNative.audio_convert_test(bytes,bytes.Length,32,1,2,1,n,mono));
                using var right=new LtcDecoder(48000,25);int decoded=right.Feed(mono,n,0).Count();Assert(decoded>=45,"R LTC");
                var pcm=new byte[n*4];for(int i=0;i<n;++i)BitConverter.GetBytes((short)(signal[i]*30000)).CopyTo(pcm,i*4);
                AudioNative.Check(AudioNative.audio_convert_test(pcm,pcm.Length,16,0,2,0,n,mono));
                using var pcmDecoder=new LtcDecoder(48000,25);int pcmCount=pcmDecoder.Feed(mono,n,0).Count();Assert(pcmCount>=45,"PCM16 L LTC");
                return Task.FromResult<object>(new{leftSilence=true,rightDecoded=decoded,pcm16LeftDecoded=pcmCount,physicalCapture=false});
            });
            await Test("DF independent minute/ten-minute known values",()=>
            {
                var df=TimecodeRate.Modes[3]; Assert(df.Frames(0,1,0,2)==1800,"minute");Assert(df.Frames(0,10,0,0)==17982,"ten minutes");Assert(df.Frames(1,0,0,0)==107892,"hour");
                bool rejected=false;try{df.Frames(0,1,0,0);}catch(InvalidDataException){rejected=true;} Assert(rejected,"illegal DF label");
                Assert(Math.Abs(TimecodeRate.Modes[2].Parse("01:00:00:00")-3603.6)<0.00001,"NDF wallclock distinction");
                return Task.FromResult<object>("known labels and invalid DF label verified");
            });
            foreach(var mode in TimecodeRate.Modes)
                await Test("native audio feed / "+mode.Name,()=>
                {
                    var samples=new float[240000];int f=mode.Nominal-3;
                    int count=AudioNative.tc_generate(mode.Fps,mode.Drop?1:0,0,0,59,f,100,samples,samples.Length);Assert(count>0,"generate");
                    using var decoder=new LtcDecoder(48000,mode.Fps);var block=new float[257];var decoded=new List<long>();
                    for(int at=0;at<count;at+=block.Length)
                    {
                        int n=Math.Min(block.Length,count-at);Array.Copy(samples,at,block,0,n);
                        foreach(var tc in decoder.Feed(block,n,at)){Assert(tc.Drop==(mode.Drop?1:0),"DF");Assert(tc.Reverse==0,"direction");decoded.Add(mode.Frames(tc.Hours,tc.Minutes,tc.Seconds,tc.Frame));}
                    }
                    Assert(decoded.Count>=95,"decoded count");long first=mode.Frames(0,0,59,f);
                    Assert(decoded[0]>=first && decoded[0]<=first+1,"first label");for(int i=1;i<decoded.Count;++i)Assert(decoded[i]==decoded[i-1]+1,"independent frame-number continuity");
                    return Task.FromResult<object>(new{decoded=decoded.Count,first=decoded[0],last=decoded[^1]});
                });
            await Test("clock loss/recovery/jump/offset/drift",()=>
            {
                var mode=TimecodeRate.Modes[1];var clock=new MasterClock(mode,0.5,2);Assert(clock.Read(0).State==SyncState.WAITING,"waiting");
                clock.Accept(new TcFrame{Frame=0},0);Assert(clock.Read(0).State==SyncState.LOCKED,"lock");Assert(clock.Read(0.12).State==SyncState.FREEWHEEL,"freewheel");Assert(clock.Read(1).State==SyncState.LOST,"lost");
                clock.Accept(new TcFrame{Seconds=5},1);Assert(clock.Read(1).State==SyncState.RESYNC,"resync");Assert(clock.Read(1).Generation==1,"jump epoch");
                for(int i=0;i<10000;++i){int frame=i%25,sec=(i/25)%60,min=i/1500;clock.Accept(new TcFrame{Minutes=min,Seconds=sec,Frame=frame},2+i/25.0);}
                double expected=10000/25.0;Assert(Math.Abs(clock.Read(2+9999/25.0).Seconds-expected)<1e-8,"no accumulated position error");
                Assert(Math.Abs((mode.Parse("00:00:03:00")-mode.Parse("00:00:01:00")+2/mode.Fps)-2.08)<1e-8,"offset");
                return Task.FromResult<object>("simulated timestamps only, 10000 updates; no physical drift measurement");
            });
            await Test("seek PTS",async()=>
            {
                using var cancel=new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await foreach(var frame in new MediaDecoder().DecodeAsync(Path.Combine(folder,"media.mp4"),320,180,cancel.Token,1.21))
                {Assert(Math.Abs(frame.Seconds-1.24)<0.002,"seek preserves original frame position");return new{firstPts=frame.Seconds};}
                throw new Exception("No seek frame");
            });
            await Test("decoded LTC drives D3D11 / loss / backward jump",async()=>
            {
                using var cancel=new CancellationTokenSource(TimeSpan.FromSeconds(15));var mode=TimecodeRate.Modes[1];var clock=new MasterClock(mode,0.2,2);
                var entries=new List<SyncTelemetry>();var received=new List<object>();var display=VideoRenderer.Displays()[0];
                var play=new SynchronizedPlayback(window.Dispatcher).RunAsync(Path.Combine(folder,"media.mp4"),display,clock,0,0,t=>{lock(entries)entries.Add(t);},cancel.Token);
                try
                {
                    await Task.Delay(150);
                    await Supply(clock,received,0,cancel.Token); await Task.Delay(600,cancel.Token);
                    await Supply(clock,received,10,cancel.Token);
                }
                finally{cancel.Cancel();try{await play;}catch(OperationCanceledException) when(cancel.IsCancellationRequested){}}
                SyncTelemetry[] log;lock(entries)log=entries.ToArray();
                await File.WriteAllTextAsync(Path.Combine(folder,"synthetic-render-log.json"),JsonSerializer.Serialize(log,new JsonSerializerOptions{WriteIndented=true}));
                await File.WriteAllTextAsync(Path.Combine(folder,"synthetic-receive-log.json"),JsonSerializer.Serialize(received,new JsonSerializerOptions{WriteIndented=true}));
                Assert(log.Any(t=>t.State==SyncState.WAITING),"waiting observed");Assert(log.Any(t=>t.State==SyncState.LOCKED && t.FramePts!=null),"locked frames");
                Assert(log.Any(t=>t.State==SyncState.FREEWHEEL),"freewheel observed");Assert(log.Any(t=>t.State==SyncState.LOST),"lost observed");Assert(log.Any(t=>t.State==SyncState.RESYNC),"resync observed");
                Assert(log.Max(t=>t.Seeks)>=2,"frame reacquisition");
                var aligned=log.Where(t=>t.State==SyncState.LOCKED && t.FramePts!=null && t.Target-t.FramePts>=-0.00001 && t.Target-t.FramePts<0.05).Count();Assert(aligned>20,"PTS frame selection");
                return new{samples=log.Length,aligned,received=received.Count,presented=log[^1].Presented,repeated=log[^1].Repeats,skipped=log[^1].Skipped,seeks=log[^1].Seeks,display};
            });
        }
        catch(Exception e){failed=true;await File.WriteAllTextAsync(Path.Combine(folder,"fatal.txt"),e.ToString());}
        finally{await File.WriteAllTextAsync(Path.Combine(folder,"exit-code.txt"),failed?"1":"0");Application.Current.Shutdown(failed?1:0);}
    }
    private static async Task Supply(MasterClock clock,List<object> log,int startFrame,CancellationToken token)
    {
        var data=new float[100000];int count=AudioNative.tc_generate(25,0,0,0,0,startFrame,50,data,data.Length);Assert(count>0,"generate segment");
        using var decoder=new LtcDecoder(48000,25);var block=new float[257];double baseline=MasterClock.Timestamp();
        for(int at=0;at<count;at+=block.Length)
        {
            double wait=baseline+(at+Math.Min(block.Length,count-at))/48000.0-MasterClock.Timestamp();if(wait>0)await Task.Delay(TimeSpan.FromSeconds(wait),token);
            int n=Math.Min(block.Length,count-at);Array.Copy(data,at,block,0,n);
            foreach(var tc in decoder.Feed(block,n,at)) {double stamp=baseline+(tc.End+1)/48000.0;clock.Accept(tc,stamp);log.Add(new{tc.Hours,tc.Minutes,tc.Seconds,tc.Frame,captureSeconds=stamp,deliverySeconds=MasterClock.Timestamp()});}
        }
    }
    private static void Assert(bool ok,string message){if(!ok)throw new Exception(message);}
}

