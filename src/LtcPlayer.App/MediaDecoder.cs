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
public record MediaInfo(int Width, int Height, string Codec, double Duration=0, double FrameRate=0,double StartTime=0);
public sealed class MediaDecoder
{
    // Each decoder keeps one file version. Production retains at most two cached files per session.
    readonly bool reuseAnalysis;
    readonly bool gpuScaling;
    readonly SemaphoreSlim analysisGate=new(1);
    internal int AnalysisRuns{get;private set;}
    internal int HapIndexBuilds{get;private set;}
    (string Path,long Length,DateTime Modified)? cachedFile;
    MediaInfo? cachedInfo;
    HapMediaDecoder.Packet[]? cachedPackets;
    public MediaDecoder(bool reuseAnalysis=false,bool gpuScaling=false){this.reuseAnalysis=reuseAnalysis;this.gpuScaling=gpuScaling;}
    static (string Path,long Length,DateTime Modified) FileVersion(string path)
    {
        var file=new FileInfo(path);
        return (file.FullName,file.Length,file.LastWriteTimeUtc);
    }
    internal static string FfmpegTime(double seconds)=>seconds.ToString("0.#################",CultureInfo.InvariantCulture);
    internal static Process Start(string executable, IEnumerable<string> args)
    {
        var si = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) si.ArgumentList.Add(arg);
        try { return Process.Start(si) ?? throw new IOException(executable + " を開始できません。"); }
        catch (System.ComponentModel.Win32Exception e) { throw new IOException(executable + " をPATH上に用意してください。", e); }
    }
    internal static void Kill(Process p) { try { if (!p.HasExited) p.Kill(true); } catch (InvalidOperationException) { } }
    public async Task<MediaInfo> InspectAsync(string path, CancellationToken cancellation)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("動画ファイルが見つかりません。", path);
        cancellation.ThrowIfCancellationRequested();
        var version=reuseAnalysis?FileVersion(path):default;
        if(reuseAnalysis && cachedFile==version && cachedInfo!=null)return cachedInfo;
        if(reuseAnalysis){cachedFile=null;cachedInfo=null;cachedPackets=null;}
        var ext=Path.GetExtension(path).ToLowerInvariant();if(ext is not (".mp4" or ".mov"))throw new InvalidDataException("MP4/H.264またはMOV/HAP・HAP Qを指定してください。");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        AnalysisRuns++;
        using var p = Start("ffprobe", new[] { "-v", "error", "-select_streams", "v:0", "-show_entries", "stream=codec_name,codec_tag_string,width,height,duration,avg_frame_rate,start_time,sample_aspect_ratio:format=duration", "-of", "json", path });
        using var registration = timeout.Token.Register(() => Kill(p));
        var output = p.StandardOutput.ReadToEndAsync(); var error = p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync(timeout.Token);
        var json = await output; var detail = await error;
        if (p.ExitCode != 0) throw new InvalidDataException("動画を解析できません: " + detail);
        using var doc = JsonDocument.Parse(json);
        var streams = doc.RootElement.GetProperty("streams");
        if (streams.GetArrayLength() == 0) throw new InvalidDataException("映像ストリームがありません。");
        var s = streams[0];
        var info = new MediaInfo(s.GetProperty("width").GetInt32(), s.GetProperty("height").GetInt32(), s.GetProperty("codec_name").GetString()!);
        string tag=s.GetProperty("codec_tag_string").GetString()!;
        if(ext==".mov" && info.Codec=="hap" && tag is "Hap1" or "HapY")info=info with{Codec=tag=="Hap1"?"hap":"hap_q"};
        else if(ext!=".mp4" || info.Codec!="h264")throw new InvalidDataException("対応外コンテナ/コーデック: "+tag+" / "+info.Codec);
        if(info.Codec!="h264" && (info.Width%4!=0||info.Height%4!=0))throw new InvalidDataException("HAPの幅・高さは4の倍数が必要です。");
        if(info.Codec!="h264" && s.TryGetProperty("sample_aspect_ratio",out var sar) && sar.GetString() is not ("1:1" or "0:1" or "N/A"))throw new InvalidDataException("HAPは正方形ピクセルのみ対応しています。");
        if (info.Width <= 0 || info.Height <= 0 || info.Width > 8192 || info.Height > 8192) throw new InvalidDataException("映像サイズが範囲外です。");
        double duration=0, fps=0;
        if(s.TryGetProperty("duration",out var dur)) double.TryParse(dur.GetString(),NumberStyles.Float,CultureInfo.InvariantCulture,out duration);
        if(duration<=0 && doc.RootElement.TryGetProperty("format",out var format) && format.TryGetProperty("duration",out dur)) double.TryParse(dur.GetString(),NumberStyles.Float,CultureInfo.InvariantCulture,out duration);
        if(s.TryGetProperty("avg_frame_rate",out var rate)) {var parts=rate.GetString()!.Split('/');if(parts.Length==2 && double.TryParse(parts[0],NumberStyles.Float,CultureInfo.InvariantCulture,out var n) && double.TryParse(parts[1],NumberStyles.Float,CultureInfo.InvariantCulture,out var d) && d>0)fps=n/d;}
        if(!double.IsFinite(duration)||duration<=0||!double.IsFinite(fps)||fps<=0)throw new InvalidDataException("映像の長さ・フレームレートを取得できません。");
        double startTime=0;if(s.TryGetProperty("start_time",out var st))double.TryParse(st.GetString(),NumberStyles.Float,CultureInfo.InvariantCulture,out startTime);
        info=info with {Duration=duration,FrameRate=fps,StartTime=startTime};
        if(reuseAnalysis && FileVersion(path)==version){cachedFile=version;cachedInfo=info;}
        return info;
    }
    public async IAsyncEnumerable<VideoFrame> DecodeAsync(string path, int width, int height, [EnumeratorCancellation] CancellationToken cancellation, double seekSeconds = 0)
    {
        if(!double.IsFinite(seekSeconds)||seekSeconds<0)throw new ArgumentOutOfRangeException(nameof(seekSeconds));
        if(Path.GetExtension(path).Equals(".mov",StringComparison.OrdinalIgnoreCase))
        {
            MediaInfo info;HapMediaDecoder.Packet[] packets;
            await analysisGate.WaitAsync(cancellation);
            try {
                info=await InspectAsync(path,cancellation);
                var version=reuseAnalysis?FileVersion(path):default;
                var existing=reuseAnalysis&&cachedFile==version?cachedPackets:null;
                if(existing==null){HapIndexBuilds++;existing=await HapMediaDecoder.IndexAsync(path,info,cancellation);}
                packets=existing;
                if(reuseAnalysis&&cachedFile==version&&FileVersion(path)==version)cachedPackets=packets;
            }finally{analysisGate.Release();}
            await foreach(var frame in HapMediaDecoder.DecodeAsync(path,info,seekSeconds,cancellation,packets))yield return frame;
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
                        if (!double.IsFinite(pts)) throw new InvalidDataException("無効なPTSです。");
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
                if (at != pixels.Length) throw new InvalidDataException("復号フレームが途中で切れています。");
                var seconds = await times.Reader.ReadAsync(frameTimeout.Token);
                yield return new VideoFrame(pixels, seconds + seekSeconds,textureFormat,textureFormat==0?0:width,textureFormat==0?0:height);
            }
            await p.WaitForExitAsync(cancellation); await stderr;
            if (p.ExitCode != 0) throw new InvalidDataException("FFmpeg復号失敗: " + errors);
        }
        finally { Kill(p); await stderr; }
    }
}
