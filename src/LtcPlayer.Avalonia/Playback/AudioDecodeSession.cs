using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;
namespace LtcPlayer.App;

public sealed record AudioMediaInfo(string Path,string Codec,double VideoStart,double? AudioStart,double Duration,double ContainerStart=0)
{
    public bool Supported=>Codec=="aac"||Codec.StartsWith("pcm_",StringComparison.Ordinal);
    public string Notice=>Codec==""?"音声なし":Supported?Codec:Codec+"は音声出力対象外";
    public static async Task<AudioMediaInfo> InspectAsync(string path,CancellationToken token)
    {
        if(!File.Exists(path))throw new FileNotFoundException("音声素材が見つかりません。",path);
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var p=MediaDecoder.Start("ffprobe",["-v","error","-show_entries","stream=codec_type,codec_name,start_time,duration:format=start_time,duration","-of","json",path]);
        using var registration=timeout.Token.Register(()=>MediaDecoder.Kill(p));
        var output=p.StandardOutput.ReadToEndAsync();var errors=p.StandardError.ReadToEndAsync();await p.WaitForExitAsync(timeout.Token);
        var detail=await errors;if(p.ExitCode!=0)throw new InvalidDataException("音声解析失敗: "+detail);
        using var doc=JsonDocument.Parse(await output);
        JsonElement? video=null,audio=null;
        foreach(var stream in doc.RootElement.GetProperty("streams").EnumerateArray())
        {
            if(stream.GetProperty("codec_type").GetString()=="video"&&video==null)video=stream;
            if(stream.GetProperty("codec_type").GetString()=="audio"&&audio==null)audio=stream;
        }
        if(video==null)throw new InvalidDataException("映像ストリームがありません。");
        static double? Number(JsonElement element,string key)=>element.TryGetProperty(key,out var v)&&double.TryParse(v.GetString(),NumberStyles.Float,CultureInfo.InvariantCulture,out double n)&&double.IsFinite(n)?n:null;
        var format=doc.RootElement.GetProperty("format");double start=Number(video.Value,"start_time")??Number(format,"start_time")??0;
        double duration=Number(video.Value,"duration")??Number(format,"duration")??0;
        if(duration<=0)throw new InvalidDataException("動画の長さを取得できません。");
        return new(System.IO.Path.GetFullPath(path),audio?.GetProperty("codec_name").GetString()??"",start,audio is {} a?Number(a,"start_time"):null,duration,Number(format,"start_time")??0);
    }
}

