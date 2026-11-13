namespace LtcPlayer.App;

// Both manual and LTC sources publish into the host's persistent full-size canvas.
public sealed class PreviewProgramPresentation : IProgramPresentation
{
    readonly global::LtcPlayer.Avalonia.PreviewHost host;
    public PreviewProgramPresentation(global::LtcPlayer.Avalonia.PreviewHost host)=>this.host=host;
    public void Present(VideoFrame frame)=>host.Present(frame);
    public void Refresh()=>host.RefreshCover();
    public void Dispose(){} // Canvas lifetime belongs to the host, not this source.
}
