namespace LtcPlayer.App;
public struct TcFrame { public int Hours, Minutes, Seconds, Frame, Drop, Reverse; }
public record MediaInfo(double Duration = 10, double FrameRate = 25, string Codec = "h264")
{ public int Width => 1920; public int Height => 1080; }
// The calculation/editor tests do not decode media. Preparation gates exercise the real import boundary.
public sealed class MediaAnalysisService {public void Invalidate(string path){}}
 public sealed class MediaDecoder
{
    public MediaDecoder(MediaAnalysisService? analysis=null){}
    public static Func<string, Task<MediaInfo>> Inspect { get; set; } = _ => Task.FromResult(new MediaInfo());
    public Task<MediaInfo> InspectAsync(string path, CancellationToken token) => Inspect(path);
}