// One bounded FFmpeg decode window. Every block carries its actual filtered timestamp.
public sealed class AudioDecodeSession : IAsyncDisposable
{
    readonly CancellationTokenSource cancel;
    readonly Channel<AudioPcmBlock> blocks=Channel.CreateBounded<AudioPcmBlock>(8);
    readonly Task worker;
    readonly TaskCompletionSource ready=new(TaskCreationOptions.RunContinuationsAsynchronously);
    static int active,peak;
    int disposed;
    public static int ActiveDecoders=>Volatile.Read(ref active);
    public static int PeakDecoders=>Volatile.Read(ref peak);
    public Task Ready=>ready.Task;
    public bool Completed=>blocks.Reader.Completion.IsCompletedSuccessfully;
    public int BufferedBlocks=>blocks.Reader.Count;
    public double PreparationSeconds{get;private set;}
    public AudioDecodeSession(AudioMediaInfo info,Guid cueId,int generation,double seek,CancellationToken token,int preparationDelayMs=0)
    {
        if(!info.Supported||cueId==Guid.Empty||!double.IsFinite(seek)||seek<0||seek>=info.Duration)throw new ArgumentException("Invalid audio decode window");
        cancel=CancellationTokenSource.CreateLinkedTokenSource(token);
        worker=Task.Run(async()=>
        {
            double began=MasterClock.Timestamp();
            try
            {
                if(preparationDelayMs>0)await Task.Delay(preparationDelayMs,cancel.Token);
                await foreach(var block in DecodeAsync(info,cueId,generation,seek,cancel.Token))
                {
                    if(!ready.Task.IsCompleted){PreparationSeconds=MasterClock.Timestamp()-began;ready.TrySetResult();}
                    await blocks.Writer.WriteAsync(block,cancel.Token);
                }
                blocks.Writer.TryComplete();ready.TrySetException(new InvalidDataException("音声フレームがありません。"));
            }
            catch(OperationCanceledException)when(cancel.IsCancellationRequested){blocks.Writer.TryComplete();ready.TrySetCanceled(cancel.Token);}
            catch(Exception e){blocks.Writer.TryComplete(e);ready.TrySetException(e);}
        });
    }
    public bool TryRead(out AudioPcmBlock? block)
    {
        if(blocks.Reader.Completion.IsFaulted)blocks.Reader.Completion.GetAwaiter().GetResult();
        if(ready.Task.IsFaulted)ready.Task.GetAwaiter().GetResult();
        return blocks.Reader.TryRead(out block);
    }
    public async ValueTask DisposeAsync()
    {
        if(Interlocked.Exchange(ref disposed,1)!=0)return;
        cancel.Cancel();await worker;cancel.Dispose();
    }
    static async IAsyncEnumerable<AudioPcmBlock> DecodeAsync(AudioMediaInfo info,Guid cueId,int generation,double seek,[EnumeratorCancellation]CancellationToken token)
    {
        double absolute=info.VideoStart+seek,remaining=info.Duration-seek;
        string F(double n)=>MediaDecoder.FfmpegTime(n);
        // Keep input timestamps, then express audio relative to the requested VIDEO position.
        string filter=$"asetpts=PTS-({F(absolute)})/TB,aresample=48000:async=1:first_pts=0,apad,atrim=duration={F(remaining)},asetnsamples=n=1024:p=0,aformat=sample_fmts=flt:channel_layouts=stereo,ashowinfo";
        using var local=CancellationTokenSource.CreateLinkedTokenSource(token);
        StartupMeasurementTrace.Mark("audio_ffmpeg_start_request",seek);
        using var p=MediaDecoder.Start("ffmpeg",["-hide_banner","-nostdin","-loglevel","info","-copyts","-ss",F(Math.Max(0,absolute-info.ContainerStart)),"-i",info.Path,"-map","0:a:0","-vn","-sn","-dn","-af",filter,"-c:a","pcm_f32le","-f","f32le","pipe:1"]);
        StartupMeasurementTrace.Mark("audio_ffmpeg_process_created");
        int now=Interlocked.Increment(ref active);int prior;
        do{prior=Volatile.Read(ref peak);if(now<=prior)break;}while(Interlocked.CompareExchange(ref peak,now,prior)!=prior);
        using var registration=local.Token.Register(()=>MediaDecoder.Kill(p));
        var stamps=Channel.CreateBounded<(long Pts,int Frames)>(16);var errors=new StringBuilder();
        var stderr=Task.Run(async()=>
        {
            try
            {
                while(await p.StandardError.ReadLineAsync(local.Token) is {} line)
                {
                    if(line.Contains("ashowinfo")&&Regex.Match(line,@"\bpts:(-?\d+).+\bnb_samples:(\d+)") is {Success:true} match)
                    {
                        long pts=long.Parse(match.Groups[1].Value,CultureInfo.InvariantCulture);int frames=int.Parse(match.Groups[2].Value,CultureInfo.InvariantCulture);
                        if(pts<0||frames<1||frames>65536)throw new InvalidDataException("音声PTSまたはブロック長が不正です。");
                        await stamps.Writer.WriteAsync((pts,frames),local.Token);
                    }
                    else{if(errors.Length>16000)errors.Remove(0,8000);errors.AppendLine(line);}
                }
                stamps.Writer.TryComplete();
            }
            catch(Exception e){stamps.Writer.TryComplete(e);MediaDecoder.Kill(p);}
        });
        try
        {
            long expected=0;
            while(true)
            {
                using var frameTimeout=CancellationTokenSource.CreateLinkedTokenSource(local.Token);frameTimeout.CancelAfter(TimeSpan.FromSeconds(15));
                if(!await stamps.Reader.WaitToReadAsync(frameTimeout.Token))break;
                if(!stamps.Reader.TryRead(out var stamp))continue;
                if(Math.Abs(stamp.Pts-expected)>1)throw new InvalidDataException("音声PTSが不連続です。");
                var bytes=new byte[checked(stamp.Frames*8)];await p.StandardOutput.BaseStream.ReadExactlyAsync(bytes,frameTimeout.Token);
                StartupMeasurementTrace.Once("audio_first_pcm",seek+stamp.Pts/48000.0);
                var samples=new float[stamp.Frames*2];Buffer.BlockCopy(bytes,0,samples,0,bytes.Length);
                yield return new(cueId,generation,seek+stamp.Pts/48000.0,samples);expected=stamp.Pts+stamp.Frames;
            }
            await p.WaitForExitAsync(local.Token).WaitAsync(TimeSpan.FromSeconds(15),local.Token);await stderr;
            if(p.ExitCode!=0)throw new IOException("FFmpeg音声復号失敗: "+errors);
        }
        finally{local.Cancel();MediaDecoder.Kill(p);try{await stderr;}catch(OperationCanceledException){}Interlocked.Decrement(ref active);}
    }
}
