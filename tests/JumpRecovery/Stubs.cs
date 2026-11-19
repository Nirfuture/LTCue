using System.Runtime.CompilerServices;
namespace LtcPlayer.App {
 public struct TcFrame {public int Hours,Minutes,Seconds,Frame,Drop,Reverse;}
 public record VideoFrame(byte[] Pixels,double Seconds,int TextureFormat=0,int Width=0,int Height=0);
 public record MediaInfo(double Duration=10,double FrameRate=25,string Codec="h264"){public int Width=>1920;public int Height=>1080;}
 public sealed class HapIndexCache {}
 public sealed class MediaAnalysisService {public void Invalidate(string path){}}
 public sealed class MediaDecoder {
  public MediaDecoder(bool reuseAnalysis=false,bool gpuScaling=false,HapIndexCache? indexCache=null,MediaAnalysisService? analysis=null){}
  public static int DelayMs=80;
  public static int PreparationMs;
  public static int Active,Peak;
  public static bool FailRecovery;
  public static Action<string,double>? Opened;public static Func<Task>? Disposing;
  public Task<MediaInfo> InspectAsync(string path,CancellationToken token)=>Task.FromResult(new MediaInfo());
  public async IAsyncEnumerable<VideoFrame> DecodeAsync(string path,int width,int height,[EnumeratorCancellation]CancellationToken token,double seek=0) {
   Opened?.Invoke(path,seek);int active=Interlocked.Increment(ref Active);Peak=Math.Max(Peak,active);
   try {
    await Task.Delay(PreparationMs,token);
    if(FailRecovery&&seek>.2)throw new IOException("Synthetic recovery failure");
    for(int i=(int)Math.Ceiling(seek*25);i<750;i++){await Task.Delay(DelayMs,token);var data=new byte[width*height*4];data[0]=(byte)(path.Contains("new")?2:1);yield return new(data,i/25.0);}
   } finally {if(Disposing!=null)await Disposing();Interlocked.Decrement(ref Active);}
  }
 }
 public record OutputDisplay(int Width=4,int Height=4);
 public record ProgramMonitorStatus(int Error=0);
 public sealed class VideoRenderer : IDisposable,ICoverRenderer {
  public void SetCover(CoverImage? image){} public void SetCoverMix(float value){} public void Redraw(){}
  public OutputPresentationMode OutputMode{get;}
  public VideoRenderer(OutputDisplay d,OutputPresentationMode mode=OutputPresentationMode.Standard){OutputMode=mode;}
  public void EnableStopConfirmation(){} public void AttachMonitor(nint p){}
  public bool PollStopRequest()=>false; public void UpdateMonitor(){}
  public ProgramMonitorStatus MonitorStatus()=>new();
  public void Present(byte[] p){} public void Present(VideoFrame f){}
  public byte[] Readback()=>new byte[64]; public void Verify(byte[] p){} public void Dispose(){}
 }
}
namespace LtcPlayer.Avalonia {public sealed class UiDispatcher {public Action? BeforeInvoke;public Task InvokeAsync(Action action){BeforeInvoke?.Invoke();action();return Task.CompletedTask;}}}
