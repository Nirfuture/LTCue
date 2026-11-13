namespace LtcPlayer.App;
public interface IProgramPresentation : IDisposable
{
 void Present(VideoFrame frame);
 void Refresh();
}
