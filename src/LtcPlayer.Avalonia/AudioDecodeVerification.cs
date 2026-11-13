using System.Text.Json;
using LtcPlayer.App;
namespace LtcPlayer.Avalonia;

public static class AudioDecodeVerification
{
    public static async Task RunAsync(MainWindow w,string folder)
    {
        Directory.CreateDirectory(folder);bool failed=false;var results=new List<object>();
        string fixtures=Program.Option("--fixtures")!;
        string offsets=Program.Option("--audio-fixtures")!;
        void Check(bool ok,string message){if(!ok)throw new IOException(message);}
        async Task Test(string name,Func<Task> body){try{await body();results.Add(new{test=name,status="PASS"});}catch(Exception e){failed=true;results.Add(new{test=name,status="FAIL",detail=e.ToString()});}File.WriteAllText(Path.Combine(folder,"results.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));}
        async Task<float[]> Decode(AudioMediaInfo info,double seek)
        {
            var cue=Guid.NewGuid();await using var session=new AudioDecodeSession(info,cue,17,seek,CancellationToken.None);
            await session.Ready.WaitAsync(TimeSpan.FromSeconds(5));var samples=new List<float>();double next=seek;double deadline=MasterClock.Timestamp()+8;
            while(!session.Completed)
            {
                if(session.TryRead(out var block)){
                    Check(block!.CueId==cue&&block.Generation==17&&Math.Abs(block.Pts-next)<2.0/48000,"PTS/cue/generation mismatch");
                    samples.AddRange(block.Samples);next=block.Pts+block.Frames/48000.0;
                }
                else{if(MasterClock.Timestamp()>deadline)throw new TimeoutException("decode did not complete");await Task.Delay(1);}
                Check(session.BufferedBlocks<=8,"unbounded decode");
            }
            Check(Math.Abs(samples.Count/2-(info.Duration-seek)*48000)<=2,"decoded duration mismatch: "+samples.Count/2+" / "+(info.Duration-seek)*48000);
            return samples.ToArray();
        }
        try
        {
            await Test("AAC and PCM blocks have contiguous real PTS",async()=>{
                foreach(string file in new[]{"h264-aac.mp4","hap-pcm.mov"}){var info=await AudioMediaInfo.InspectAsync(Path.Combine(fixtures,file),CancellationToken.None);Check(info.Supported,"codec rejected");var samples=await Decode(info,0);Check(samples.Any(v=>Math.Abs(v)>.01),"silent decode");}
            });
            await Test("seek returns source-relative PTS and bounded duration",async()=>{
                foreach(string file in new[]{"h264-aac.mp4","hap-pcm.mov"}){var info=await AudioMediaInfo.InspectAsync(Path.Combine(fixtures,file),CancellationToken.None);await Decode(info,1.25);}
            });
            await Test("sub-millisecond seeks decode audio and video without exponent duration syntax",async()=>{
                string file=Path.Combine(fixtures,"h264-aac.mp4");var info=await AudioMediaInfo.InspectAsync(file,CancellationToken.None);
                foreach(double seek in new[]{.0000752,.0000000752}){await Decode(info,seek);bool read=false;await foreach(var frame in new MediaDecoder().DecodeAsync(file,320,180,CancellationToken.None,seek)){Check(frame.Seconds>=seek-.001&&frame.Seconds<.1,"wrong tiny-seek video PTS");read=true;break;}Check(read,"no tiny-seek video frame");}
            });
            await Test("late PCM audio preserves leading silence",async()=>{
                var info=await AudioMediaInfo.InspectAsync(Path.Combine(offsets,"late-pcm.mov"),CancellationToken.None);var samples=await Decode(info,0);
                Check(info.AudioStart-info.VideoStart>.3,"fixture has no offset");double first=Enumerable.Range(0,samples.Length/2).First(i=>Math.Abs(samples[i*2])>.01)/48000.0;
                Check(Math.Abs(first-(info.AudioStart!.Value-info.VideoStart))<.002,"leading silence lost: "+first);File.WriteAllText(Path.Combine(folder,"late-start.json"),JsonSerializer.Serialize(new{info.VideoStart,info.AudioStart,first}));
                var sought=await Decode(info,.1);int silenceFrames=(int)Math.Round((info.AudioStart.Value-info.VideoStart-.1)*48000);Check(sought.Take((silenceFrames-2)*2).All(v=>v==0),"seek removed remaining silence");
            });
            await Test("audio before video is trimmed to video origin",async()=>{
                var info=await AudioMediaInfo.InspectAsync(Path.Combine(offsets,"early-pcm.mov"),CancellationToken.None);var samples=await Decode(info,0);
                double expected=.1+.1*info.VideoStart;Check(info.VideoStart>.4&&Math.Abs(samples[0]-expected)<.002,"wrong early audio trim: "+samples[0]+" expected "+expected);
                var sought=await Decode(info,.75);Check(Math.Abs(sought[0]-(expected+.075))<.002,"seek used wrong origin");
            });
            await Test("nonzero container timestamp keeps AAC source position",async()=>{
                var original=await AudioMediaInfo.InspectAsync(Path.Combine(fixtures,"h264-aac.mp4"),CancellationToken.None);var shifted=await AudioMediaInfo.InspectAsync(Path.Combine(offsets,"nonzero-aac.mp4"),CancellationToken.None);
                Check(shifted.VideoStart>4,"fixture is not timestamp shifted");var a=await Decode(original,1);var b=await Decode(shifted,1);
                double difference=a.Take(48000).Zip(b.Take(48000),(x,y)=>Math.Abs(x-y)).Average();Check(difference<.002,"shifted AAC waveform differs: "+difference);
            });
            await Test("audio-free and unsupported tracks do not open a decoder",async()=>{
                foreach(string file in new[]{"hap-q-silent.mov","h264-mp3.mp4"}){var info=await AudioMediaInfo.InspectAsync(Path.Combine(fixtures,file),CancellationToken.None);Check(!info.Supported,"unexpected codec support");}
                Check(AudioDecodeSession.ActiveDecoders==0,"decoder leaked");
            });
            await Test("cancelled preparation and a full queue release FFmpeg",async()=>{
                var info=await AudioMediaInfo.InspectAsync(Path.Combine(fixtures,"h264-aac.mp4"),CancellationToken.None);
                await using(var slow=new AudioDecodeSession(info,Guid.NewGuid(),1,0,CancellationToken.None,500)){await slow.DisposeAsync();Check(slow.Ready.IsCanceled,"preparation not cancelled");}
                await using(var full=new AudioDecodeSession(info,Guid.NewGuid(),2,0,CancellationToken.None)){await full.Ready;await Task.Delay(100);Check(full.BufferedBlocks<=8,"queue unbounded");await full.DisposeAsync();}
                Check(AudioDecodeSession.ActiveDecoders==0,"FFmpeg leaked");
            });
        }
        finally{await w.Monitor.DisposeAsync();MainWindow.Shutdown(failed?1:0);}
    }
}
