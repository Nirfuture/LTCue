using System.Runtime.InteropServices;
namespace LtcPlayer.App;

// One GPU composition, with independently attached preview and external presentation.
public sealed class RenderSession : IDisposable, ICoverRenderer
{
    nint handle;
    readonly int thread = Environment.CurrentManagedThreadId;
    public Guid SessionId { get; } = Guid.NewGuid();
    public long FrameId { get; private set; }
    public long CompositionId { get; private set; }
    public double? FramePts { get; private set; }
    public int Width { get; }
    public int Height { get; }
    public bool OutputAttached { get; private set; }
    public bool OutputDisconnected { get; private set; }
    public OutputPresentationMode OutputMode { get; private set; }
    bool previewAttached;
    public int PreviewError { get; private set; }
    public int SharedDeviceError { get; private set; }
#if VERIFICATION
    internal Func<int,int>? PreviewResultForTest { get; set; }
    internal Func<int>? DeviceReasonForTest { get; set; }
    internal Func<int,int>? ExternalErrorForTest { get; set; }
    internal void SimulateDisplayChangeForTest(){Access();Check(N.canvas_display_change_test(handle));}
#endif
    public RenderSession(int width, int height)
    {
        Width = width; Height = height;
        Check(N.canvas_create(width, height, out handle));
    }
    void Access()
    {
        if (Environment.CurrentManagedThreadId != thread) throw new InvalidOperationException("Render session thread mismatch");
        ObjectDisposedException.ThrowIf(handle == 0, this);
    }
    public void AttachPreview(nint window) { Access(); Check(N.monitor_attach(handle, window)); previewAttached = true; PreviewError=0; }
    public void AttachOutput(OutputDisplay display, OutputPresentationMode mode, bool probe)
    {
        Access(); CheckSharedDevice(); OutputPresentation.Validate(mode);
        Check(N.canvas_attach(handle, ref display, mode == OutputPresentationMode.Compatibility ? 1 : 0, probe ? 1 : 0));
        int actual=N.output_presentation_mode(handle);
        if(actual!=(int)mode){N.canvas_detach(handle);throw new IOException("実際の出力方式が設定と一致しません。");}
        OutputMode=(OutputPresentationMode)actual;OutputAttached=true;OutputDisconnected=false;
    }
    public void DetachOutput()
    {
        Access(); try { Check(N.canvas_detach(handle)); } finally { OutputAttached = false; }
    }
    public bool PollStopRequest() { Access(); return OutputAttached && N.output_stop_request(handle, 1) != 0; }
    public void Present(VideoFrame frame)
    {
        Access(); CheckSharedDevice();
        if (frame.TextureFormat == 0) Check(N.output_frame(handle, frame.Pixels, frame.Pixels.Length));
        else Check(N.output_hap(handle, frame.Pixels, frame.Pixels.Length, frame.Width, frame.Height, frame.TextureFormat));
        FrameId++; CompositionId++; FramePts = frame.Seconds;
        UpdatePreview();
    }
    public void SetCover(CoverImage? image) { Access();CheckSharedDevice();try{CoverNative.Image(handle,image);}catch(IOException){CheckDevice(0);throw;} }
    public void SetCoverMix(float amount) { Access();CheckSharedDevice();try{CoverNative.Mix(handle,amount);}catch(IOException){CheckDevice(0);throw;} }
    public void Redraw() { Access(); CheckSharedDevice(); Check(N.output_redraw(handle)); CompositionId++; UpdatePreview(); }
    public void UpdatePreview()
    {
        Access();CheckSharedDevice();if(!previewAttached||PreviewError<0)return;
        int result=N.monitor_update(handle,0);
#if VERIFICATION
        result=PreviewResultForTest?.Invoke(result)??result;
#endif
        if(result<0){CheckDevice(result);PreviewError=result;}
    }
    void CheckSharedDevice(){if(SharedDeviceError<0)throw new SharedGraphicsException(SharedDeviceError);}
    void CheckDevice(int error)
    {
        int reason=handle!=0?N.canvas_device_error(handle):0;
#if VERIFICATION
        reason=DeviceReasonForTest?.Invoke()??reason;
#endif
        if(reason<0||IsDeviceFailure(error)){SharedDeviceError=reason<0?reason:error;throw new SharedGraphicsException(SharedDeviceError);}
    }
    internal static bool IsDeviceFailure(int error)=>error is unchecked((int)0x887A0005) or unchecked((int)0x887A0006) or unchecked((int)0x887A0007) or unchecked((int)0x887A0020);

