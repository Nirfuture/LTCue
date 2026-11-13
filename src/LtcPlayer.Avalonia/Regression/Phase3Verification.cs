using System.Text.Json;
using Window = LtcPlayer.Avalonia.RegressionWindow;
using Application = LtcPlayer.Avalonia.RegressionApplication;
namespace LtcPlayer.App;
public static class Phase3Verification
{
    public static async Task RunAsync(Window window,string folder)
    {
        Directory.CreateDirectory(folder);var results=new List<object>();bool failed=false;
        var mode=TimecodeRate.Modes[1];
        CueRow Row(string name,string start,double duration)=>new(){FilePath=Path.Combine(folder,name+".mp4"),Hours="00",Minutes="00",Seconds=start,Frames="00",Duration=duration,FrameRate=25,Codec="h264"};
        async Task Test(string name,Func<Task<object>> action)
        {
            try{results.Add(new{test=name,status="PASS",detail=await action()});}
            catch(Exception e){failed=true;results.Add(new{test=name,status="FAIL",detail=e.ToString()});}
            await File.WriteAllTextAsync(Path.Combine(folder,"results.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true,IncludeFields=true}));
        }
        try
        {
            await Test("later cue wins; no return after short later cue",()=>
            {
                var a=Row("a","00",5);var b=Row("b","01",1);var c=Row("c","03",1);var scheduler=new CueScheduler(ShowProgramService.Validate(new[]{a,b,c},mode));
                Assert(scheduler.Select(-1)==null,"before start");Assert(scheduler.Select(.5)!.CueId==a.CueId,"a");Assert(scheduler.Select(1)!.CueId==b.CueId,"b preempts a");Assert(scheduler.Select(2)==null,"no fallback to a");Assert(scheduler.Select(3)!.CueId==c.CueId,"c");Assert(scheduler.Select(4)==null,"after end");return Task.FromResult<object>("boundaries verified");
            });
            await Test("display order / preload order / TC jumps",()=>
            {
                var a=Row("a","00",5);var b=Row("b","01",1);var c=Row("c","03",1);var rows=new[]{c,a,b};var s=new CueScheduler(ShowProgramService.Validate(rows,mode));
                Assert(s.Next(-1)!.CueId==a.CueId,"first preload");Assert(s.Next(.5)!.CueId==b.CueId,"next preload");Assert(s.Next(2.5)!.CueId==c.CueId,"preload gap");Assert(s.Select(3.5)!.CueId==c.CueId,"forward jump");Assert(s.Select(.5)!.CueId==a.CueId,"backward jump");
                Array.Reverse(rows);var other=new CueScheduler(ShowProgramService.Validate(rows,mode));foreach(var t in new[]{-.5,.5,1.5,2.5,3.5,4.5})Assert(s.Select(t)?.CueId==other.Select(t)?.CueId,"order independent");return Task.FromResult<object>("same decisions after reorder and jumps");
            });
            await Test("clone disabled / duplicate TC conflict / overlap warning",()=>
            {
                var a=Row("a","00",5);var clone=a.CloneDisabled();Assert(clone.CueId!=a.CueId && !clone.Enabled,"clone identity/state");ShowProgramService.Validate(new[]{a,clone},mode);
                clone.Enabled=true;bool rejected=false;try{ShowProgramService.Validate(new[]{a,clone},mode);}catch(InvalidDataException){rejected=true;}Assert(rejected,"duplicate arm block");
                clone.Seconds="01";ShowProgramService.Validate(new[]{a,clone},mode);Assert(a.WarningMark!="" && clone.WarningMark!="","overlap warning");return Task.FromResult<object>("overlap allowed, duplicate rejected");
            });
            await Test("JSON roundtrip / metadata revalidation",async()=>
            {
                var a=Row("a","00",999);var b=Row("b","01",1);b.Enabled=false;var rows=new[]{b,a};string file=Path.Combine(folder,"show.json");
                await ShowProgramService.SaveAsync(file,rows,mode);var loaded=await ShowProgramService.LoadAsync(file,CancellationToken.None);
                Assert(loaded.Rows[0].CueId==b.CueId && loaded.Rows[1].CueId==a.CueId,"identity/order");Assert(!loaded.Rows[0].Enabled,"disabled state");Assert(Math.Abs(loaded.Rows[1].Duration-5)<.001,"real metadata");Assert(loaded.Notices.Count>0,"metadata change reported");
                var missing=loaded.Rows[1];missing.FilePath=Path.Combine(folder,"missing.mp4");await ShowProgramService.SaveAsync(Path.Combine(folder,"missing-show.json"),loaded.Rows,mode);
                var invalid=await ShowProgramService.LoadAsync(Path.Combine(folder,"missing-show.json"),CancellationToken.None);bool rejected=false;try{ShowProgramService.Validate(invalid.Rows,mode);}catch(InvalidDataException){rejected=true;}Assert(rejected,"missing enabled media blocks arm");return new{metadataNotices=loaded.Notices,missingNotices=invalid.Notices};
            });
            await Test("D3D11 cue switching / black gap / backward and forward jumps",async()=>
            {
                var rows=new[]{Row("c","03",1),Row("a","00",5),Row("b","01",1)};var schedule=ShowProgramService.Validate(rows,mode);var a=rows[1];var b=rows[2];var c=rows[0];
                using var cancel=new CancellationTokenSource(TimeSpan.FromSeconds(20));var clock=new MasterClock(mode,.5,2);var entries=new List<ProgramObservation>();
                var play=new ProgramPlayback(window.Dispatcher).RunAsync(schedule,VideoRenderer.Displays()[0],clock,0,t=>{lock(entries)entries.Add(t);},cancel.Token,true);
                try
                {
                    await Task.Delay(250);await Supply(clock,0,115,cancel.Token);await Supply(clock,12,20,cancel.Token);await Supply(clock,87,20,cancel.Token);
                }
                finally{cancel.Cancel();try{await play;}catch(OperationCanceledException)when(cancel.IsCancellationRequested){}}
                ProgramObservation[] log;lock(entries)log=entries.ToArray();await File.WriteAllTextAsync(Path.Combine(folder,"program-render-log.json"),JsonSerializer.Serialize(log,new JsonSerializerOptions{WriteIndented=true}));
                Assert(log.Any(t=>t.CueId==a.CueId),"a shown");Assert(log.Any(t=>t.CueId==b.CueId),"b shown");Assert(log.Any(t=>t.CueId==c.CueId),"c shown");
                Assert(log.Where(t=>t.CueId==a.CueId).All(t=>((t.VerificationPixelBgra>>16)&255)>180 && (t.VerificationPixelBgra&255)<60),"a red pixels");
                Assert(log.Where(t=>t.CueId==b.CueId).All(t=>(t.VerificationPixelBgra&255)>180 && ((t.VerificationPixelBgra>>16)&255)<60),"b blue pixels");
                Assert(log.Where(t=>t.CueId==c.CueId).All(t=>((t.VerificationPixelBgra>>8)&255)>80 && (t.VerificationPixelBgra&255)<60),"c green pixels");
                Assert(log.Any(t=>t.Target>2.2 && t.Target<2.8 && t.CueId==null),"gap black");Assert(!log.Any(t=>t.Target>2.2 && t.Target<2.8 && t.CueId==a.CueId),"no a fallback");
                Assert(log.Where(t=>t.Target>2.2 && t.Target<2.8).All(t=>(t.VerificationPixelBgra&0xffffff)==0),"gap pixels black");
                Assert(log.Any(t=>t.PreloadId==b.CueId && t.Target<1),"b preloaded");Assert(log.Any(t=>t.PreloadId==c.CueId && t.Target<3),"c preloaded");
                Assert(log.Any(t=>t.State==SyncState.RESYNC),"jump resync");return new{observations=log.Length,presented=log[^1].Presented,skipped=log[^1].Skipped,seeks=log[^1].Seeks,latePreparation=log.Count(t=>t.Warning!=""),readback="every presented upload matches"};
            });
        }
        catch(Exception e){failed=true;await File.WriteAllTextAsync(Path.Combine(folder,"fatal.txt"),e.ToString());}
        finally{await File.WriteAllTextAsync(Path.Combine(folder,"exit-code.txt"),failed?"1":"0");Application.Current.Shutdown(failed?1:0);}
    }
    private static async Task Supply(MasterClock clock,int first,int count,CancellationToken token)
    {
        double baseline=MasterClock.Timestamp();for(int i=0;i<count;++i){double wait=baseline+i/25.0-MasterClock.Timestamp();if(wait>0)await Task.Delay(TimeSpan.FromSeconds(wait),token);int number=first+i;clock.Accept(new TcFrame{Seconds=number/25,Frame=number%25},MasterClock.Timestamp());}
    }
    private static void Assert(bool ok,string reason){if(!ok)throw new Exception(reason);}
}

