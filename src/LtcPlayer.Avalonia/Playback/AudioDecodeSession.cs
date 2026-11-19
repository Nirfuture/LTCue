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
    public MediaFileVersion? FileVersion{get;init;}
    public bool Supported=>Codec=="aac"||Codec.StartsWith("pcm_",StringComparison.Ordinal);
    public string Notice=>Codec==""?"音声なし":Supported?Codec:Codec+"は音声形式非対応";
    public static async Task<AudioMediaInfo> InspectAsync(string path,CancellationToken token,MediaAnalysisService? analysis=null)
    {
        if(!File.Exists(path))throw new FileNotFoundException("音声ファイルなし",path);
        if(analysis==null){await using var temporary=new MediaAnalysisService();return await InspectAsync(path,token,temporary);}
        try{var shared=await analysis.GetAsync(path,token);var a=shared.Audio();return new AudioMediaInfo(shared.FilePath,a.Codec,a.VideoStart,a.AudioStart,a.Duration,a.ContainerStart){FileVersion=shared.Version};}
        catch(InvalidDataException){analysis.Invalidate(path);throw;}
    }

}
// One bounded FFmpeg decode window. Every block carries its actual filtered timestamp.
public sealed class AudioDecodeSession : IAsyncDisposable
{
    readonly CancellationTokenSource cancel;
    readonly Channel<AudioPcmBlock> blocks=Channel.CreateBounded<AudioPcmBlock>(8);
    readonly Task worker;
    readonly TaskCompletionSource ready=new(TaskCreationOptions.RunContinuationsAsynchronously);
    static int active,peak,processActive,processPeak;
    static long processStarts;
    public static long ProcessStarts=>Interlocked.Read(ref processStarts);
    int disposed;
    public static int ActiveDecoders=>Volatile.Read(ref active);
    public static int PeakDecoders=>Volatile.Read(ref peak);
    public static int ActiveProcesses=>Volatile.Read(ref processActive);
    public static int PeakProcesses=>Volatile.Read(ref processPeak);
    public bool UsingDirectPcm{get;private set;}
    public Task Ready=>ready.Task;
    public bool Completed=>blocks.Reader.Completion.IsCompletedSuccessfully;
    public int BufferedBlocks=>blocks.Reader.Count;
    public double PreparationSeconds{get;private set;}
    public AudioDecodeSession(AudioMediaInfo info,Guid cueId,int generation,double seek,CancellationToken token,int preparationDelayMs=0):this(info,cueId,generation,seek,token,preparationDelayMs,true){}
    internal AudioDecodeSession(AudioMediaInfo info,Guid cueId,int generation,double seek,CancellationToken token,int preparationDelayMs,bool useDirectPcm)
    {
        if(!info.Supported||cueId==Guid.Empty||!double.IsFinite(seek)||seek<0||seek>=info.Duration)throw new ArgumentException("Invalid audio decode window");
        cancel=CancellationTokenSource.CreateLinkedTokenSource(token);
        worker=Task.Run(async()=>
        {
            int windows=Interlocked.Increment(ref active);SetPeak(ref peak,windows);
            double began=MasterClock.Timestamp();
            try
            {
                if(preparationDelayMs>0)await Task.Delay(preparationDelayMs,cancel.Token);
                StartupMeasurementTrace.Mark("audio_direct_prepare_begin");
                await using var direct=useDirectPcm?await MovPcmReader.TryOpenAsync(info,cancel.Token):null;
                UsingDirectPcm=direct!=null;StartupMeasurementTrace.Mark("audio_decode_path",note:UsingDirectPcm?"mov-pcm":"ffmpeg");
                StartupMeasurementTrace.Mark("audio_direct_prepare_complete");
                var stream=direct!=null?direct.ReadAsync(cueId,generation,seek,cancel.Token):DecodeAsync(info,cueId,generation,seek,cancel.Token);
                await foreach(var block in stream)
                {
                    StartupMeasurementTrace.Once("audio_first_pcm",block.Pts);
                    if(!ready.Task.IsCompleted){PreparationSeconds=MasterClock.Timestamp()-began;ready.TrySetResult();}
                    await blocks.Writer.WriteAsync(block,cancel.Token);
                }
                blocks.Writer.TryComplete();ready.TrySetException(new InvalidDataException("音声フレームなし"));
            }
            catch(OperationCanceledException)when(cancel.IsCancellationRequested){blocks.Writer.TryComplete();ready.TrySetCanceled(cancel.Token);}
            catch(Exception e){blocks.Writer.TryComplete(e);ready.TrySetException(e);}
            finally{Interlocked.Decrement(ref active);}
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
        Interlocked.Increment(ref processStarts);
        int now=Interlocked.Increment(ref processActive);SetPeak(ref processPeak,now);
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
                        if(pts<0||frames<1||frames>65536)throw new InvalidDataException("音声データ不正");
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
#if VERIFICATION
            bool measuredMemory=false;
#endif
            while(true)
            {
                using var frameTimeout=CancellationTokenSource.CreateLinkedTokenSource(local.Token);frameTimeout.CancelAfter(TimeSpan.FromSeconds(15));
                if(!await stamps.Reader.WaitToReadAsync(frameTimeout.Token))break;
                if(!stamps.Reader.TryRead(out var stamp))continue;
                if(Math.Abs(stamp.Pts-expected)>1)throw new InvalidDataException("音声時刻の不連続");
                var bytes=new byte[checked(stamp.Frames*8)];await p.StandardOutput.BaseStream.ReadExactlyAsync(bytes,frameTimeout.Token);
                StartupMeasurementTrace.Once("audio_first_pcm",seek+stamp.Pts/48000.0);
#if VERIFICATION
                if(!measuredMemory){measuredMemory=true;try{p.Refresh();StartupMeasurementTrace.Once("audio_ffmpeg_working_set",p.WorkingSet64);StartupMeasurementTrace.Once("audio_ffmpeg_private_bytes",p.PrivateMemorySize64);StartupMeasurementTrace.Once("audio_ffmpeg_handles",p.HandleCount);}catch(Exception e)when(e is InvalidOperationException or System.ComponentModel.Win32Exception){StartupMeasurementTrace.Mark("audio_ffmpeg_resources_unavailable",note:e.Message);}}
#endif
                var samples=new float[stamp.Frames*2];Buffer.BlockCopy(bytes,0,samples,0,bytes.Length);
                yield return new(cueId,generation,seek+stamp.Pts/48000.0,samples);expected=stamp.Pts+stamp.Frames;
            }
            await p.WaitForExitAsync(local.Token).WaitAsync(TimeSpan.FromSeconds(15),local.Token);await stderr;
            if(p.ExitCode!=0)throw new IOException("音声の復号失敗："+errors);
        }
        finally{local.Cancel();MediaDecoder.Kill(p);try{await stderr;}catch(OperationCanceledException){}Interlocked.Decrement(ref processActive);}
    }
    static void SetPeak(ref int peak,int value){int prior;do{prior=Volatile.Read(ref peak);if(value<=prior)return;}while(Interlocked.CompareExchange(ref peak,value,prior)!=prior);}
}
