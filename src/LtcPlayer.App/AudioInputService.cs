using System.Runtime.InteropServices;
namespace LtcPlayer.App;
[StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
public struct AudioDevice
{
    [MarshalAs(UnmanagedType.ByValTStr,SizeConst=512)] public string Id;
    [MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)] public string Name;
    public override readonly string ToString()=>Name;
}
[StructLayout(LayoutKind.Sequential)]
public struct TcFrame {public int Hours,Minutes,Seconds,Frame,Drop,Reverse; public long Start,End;}
internal static class AudioNative
{
    const string Dll="AudioNative.dll";
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int audio_devices([Out] AudioDevice[] devices,int capacity,out int count);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl,CharSet=CharSet.Unicode)] internal static extern int audio_open(string id,int channel,out nint handle,out int rate,out int channels);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int audio_poll(nint handle,[Out] float[] samples,int capacity,out int count,out ulong position,out ulong qpc,out int flags);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern void audio_close(nint handle);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern nint tc_create(int rate,double fps);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern void tc_free(nint handle);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern void tc_feed(nint handle,float[] samples,int count,long position);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int tc_read(nint handle,out TcFrame frame);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int tc_generate(double fps,int drop,int h,int m,int s,int f,int frames,[Out] float[] samples,int capacity);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] internal static extern int audio_convert_test(byte[] data,int byteCount,int bits,int floating,int channels,int channel,int count,[Out] float[] mono);
    internal static void Check(int hr){if(hr<0) throw new IOException($"WASAPI失敗: 0x{hr:X8}");}
}
public sealed class LtcDecoder : IDisposable
{
    private nint handle;
    public LtcDecoder(int rate,double fps) {handle=AudioNative.tc_create(rate,fps); if(handle==0) throw new OutOfMemoryException();}
    public IEnumerable<TcFrame> Feed(float[] samples,int count,long position)
    {
        if(handle==0 || count<0 || count>samples.Length) throw new ArgumentException();
        AudioNative.tc_feed(handle,samples,count,position);
        while(AudioNative.tc_read(handle,out var frame)!=0) yield return frame;
    }
    public void Dispose(){if(handle!=0){AudioNative.tc_free(handle); handle=0;}}
}
public sealed class AudioInputService
{
    public static AudioDevice[] Devices()
    {
        var ds=new AudioDevice[64]; AudioNative.Check(AudioNative.audio_devices(ds,ds.Length,out int count));
        if(count>ds.Length) throw new IOException("入力デバイス数が上限を超えています。"); return ds.Take(count).ToArray();
    }
    public Task RunAsync(string id,int channel,TimecodeRate mode,MasterClock clock,Action<float,string> status,Action<TcFrame,double>? received,CancellationToken token)=>Task.Run(()=>
    {
        var data=new float[65536];
        while(!token.IsCancellationRequested)
        {
            nint capture=0;
            try
            {
                AudioNative.Check(AudioNative.audio_open(id,channel,out capture,out int rate,out int channels));
                using var decoder=new LtcDecoder(rate,mode.Fps); status(0,$"接続: {rate}Hz / {channels}ch"); bool firstPacket=true;
                while(!token.IsCancellationRequested)
                {
                    AudioNative.Check(AudioNative.audio_poll(capture,data,data.Length,out int count,out ulong position,out ulong qpc,out int flags));
                    if(count==0){Thread.Sleep(4); continue;}
                    if((flags&1)!=0 && !firstPacket) throw new IOException("音声データ不連続を検出。再接続します。");
                    firstPacket=false;
                    if((flags&4)!=0) throw new IOException("音声タイムスタンプが不確実です。");
                    float peak=0; for(int i=0;i<count;++i) peak=Math.Max(peak,Math.Abs(data[i])); status(peak,"入力中");
                    foreach(var tc in decoder.Feed(data,count,(long)position))
                    {
                        double end=qpc/10000000.0+(tc.End-(double)position+1)/rate;
                        clock.Accept(tc,end); received?.Invoke(tc,end);
                    }
                }
            }
            catch(Exception e) {clock.Fault(); status(0,e.Message+" / 同じ入力IDの復帰待ち");}
            finally {if(capture!=0) AudioNative.audio_close(capture);}
            if(!token.IsCancellationRequested) token.WaitHandle.WaitOne(1000);
        }
    },CancellationToken.None);
}
