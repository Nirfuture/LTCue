using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;
namespace LtcPlayer.App;
public record VideoFrame(byte[] Pixels, double Seconds,int TextureFormat=0,int Width=0,int Height=0)
{
    public double DecodeMilliseconds{get;init;}
}
public record MediaInfo(int Width, int Height, string Codec, double Duration=0, double FrameRate=0,double StartTime=0) { public MediaFileVersion? FileVersion{get;init;} }
public sealed class MediaDecoder
{
    readonly bool gpuScaling;
    readonly SemaphoreSlim analysisGate=new(1);
    internal int AnalysisRuns{get;private set;}
    internal int HapIndexBuilds{get;private set;}
    readonly HapIndexCache? indexCache;
    readonly MediaAnalysisService? analysis;
    public MediaDecoder(bool reuseAnalysis=false,bool gpuScaling=false,HapIndexCache? indexCache=null,MediaAnalysisService? analysis=null){this.gpuScaling=gpuScaling;this.analysis=analysis??(reuseAnalysis?new MediaAnalysisService():null);this.indexCache=indexCache??this.analysis?.IndexCache;}
    internal static string FfmpegTime(double seconds)=>seconds.ToString("0.#################",CultureInfo.InvariantCulture);
    internal static Process Start(string executable, IEnumerable<string> args)
    {
        var si = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) si.ArgumentList.Add(arg);
        try {
#if VERIFICATION
            string kind=executable=="ffprobe"?(si.ArgumentList.Contains("-show_packets")?"index_ffprobe":"info_ffprobe"):"decode_ffmpeg";
            MediaProbeTrace.Mark(kind+"_start",si.ArgumentList.LastOrDefault()??"");
#endif
            var process=Process.Start(si) ?? throw new IOException(executable + " を起動できません");
#if VERIFICATION
            MediaProbeTrace.Mark(kind+"_started",process.Id.ToString());
#endif
            return process;
        }
        catch (System.ComponentModel.Win32Exception e) { throw new IOException(executable + " が見つかりません（PATHを確認）", e); }
    }
    internal static void Kill(Process p) { try { if (!p.HasExited) p.Kill(true); } catch (InvalidOperationException) { } }
    public async Task<MediaInfo> InspectAsync(string path, CancellationToken cancellation)
    {
        if(!File.Exists(path))throw new FileNotFoundException("ファイルなし",path);
        if(Path.GetExtension(path).ToLowerInvariant() is not (".mp4" or ".mov"))throw new InvalidDataException("対応形式：MP4/H.264、MOV/HAP・HAP Q");
        if(analysis!=null)return await InspectWithAsync(analysis,path,cancellation);
        await using var temporary=new MediaAnalysisService();return await InspectWithAsync(temporary,path,cancellation);
    }
    async Task<MediaInfo> InspectWithAsync(MediaAnalysisService service,string path,CancellationToken token){
        long before=service.ProbeRuns;
        try{return (await service.GetAsync(path,token).ConfigureAwait(false)).Video();}
        catch(InvalidDataException){service.Invalidate(path);throw;}
        finally{AnalysisRuns+=(int)(service.ProbeRuns-before);}
    }
    public async IAsyncEnumerable<VideoFrame> DecodeAsync(string path, int width, int height, [EnumeratorCancellation] CancellationToken cancellation, double seekSeconds = 0,Action<MediaPreparationStage>? progress=null)
    {
        if(!double.IsFinite(seekSeconds)||seekSeconds<0)throw new ArgumentOutOfRangeException(nameof(seekSeconds));
        if(Path.GetExtension(path).Equals(".mov",StringComparison.OrdinalIgnoreCase))
        {
            MediaInfo info;IReadOnlyList<HapMediaDecoder.Packet> packets;
            await analysisGate.WaitAsync(cancellation);
            try {
                info=await InspectAsync(path,cancellation);
                async Task<IReadOnlyList<HapMediaDecoder.Packet>> Build(CancellationToken token){HapIndexBuilds++;progress?.Invoke(MediaPreparationStage.Indexing);return await HapMediaDecoder.IndexAsync(path,info,token);}
                progress?.Invoke(MediaPreparationStage.WaitingForIndex);
                packets=indexCache==null?await Build(cancellation):await indexCache.GetAsync(path,info,Build,cancellation);
            }finally{analysisGate.Release();}
            await foreach(var frame in HapMediaDecoder.DecodeAsync(path,info,seekSeconds,cancellation,packets,progress))yield return frame;
            yield break;
        }
        int textureFormat=0;
        if(gpuScaling)
        {
            var info=await InspectAsync(path,cancellation);
            // Keep the canvas aspect ratio and black bars exactly; avoid CPU upscaling
            // only when an integral reduction fits the existing GPU BGRA path.
            double factor=Math.Min(1,Math.Max(info.Width/(double)width,info.Height/(double)height));
            int divisor=(int)Math.Floor(1/factor);
            while(divisor>1&&(width%divisor!=0||height%divisor!=0||(width/divisor)%4!=0||(height/divisor)%4!=0))divisor--;
            if(divisor>1){width/=divisor;height/=divisor;textureFormat=3;}
        }
        var times = Channel.CreateUnbounded<double>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
        var errors = new StringBuilder();
        var filter = $"scale={width}:{height}:force_original_aspect_ratio=decrease,pad={width}:{height}:(ow-iw)/2:(oh-ih)/2,setsar=1,showinfo";
        if(!double.IsFinite(seekSeconds) || seekSeconds<0) throw new ArgumentOutOfRangeException(nameof(seekSeconds));
        using var p = Start("ffmpeg", new[] { "-hide_banner", "-nostdin", "-loglevel", "info", "-ss", FfmpegTime(seekSeconds), "-i", path, "-map", "0:v:0", "-an", "-sn", "-dn", "-vf", filter, "-fps_mode", "passthrough", "-pix_fmt", "bgra", "-f", "rawvideo", "pipe:1" });
        using var registration = cancellation.Register(() => Kill(p));
        var stderr = Task.Run(async () =>
        {
            try
            {
                while (await p.StandardError.ReadLineAsync() is { } line)
                {
                    var match = Regex.Match(line, @"\bpts_time:([-+0-9.eE]+)");
                    if (line.Contains("showinfo") && match.Success)
                    {
                        var pts = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                        if (!double.IsFinite(pts)) throw new InvalidDataException("映像時刻の不正");
                        times.Writer.TryWrite(pts);
                    }
                    else { if (errors.Length > 12000) errors.Remove(0, 6000); errors.AppendLine(line); }
                }
                times.Writer.TryComplete();
            }
            catch (Exception e) { times.Writer.TryComplete(e); Kill(p); }
        });
        try
        {
            while (true)
            {
                using var frameTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                frameTimeout.CancelAfter(TimeSpan.FromSeconds(15));
                var pixels = new byte[checked(width * height * 4)]; var at = 0;
                while (at < pixels.Length)
                {
                    var n = await p.StandardOutput.BaseStream.ReadAsync(pixels.AsMemory(at), frameTimeout.Token);
                    if (n == 0) break; at += n;
                }
                if (at == 0) break;
                if (at != pixels.Length) throw new InvalidDataException("映像データが不完全です");
                var seconds = await times.Reader.ReadAsync(frameTimeout.Token);
                yield return new VideoFrame(pixels, seconds + seekSeconds,textureFormat,textureFormat==0?0:width,textureFormat==0?0:height);
            }
            await p.WaitForExitAsync(cancellation); await stderr;
            if (p.ExitCode != 0) throw new InvalidDataException("映像の復号失敗：" + errors);
        }
        finally { Kill(p); await stderr; }
    }
}

public static class MediaProbeTrace
{
#if VERIFICATION
    public static Action<string,string>? Observer;
#endif
    [Conditional("VERIFICATION")]
    public static void Mark(string name,string note=""){
#if VERIFICATION
        Observer?.Invoke(name,note);
#endif
    }
}
