namespace LtcPlayer.App;

// Both manual and LTC sources publish into the host's persistent full-size canvas.
public sealed class PreviewProgramPresentation : IProgramPresentation
{
    readonly global::LtcPlayer.Avalonia.PreviewHost host;
    readonly Func<bool> mayPresent;
    public PreviewProgramPresentation(global::LtcPlayer.Avalonia.PreviewHost host,Func<bool>? mayPresent=null){this.host=host;this.mayPresent=mayPresent??(()=>true);}
    public void Present(VideoFrame frame){if(mayPresent())host.Present(frame);}
    public void Refresh()=>host.RefreshCover("hold");
    public void Dispose(){} // Canvas lifetime belongs to the host, not this source.
}
