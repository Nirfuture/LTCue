using System.Runtime.InteropServices;
namespace LtcPlayer.App;
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct OutputDisplay
{
    public int X, Y, Width, Height, Primary;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
    public override readonly string ToString() => $"{Name}  {Width}×{Height} ({X}, {Y})" + (Primary != 0 ? " メイン" : "");
}
[StructLayout(LayoutKind.Sequential)]
public struct OutputBounds { public int Left, Top, Right, Bottom; }
public record ProgramMonitorStatus(int Width,int Height,ulong Rendered,ulong Presented,ulong PresentRetries,ulong Occluded,ulong GpuBusyPolls,int LastPresentResult,int Error);
public sealed class VideoRenderer : IDisposable,ICoverRenderer
{
    private nint handle;
    public void SetCover(CoverImage? image){ThreadCheck();CoverNative.Image(handle,image);}
    public void SetCoverMix(float amount){ThreadCheck();CoverNative.Mix(handle,amount);}
    public void Redraw(){ThreadCheck();CoverNative.Redraw(handle);UpdateMonitor();}
    private global::LtcPlayer.Avalonia.RenderCallTrace? trace;
    private int Call(string operation,Func<int> action)=>trace!=null?trace.Call(operation,action):action();
    private readonly int thread = Environment.CurrentManagedThreadId;
    private readonly int width,height;
    private bool monitoring,confirmation;
    public void AttachMonitor(nint window){ThreadCheck();Check(Call("monitor_attach",()=>Native.monitor_attach(handle,window)));monitoring=true;}
    public void EnableStopConfirmation(){confirmation=true;Native.output_stop_request(handle,1);}
    public bool PollStopRequest()=>confirmation&&Native.output_stop_request(handle,1)!=0;
    internal void TestNativeStopRequest(bool escape){Check(Native.output_stop_test(handle,escape?1:0));}
    public (int Width,int Height,ulong Frames,int Error) MonitorStats(){int error=Native.monitor_stats(handle,out int w,out int h,out ulong frames);return(w,h,frames,error);}
    public ProgramMonitorStatus MonitorStatus(){ThreadCheck();var s=MonitorStats();int error=Native.monitor_diagnostics(handle,out ulong rendered,out ulong retries,out ulong occluded,out ulong busy,out int last);return new(s.Width,s.Height,rendered,s.Frames,retries,occluded,busy,last,error!=0?error:s.Error);}
    public void UpdateMonitor(bool force=false){ThreadCheck();if(monitoring)_=Call("monitor_update",()=>Native.monitor_update(handle,force?1:0));}
    public byte[] MonitorReadback(){var s=MonitorStats();var data=new byte[s.Width*s.Height*4];Check(Native.monitor_readback(handle,data,data.Length));return data;}
    public static OutputDisplay[] Displays()
    {
        var data = new OutputDisplay[32]; int count = Native.output_displays(data, data.Length);
        if (count <= 0 || count > data.Length) throw new IOException("出力ディスプレイ一覧を取得できません。");
        return data.Take(count).ToArray();
    }
    public OutputPresentationMode OutputMode{get;private set;}
    public VideoRenderer(OutputDisplay display,OutputPresentationMode outputMode=OutputPresentationMode.Standard) {
        OutputPresentation.Validate(outputMode);width=display.Width;height=display.Height;
        if(global::LtcPlayer.Avalonia.Program.Option("--diagnostic-render") is {} folder)trace=new(folder);
        bool composited=outputMode==OutputPresentationMode.Compatibility;
        try{Check(Call(composited?"output_create:BitBlt":"output_create:Flip",()=>composited?Native.output_create_composited(ref display,out handle,global::LtcPlayer.Avalonia.Program.VerificationOutput?1:0):global::LtcPlayer.Avalonia.Program.VerificationOutput?Native.output_create_probe(ref display,out handle):Native.output_create(ref display,out handle)));}
        catch{trace?.Dispose();throw;}
        int actual=Native.output_presentation_mode(handle);
        if(actual is not (0 or 1)){Dispose();throw new IOException("実際の表示方式を取得できません。");}
        OutputMode=(OutputPresentationMode)actual;
        if(OutputMode!=outputMode){Dispose();throw new IOException("指定した表示方式を開始できません。");}
    }
    private void ThreadCheck() { if (thread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Renderer thread mismatch"); }
    public void Present(byte[] pixels) { ThreadCheck(); Check(Call("output_frame",()=>Native.output_frame(handle,pixels,pixels.Length)));UpdateMonitor(); }
    public void Present(VideoFrame frame)
    {
        ThreadCheck();if(frame.TextureFormat==0)Present(frame.Pixels);else {Check(Call("output_hap",()=>Native.output_hap(handle,frame.Pixels,frame.Pixels.Length,frame.Width,frame.Height,frame.TextureFormat)));UpdateMonitor();}
    }
    public byte[] Readback(){ThreadCheck();var pixels=new byte[checked(width*height*4)];Check(Native.output_readback(handle,pixels,pixels.Length));return pixels;}
    public void Verify(byte[] pixels) { ThreadCheck(); Check(Native.output_check(handle, pixels, pixels.Length)); }
    public OutputBounds Bounds() { ThreadCheck(); Check(Native.output_bounds(handle, out var bounds)); return bounds; }
    private static void Check(int code)
    {
        if (code == unchecked((int)0x800704C7)) throw new OperationCanceledException("出力ウィンドウを閉じました。");
        if (code < 0) throw new IOException($"D3D11/DXGI失敗: 0x{code:X8}");
    }
    public void Dispose() { ThreadCheck(); if (handle != 0) { try{Call("output_destroy",()=>{Native.output_destroy(handle);return 0;});}finally{handle=0;trace?.Dispose();} } }
    private static class Native
    {
        private const string Dll = "PlayerNative.dll";
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern int output_presentation_mode(nint h);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern int monitor_attach(nint h,nint window);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern int monitor_update(nint h,int force);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern int monitor_stats(nint h,out int w,out int height,out ulong frames);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern int monitor_diagnostics(nint h,out ulong rendered,out ulong retries,out ulong occluded,out ulong busy,out int presentResult);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern int monitor_readback(nint h,[Out]byte[] data,int length);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern int output_stop_request(nint h,int enable);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)]internal static extern int output_stop_test(nint h,int escape);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern int output_displays([Out] OutputDisplay[] displays, int capacity);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern int output_create_composited(ref OutputDisplay display,out nint handle,int probe);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern int output_create(ref OutputDisplay display, out nint handle);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern int output_create_probe(ref OutputDisplay display, out nint handle);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern int output_frame(nint handle, byte[] pixels, int length);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int output_hap(nint handle,byte[] blocks,int length,int width,int height,int kind);
        [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int output_readback(nint handle,[Out]byte[] pixels,int length);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern int output_check(nint handle, byte[] pixels, int length);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern int output_bounds(nint handle, out OutputBounds bounds);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] internal static extern void output_destroy(nint handle);
    }
}
