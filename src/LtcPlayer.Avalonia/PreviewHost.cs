using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LtcPlayer.App;
using System.Runtime.InteropServices;
namespace LtcPlayer.Avalonia;

// The host owns the canvas for its entire lifetime. Output toggles never replace it.
public sealed class PreviewHost : NativeControlHost, ICoverRenderer
{
    RenderSession? canvas;
    // clipWindow is the attachment supplied by NativeControlHost; Avalonia owns it.
    // Clip that outer host so its background cannot extend beyond our viewport.
    nint window, container, clipWindow;
    bool borrowedForVerification;
    int width=1920, height=1080;
    CoverBinding coverBinding=new();
    internal CoverController? Cover { get; set; }
    public RenderSession Canvas => canvas ?? throw new InvalidOperationException("Canvas is not ready");
    public (int Width,int Height) CanvasSize => (width,height);
    public bool Ready => canvas!=null;
    public bool ProgramMode => borrowedForVerification || canvas?.OutputAttached==true;
    internal nint ProgramWindowForVerification => window;
    public int Presents { get; private set; }
    Rect? effectiveViewport;
    (int Left,int Top,int Right,int Bottom)? appliedClip;
    public PreviewHost(){
        EffectiveViewportChanged+=(_,e)=>{effectiveViewport=e.EffectiveViewport;UpdateNativeClip();};
        LayoutUpdated+=(_,_)=>UpdateNativeClip();
    }
    void UpdateNativeClip(){
        if(clipWindow==0)return;
        var full=new Rect(Bounds.Size);var clip=effectiveViewport is {} visible?full.Intersect(visible):full;
        // Native child windows do not inherit Avalonia clipping. Intersect actual
        // ancestor bounds as well: the effective viewport may extend past the
        // visible scroll area during a height-only resize.
        foreach(var ancestor in this.GetVisualAncestors()){
            if(!ancestor.ClipToBounds&&ancestor is not ScrollViewer&&ancestor is not TopLevel)continue;
            if(ancestor.TranslatePoint(default,this) is {} origin)clip=clip.Intersect(new Rect(origin,ancestor.Bounds.Size));
        }
        double scale=TopLevel.GetTopLevel(this)?.RenderScaling??1;
        var region=clip.Width<=0||clip.Height<=0?(0,0,0,0):((int)Math.Ceiling(clip.Left*scale),(int)Math.Ceiling(clip.Top*scale),(int)Math.Floor(clip.Right*scale),(int)Math.Floor(clip.Bottom*scale));
        if(appliedClip==region)return;
        nint handle=N.CreateRectRgn(region.Item1,region.Item2,region.Item3,region.Item4);
        if(handle==0)return;
        if(N.SetWindowRgn(clipWindow,handle,true)!=0)appliedClip=region;else N.DeleteObject(handle);
    }
#if VERIFICATION
    internal (int Left,int Top,int Right,int Bottom) NativeClipForTest(){
        nint region=N.CreateRectRgn(0,0,0,0);
        try{if(N.GetWindowRgn(clipWindow,region)==0)throw new IOException("Preview window has no clip region");N.GetRgnBox(region,out var box);return(box.Left,box.Top,box.Right,box.Bottom);}finally{N.DeleteObject(region);}
    }
#endif
    public void ConfigureCanvas(int newWidth,int newHeight)
    {
        using var prepared=PrepareCanvas(newWidth,newHeight);
        prepared?.Commit();
    }
    internal PreparedCanvas? PrepareCanvas(int newWidth,int newHeight)
    {
        Dispatcher.UIThread.VerifyAccess();
        if(newWidth==width&&newHeight==height)return null;
        if(canvas?.OutputAttached==true)throw new InvalidOperationException("出力中は描画サイズを変更できません。");
        return new PreparedCanvas(this,newWidth,newHeight);
    }
    // A failed preparation or settings write must leave the old canvas and image intact.
    internal sealed class PreparedCanvas : IDisposable
    {
        readonly PreviewHost host;
        readonly int newWidth,newHeight;
        nint child;
        RenderSession? candidate;
        public PreparedCanvas(PreviewHost host,int width,int height)
        {
            this.host=host;newWidth=width;newHeight=height;
            if(width<4||height<4||width>8192||height>8192)throw new ArgumentOutOfRangeException(nameof(width));
            if(host.container==0)return;
            Check(N.preview_child_create(host.container,out child));
            N.ShowWindow(child,0);
            try{candidate=new(width,height);candidate.AttachPreview(child);candidate.Present(new(new byte[checked(width*height*4)],0));}
            catch{Dispose();throw;}
        }
        public void Commit()
        {
            Dispatcher.UIThread.VerifyAccess();
            var old=host.canvas;var oldWindow=host.window;
            host.canvas=candidate;candidate=null;host.window=child;child=0;
            host.width=newWidth;host.height=newHeight;host.coverBinding=new();host.appliedClip=null;host.UpdateNativeClip();
            if(host.window!=0)N.ShowWindow(host.window,5);
            old?.Dispose();if(oldWindow!=0)N.preview_window_destroy(oldWindow);
        }
        public void Dispose(){candidate?.Dispose();candidate=null;if(child!=0){N.preview_window_destroy(child);child=0;}}
    }
    public void SetCover(CoverImage? image)=>Canvas.SetCover(image);
    public void SetCoverMix(float amount)=>Canvas.SetCoverMix(amount);
    public void Redraw(){if(Ready)Canvas.Redraw();}
    internal void RefreshCover()
    {
        if(!Ready||Canvas.SharedDeviceError<0)return;
        if(Cover!=null&&coverBinding.Apply(Cover,Canvas))Canvas.Redraw();
        Canvas.UpdatePreview();
        Canvas.RefreshOutput();
    }
    public (int Width,int Height) PixelSize
    {
        get{Dispatcher.UIThread.VerifyAccess();var s=Canvas.PreviewStatus();return(s.Width,s.Height);}
    }
    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        clipWindow=parent.Handle;
        Check(N.preview_child_create(parent.Handle,out container));
        try{CreateCanvas();appliedClip=null;UpdateNativeClip();return new PlatformHandle(container,"HWND");}
        catch{N.preview_window_destroy(container);container=0;throw;}
    }
    void CreateCanvas()
    {
        Check(N.preview_child_create(container,out window));
        try{canvas=new(width,height);canvas.AttachPreview(window);coverBinding=new();appliedClip=null;Black();UpdateNativeClip();}
        catch{canvas?.Dispose();canvas=null;N.preview_window_destroy(window);window=0;throw;}
    }
    protected override void DestroyNativeControlCore(IPlatformHandle handle)
    {
        try{canvas?.Dispose();}finally{canvas=null;N.preview_window_destroy(container);window=container=clipWindow=0;}
    }
    public void Present(VideoFrame frame)
    {
        Dispatcher.UIThread.VerifyAccess();
        if(Cover!=null)coverBinding.Apply(Cover,Canvas);
        Canvas.Present(frame);Presents++;
    }
    public void Black(){if(Ready)Present(new(new byte[checked(width*height*4)],0));}
    public byte[] Readback()=>Canvas.PreviewReadback();

    // Isolated legacy renderer tests borrow a child. Product output never uses this path.
    public nint BeginProgram()
    {
        Dispatcher.UIThread.VerifyAccess();
        if(ProgramMode)throw new InvalidOperationException("Already attached");
        canvas?.Dispose();canvas=null;N.preview_window_destroy(window);
        Check(N.preview_child_create(container,out window));borrowedForVerification=true;return window;
    }
    public void EndProgram()
    {
        if(!borrowedForVerification)return;
        N.preview_window_destroy(window);window=0;borrowedForVerification=false;CreateCanvas();
    }
    static void Check(int hr){if(hr<0)throw new IOException($"Preview HWND failure 0x{hr:X8}");}
    static class N
    {
        [StructLayout(LayoutKind.Sequential)]internal struct RectI{public int Left,Top,Right,Bottom;}
        [DllImport("gdi32.dll")]internal static extern nint CreateRectRgn(int left,int top,int right,int bottom);
        [DllImport("gdi32.dll")]internal static extern bool DeleteObject(nint handle);
        [DllImport("gdi32.dll")]internal static extern int GetRgnBox(nint region,out RectI rect);
        [DllImport("user32.dll")]internal static extern int SetWindowRgn(nint window,nint region,bool redraw);
        [DllImport("user32.dll")]internal static extern int GetWindowRgn(nint window,nint region);
        [DllImport("user32.dll")]internal static extern bool ShowWindow(nint window,int command);
        const string D="PlayerNative.dll";
        [DllImport(D,CallingConvention=CallingConvention.Cdecl)]internal static extern int preview_child_create(nint parent,out nint child);
        [DllImport(D,CallingConvention=CallingConvention.Cdecl)]internal static extern void preview_window_destroy(nint child);
    }
}