    public void RefreshOutput()
    {
        Access(); if(!OutputAttached)return;
        int error=N.canvas_output_error(handle);
#if VERIFICATION
        error=ExternalErrorForTest?.Invoke(error)??error;
#endif
        if(error==unchecked((int)0x8007048F)){OutputDisconnected=true;return;}
        if(error<0){CheckDevice(error);throw new IOException($"外部出力エラー: 0x{error:X8}");}
        int hr=N.canvas_output_refresh(handle);
        if(hr==unchecked((int)0x887A000A))return; // DXGI_ERROR_WAS_STILL_DRAWING: keep latest image.
        if(hr==unchecked((int)0x8007048F)){OutputDisconnected=true;return;}
        Check(hr);
    }
    public ProgramMonitorStatus PreviewStatus()
    {
        Access(); int error = N.monitor_stats(handle, out int w, out int h, out ulong frames);
        int diagnostics = N.monitor_diagnostics(handle, out ulong rendered, out ulong retries, out ulong occluded, out ulong busy, out int result);
        return new(w, h, rendered, frames, retries, occluded, busy, result, SharedDeviceError<0?SharedDeviceError:PreviewError<0?PreviewError:error != 0 ? error : diagnostics);
    }
    public byte[] Readback()
    {
        Access(); var data = new byte[checked(Width * Height * 4)]; Check(N.output_readback(handle, data, data.Length)); return data;
    }
    public byte[] PreviewReadback()
    {
        var s = PreviewStatus(); var data = new byte[checked(s.Width * s.Height * 4)]; Check(N.monitor_readback(handle, data, data.Length)); return data;
    }
    public void Dispose()
    {
        if (handle == 0) return;
        Access();
        try { N.canvas_detach(handle); } finally { N.output_destroy(handle); handle = 0; }
    }
    void Check(int hr) { if(hr>=0)return;CheckDevice(hr);throw new IOException($"GPU canvas failure: 0x{hr:X8}"); }
    static class N
    {
        [DllImport("PlayerNative.dll", CallingConvention=CallingConvention.Cdecl)] internal static extern int canvas_output_refresh(nint handle);
        [DllImport("PlayerNative.dll", CallingConvention=CallingConvention.Cdecl)] internal static extern int canvas_output_error(nint handle);
        [DllImport("PlayerNative.dll", CallingConvention=CallingConvention.Cdecl)] internal static extern int output_presentation_mode(nint handle);
        [DllImport("PlayerNative.dll",CallingConvention=CallingConvention.Cdecl)] internal static extern int canvas_device_error(nint handle);
        [DllImport("PlayerNative.dll",CallingConvention=CallingConvention.Cdecl)] internal static extern int canvas_display_change_test(nint handle);
        const string D = "PlayerNative.dll";
        [DllImport(D, CallingConvention=CallingConvention.Cdecl)] internal static extern int canvas_create(int width,int height,out nint handle);
        [DllImport(D, CallingConvention=CallingConvention.Cdecl)] internal static extern int canvas_attach(nint handle,ref OutputDisplay display,int compatibility,int probe);
        [DllImport(D, CallingConvention=CallingConvention.Cdecl)] internal static extern int canvas_detach(nint handle);
        [DllImport(D, CallingConvention=CallingConvention.Cdecl)] internal static extern int output_stop_request(nint handle,int enabled);
        [DllImport(D, CallingConvention=CallingConvention.Cdecl)] internal static extern int output_frame(nint handle,byte[] data,int length);
        [DllImport(D, CallingConvention=CallingConvention.Cdecl)] internal static extern int output_hap(nint handle,byte[] data,int length,int width,int height,int kind);
        [DllImport(D, CallingConvention=CallingConvention.Cdecl)] internal static extern int output_redraw(nint handle);
        [DllImport(D, CallingConvention=CallingConvention.Cdecl)] internal static extern void output_destroy(nint handle);
        [DllImport(D, CallingConvention=CallingConvention.Cdecl)] internal static extern int output_readback(nint handle,[Out]byte[] data,int length);
        [DllImport(D, CallingConvention=CallingConvention.Cdecl)] internal static extern int monitor_attach(nint handle,nint window);
        [DllImport(D, CallingConvention=CallingConvention.Cdecl)] internal static extern int monitor_update(nint handle,int force);
        [DllImport(D, CallingConvention=CallingConvention.Cdecl)] internal static extern int monitor_stats(nint handle,out int width,out int height,out ulong frames);
        [DllImport(D, CallingConvention=CallingConvention.Cdecl)] internal static extern int monitor_diagnostics(nint handle,out ulong rendered,out ulong retries,out ulong occluded,out ulong busy,out int result);
        [DllImport(D, CallingConvention=CallingConvention.Cdecl)] internal static extern int monitor_readback(nint handle,[Out]byte[] data,int length);
    }
}

public sealed class SharedGraphicsException(int code) : IOException($"共通GPUの描画を継続できません（0x{code:X8}）。アプリを再起動してください。")
{
 public int ErrorCode{get;}=code;
}
