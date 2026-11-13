using System.Runtime.CompilerServices;
namespace LtcPlayer.App {
 public struct TcFrame {public int Hours,Minutes,Seconds,Frame,Drop,Reverse;}
 public record VideoFrame(byte[] Pixels,double Seconds,int TextureFormat=0,int Width=0,int Height=0);
 public record MediaInfo(double Duration=10,double FrameRate=25,string Codec="h264"){public int Width=>1920;public int Height=>1080;}
 public sealed class MediaDecoder {
  public MediaDecoder(bool reuseAnalysis=false,bool gpuScaling=false){}
  public static int DelayMs=80;
  public static int PreparationMs;
  public static int Active,Peak;
  public static bool FailRecovery;
  public Task<MediaInfo> InspectAsync(string path,CancellationToken token)=>Task.FromResult(new MediaInfo());
  public async IAsyncEnumerable<VideoFrame> DecodeAsync(string path,int width,int height,[EnumeratorCancellation]CancellationToken token,double seek=0) {
   int active=Interlocked.Increment(ref Active);Peak=Math.Max(Peak,active);
   try {
    await Task.Delay(PreparationMs,token);
    if(FailRecovery&&seek>.2)throw new IOException("Synthetic recovery failure");
    for(int i=(int)Math.Ceiling(seek*25);i<750;i++){await Task.Delay(DelayMs,token);yield return new(new byte[width*height*4],i/25.0);}
   } finally {Interlocked.Decrement(ref Active);}
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
namespace LtcPlayer.Avalonia {public sealed class UiDispatcher {public Task InvokeAsync(Action action){action();return Task.CompletedTask;}}}
