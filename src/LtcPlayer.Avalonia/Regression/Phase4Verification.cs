using System.Diagnostics;
using System.Text.Json;
using Window = LtcPlayer.Avalonia.RegressionWindow;
using Application = LtcPlayer.Avalonia.RegressionApplication;


namespace LtcPlayer.App;
public static class Phase4Verification
{
    public static async Task RunAsync(Window window,string folder)
    {
        var results=new List<object>();bool failed=false;Directory.CreateDirectory(folder);
        async Task Test(string name,Func<Task<object>> action)
        {
            try{results.Add(new{test=name,status="PASS",detail=await action()});}
            catch(Exception e){failed=true;results.Add(new{test=name,status="FAIL",detail=e.ToString()});}
            await File.WriteAllTextAsync(Path.Combine(folder,"results.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true,IncludeFields=true}));
        }
        try
        {
            await Test("Snappy copy formats, extended HAP header, malformed rejection",()=>
            {
                byte[][] copies=[[7,8,120,97,98,1,2],[7,8,120,97,98,14,2,0],[7,8,120,97,98,15,2,0,0,0]];
                foreach(var bytes in copies)Assert(System.Text.Encoding.ASCII.GetString(HapCodec.Snappy(bytes,7))=="xababab","copy decoding");
                byte[] extended=[0,0,0,0xab,8,0,0,0,1,2,3,4,5,6,7,8];Assert(HapCodec.Unpack(extended,4,4,1).SequenceEqual(new byte[]{1,2,3,4,5,6,7,8}),"extended header");
                int rejected=0;foreach(var bytes in new byte[][]{[4,1,0],[255,255,255,255,31],[8,0],[4,0,1,1,0]})try{HapCodec.Snappy(bytes,16);}catch(Exception e)when(e is InvalidDataException or OverflowException){++rejected;}
                Assert(rejected==4,"malformed Snappy");bool bad=false;try{HapCodec.Unpack(new byte[]{1,0,0,0xae,0},4,4,1);}catch(InvalidDataException){bad=true;}Assert(bad,"alpha rejected");return Task.FromResult<object>(new{copyFormats=3,rejected});
            });
            foreach(var name in new[]{"hap_none","hap_snappy","hap_chunks","hap_q_none","hap_q_snappy","hap_q_chunks"})
                await Test(name+" BC upload/shader vs FFmpeg CPU",()=>Compare(window,name,folder));
            await Test("HAP seek PTS and compressed frame size",async()=>
            {
                using var cancel=new CancellationTokenSource(TimeSpan.FromSeconds(20));await foreach(var frame in new MediaDecoder().DecodeAsync(Path.Combine(folder,"hap_q_chunks.mov"),1920,1080,cancel.Token,.41))
                {Assert(frame.TextureFormat==2 && frame.Pixels.Length==1920*1080,"BC3 size");Assert(Math.Abs(frame.Seconds-.44)<.001,"seek timestamp");return new{frame.Seconds,bytes=frame.Pixels.Length,frame.TextureFormat};}throw new Exception("No seek frame");
            });
            await Test("malformed MOV frame rejected without CPU fallback",async()=>
            {
                string original=Path.Combine(folder,"hap_none.mov"),bad=Path.Combine(folder,"bad-hap.mov");File.Copy(original,bad,true);
                var info=await new MediaDecoder().InspectAsync(bad,CancellationToken.None);var index=await HapMediaDecoder.IndexAsync(bad,info,CancellationToken.None);
                using(var file=new FileStream(bad,FileMode.Open,FileAccess.Write)){file.Position=index[0].Position+3;file.WriteByte(0xae);}
                bool rejected=false;try{await foreach(var frame in new MediaDecoder().DecodeAsync(bad,1920,1080,CancellationToken.None))break;}catch(InvalidDataException){rejected=true;}
                Assert(rejected,"corrupted texture format accepted");return "MOV metadata valid but malformed HAP payload rejected";
            });
            await Test("paced compressed playback timing",async()=>
            {
                var measurements=new List<object>();foreach(var name in new[]{"hap_snappy","hap_q_chunks"})
                {
                    using var cancel=new CancellationTokenSource(TimeSpan.FromSeconds(20));var timer=Stopwatch.StartNew();
                    var result=await new PlaybackService(window.Dispatcher).PlayAsync(Path.Combine(folder,name+".mov"),VideoRenderer.Displays()[0],true,cancel.Token);
                    Assert(result.Frames==25 && result.LastPts-result.FirstPts>.95,"paced frame count and timestamps");
                    measurements.Add(new{name,totalHostSeconds=timer.Elapsed.TotalSeconds,result});
                }
                return measurements;
            });
            await Test("mixed H264/HAP/HAPQ cues and preload",async()=>
            {
                var cues=new List<ScheduledCue>();foreach(var pair in new[]{("h264.mp4",0.0),("hap_snappy.mov",1.0),("hap_q_chunks.mov",2.0)})
                {string path=Path.Combine(folder,pair.Item1);var info=await new MediaDecoder().InspectAsync(path,CancellationToken.None);cues.Add(new(Guid.NewGuid(),path,pair.Item2,info.Duration,info.FrameRate,info.Codec));}
                var rows=cues.Select(c=>new CueRow{CueId=c.CueId,FilePath=c.FilePath,Hours="00",Minutes="00",Seconds=((int)c.Start).ToString("00"),Frames="00",Duration=c.Duration,FrameRate=c.FrameRate,Codec=c.Codec}).Reverse().ToList();
                await ShowProgramService.SaveAsync(Path.Combine(folder,"mixed-show.json"),rows,TimecodeRate.Modes[1]);var restored=await ShowProgramService.LoadAsync(Path.Combine(folder,"mixed-show.json"),CancellationToken.None);
                Assert(ShowProgramService.Validate(restored.Rows,restored.Rate).Count==3,"mixed JSON restore");
                using var cancel=new CancellationTokenSource(TimeSpan.FromSeconds(12));var clock=new MasterClock(TimecodeRate.Modes[1],.5,2);var log=new List<ProgramObservation>();
                var run=new ProgramPlayback(window.Dispatcher).RunAsync(cues,VideoRenderer.Displays()[0],clock,0,t=>{lock(log)log.Add(t);},cancel.Token,true);
                try{double baseline=MasterClock.Timestamp();for(int i=0;i<85;++i){double wait=baseline+i/25.0-MasterClock.Timestamp();if(wait>0)await Task.Delay(TimeSpan.FromSeconds(wait));clock.Accept(new TcFrame{Seconds=i/25,Frame=i%25},MasterClock.Timestamp());}}
                finally{cancel.Cancel();try{await run;}catch(OperationCanceledException)when(cancel.IsCancellationRequested){}}
                ProgramObservation[] entries;lock(log)entries=log.ToArray();await File.WriteAllTextAsync(Path.Combine(folder,"mixed-render-log.json"),JsonSerializer.Serialize(entries,new JsonSerializerOptions{WriteIndented=true}));
                foreach(var cue in cues)Assert(entries.Any(t=>t.CueId==cue.CueId),"cue displayed: "+cue.Codec);
                Assert(entries.Any(t=>t.PreloadId==cues[1].CueId && t.Target<1),"HAP preload");Assert(entries.Any(t=>t.PreloadId==cues[2].CueId && t.Target<2),"HAPQ preload");return new{observations=entries.Length,presented=entries[^1].Presented,latePreparation=entries.Count(t=>t.Warning!=""),codecs=cues.Select(c=>c.Codec).ToArray()};
            });
        }
        catch(Exception e){failed=true;await File.WriteAllTextAsync(Path.Combine(folder,"fatal.txt"),e.ToString());}
        finally{await File.WriteAllTextAsync(Path.Combine(folder,"exit-code.txt"),failed?"1":"0");Application.Current.Shutdown(failed?1:0);}
    }
    private static async Task<object> Compare(Window window,string name,string folder)
    {
        var display=VideoRenderer.Displays()[0];Assert(display.Width==1920 && display.Height==1080,"This oracle fixture requires 1920x1080 output");
        string path=Path.Combine(folder,name+".mov");using var cancel=new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var info=await new MediaDecoder().InspectAsync(path,cancel.Token);VideoRenderer? renderer=null;
        using var cpu=MediaDecoder.Start("ffmpeg",new[]{"-hide_banner","-v","error","-nostdin","-i",path,"-map","0:v:0","-an","-pix_fmt","bgra","-f","rawvideo","pipe:1"});
        using var registration=cancel.Token.Register(()=>MediaDecoder.Kill(cpu));var errors=cpu.StandardError.ReadToEndAsync();
        var decode=new List<double>();var present=new List<double>();var whole=Stopwatch.StartNew();double first=0;long difference=0,channels=0,over4=0;int max=0,count=0;byte[]? captured=null;
        try
        {
            await window.Dispatcher.InvokeAsync(()=>renderer=new VideoRenderer(display));
            await foreach(var frame in new MediaDecoder().DecodeAsync(path,1920,1080,cancel.Token))
            {
                if(count==0)first=whole.Elapsed.TotalMilliseconds;
                Assert(frame.TextureFormat==(info.Codec=="hap"?1:2),"compressed route");decode.Add(frame.DecodeMilliseconds);
                var expected=new byte[1920*1080*4];await cpu.StandardOutput.BaseStream.ReadExactlyAsync(expected,cancel.Token);
                await window.Dispatcher.InvokeAsync(()=>{var time=Stopwatch.StartNew();renderer!.Present(frame);present.Add(time.Elapsed.TotalMilliseconds);captured=renderer.Readback();});
                for(int i=0;i<expected.Length;i+=4)for(int c=0;c<3;++c){int d=Math.Abs(captured![i+c]-expected[i+c]);difference+=d;++channels;max=Math.Max(max,d);if(d>4)++over4;}
                ++count;
            }
            await cpu.WaitForExitAsync(cancel.Token);string error=await errors;Assert(cpu.ExitCode==0,error);Assert(count==25,"frame count");
            double mean=difference/(double)channels;
            // Predeclared tolerances allow hardware BC interpolation/UNORM rounding differences.
            Assert(mean<=1.5 && max<=8,"GPU/CPU color difference: mean="+mean+", max="+max);
            await window.Dispatcher.InvokeAsync(()=>SavePng(Path.Combine(folder,name+"-gpu.png"),captured!,1920,1080));
            double Median(List<double> values)=>values.Order().ElementAt(values.Count/2);
            return new{count,codec=info.Codec,meanAbsoluteRgbError=mean,maxRgbError=max,fractionOver4=over4/(double)channels,firstFrameMilliseconds=first,decodeMedianMilliseconds=Median(decode),decodeMaxMilliseconds=decode.Max(),presentHostMedianMilliseconds=Median(present),presentHostMaxMilliseconds=present.Max(),verifiedFramesPerSecond=count/whole.Elapsed.TotalSeconds,compressedBytesPerFrame=1920*1080/(info.Codec=="hap"?2:1),note="host timings; CPU reference/readback included in verified fps; no GPU timestamp measurement"};
        }
        finally{MediaDecoder.Kill(cpu);await errors;if(renderer!=null)await window.Dispatcher.InvokeAsync(renderer.Dispose);}
    }
    private static void SavePng(string path,byte[] pixels,int width,int height)
    {
        LtcPlayer.Avalonia.RegressionWindow.SavePng(path,pixels,width,height);
    }
    private static void Assert(bool ok,string reason){if(!ok)throw new Exception(reason);}
}


