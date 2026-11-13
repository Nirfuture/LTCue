using System.Runtime.InteropServices;
using System.Text;
namespace LtcPlayer.App;

[StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
public struct OutputAudioDevice
{
    [MarshalAs(UnmanagedType.ByValTStr,SizeConst=512)]public string Id;
    [MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)]public string Name;
    public int IsDefault;
    public override readonly string ToString()=>Name;
}

[StructLayout(LayoutKind.Sequential)]
public struct AudioDeviceSnapshot
{
    public ulong Written,Consumed,ClockPosition,ClockFrequency,ClockQpc;
    public uint Padding,Capacity;
    public long Latency100Ns;
    public int Running;
    public float RateRatio,Peak;
    public int ClockStatus;
    public readonly double DeviceSeconds=>ClockFrequency==0?0:ClockPosition/(double)ClockFrequency;
    public readonly double QpcSeconds=>ClockQpc/10000000.0;
    public readonly double QueuedSeconds=>Padding/48000.0;
    public readonly double ConsumedSeconds=>Consumed/48000.0;
}

public sealed record AudioPcmBlock(Guid CueId,int Generation,double Pts,float[] Samples)
{
    public int Frames=>Samples.Length/2;
}
public readonly record struct AudioBlockWrite(int Frames,bool Stale);

// Synchronous native owner. All stream calls, including Dispose, must use one worker thread.
// Playback/device policy lives outside this sink; it never selects cues or opens a fallback device.
public sealed class AudioOutputSink : IDisposable
{
    nint handle;
    readonly int owner=Environment.CurrentManagedThreadId;
    bool accepting;
    Guid cue;
    int generation;
    double origin;
    public string DeviceId{get;}
    public int StaleBlocks{get;private set;}
    public static int ActiveStreams=>N.pa_active();
    public AudioOutputSink(string deviceId)
    {
        Check(N.pa_open_device(deviceId,out handle));
        try{var id=new StringBuilder(512);Check(N.pa_device_id(handle,id,id.Capacity));DeviceId=id.ToString();}
        catch{N.pa_close(handle);handle=0;throw;}
    }
    public static OutputAudioDevice[] Devices()
    {
        var data=new OutputAudioDevice[128];Check(N.pa_devices(data,data.Length,out int count));
        if(count<0||count>data.Length)throw new IOException("音声出力デバイス数が上限を超えています。");
        return data.Take(count).ToArray();
    }
    void Access()
    {
        if(owner!=Environment.CurrentManagedThreadId)throw new InvalidOperationException("Audio sink thread mismatch");
        ObjectDisposedException.ThrowIf(handle==0,this);
    }
    public AudioDeviceSnapshot Snapshot(){Access();Check(N.pa_clock_snapshot(handle,out var snapshot));return snapshot;}
    public double PlayedPts{get{var s=Snapshot();return origin+s.ConsumedSeconds;}}
    public void BeginEpoch(Guid cueId,int newGeneration,double sourcePts)
    {
        Access();if(cueId==Guid.Empty||!double.IsFinite(sourcePts)||sourcePts<0)throw new ArgumentOutOfRangeException(nameof(sourcePts));
        Check(N.pa_reset(handle));cue=cueId;generation=newGeneration;origin=sourcePts;accepting=true;
    }
    public void Suspend(){Access();Check(N.pa_reset(handle));accepting=false;}
    public AudioBlockWrite Write(AudioPcmBlock block,int offsetFrames=0,float gain=1,int? maximumFrames=null)
    {
        Access();
        if(!accepting||block.CueId!=cue||block.Generation!=generation){StaleBlocks++;return new(0,true);}
        if(block.Samples.Length%2!=0||offsetFrames<0||offsetFrames>block.Frames||!double.IsFinite(block.Pts))throw new ArgumentException("Invalid PCM block");
        var s=Snapshot();double tail=origin+s.Written/48000.0;
        if(Math.Abs(block.Pts+offsetFrames/48000.0-tail)>2.0/48000)throw new InvalidDataException("Non-contiguous PCM PTS");
        int count=Math.Min(block.Frames-offsetFrames,maximumFrames??int.MaxValue);if(count<0)throw new ArgumentOutOfRangeException(nameof(maximumFrames));
        Check(N.pa_write(handle,block.Samples,offsetFrames,count,gain,out int written));return new(written,false);
    }
    public void SetRunning(bool running){Access();Check(N.pa_run(handle,running?1:0));}
    public void SetRateRatio(float ratio){Access();Check(N.pa_rate_ratio(handle,ratio));}
    public void SetStreamGain(float gain){Access();Check(N.pa_stream_gain(handle,gain));}
    public void Dispose(){if(handle==0)return;Access();N.pa_close(handle);handle=0;}
    static void Check(int hr){if(hr<0)throw new IOException($"WASAPI audio output failure: 0x{hr:X8}");}
    static class N
    {
        const string D="PlayerNative.dll";
        [DllImport(D,CallingConvention=CallingConvention.Cdecl,CharSet=CharSet.Unicode)]internal static extern int pa_open_device(string id,out nint handle);
        [DllImport(D,CallingConvention=CallingConvention.Cdecl,CharSet=CharSet.Unicode)]internal static extern int pa_device_id(nint handle,StringBuilder id,int capacity);
        [DllImport(D,CallingConvention=CallingConvention.Cdecl,CharSet=CharSet.Unicode)]internal static extern int pa_devices([Out]OutputAudioDevice[] devices,int capacity,out int count);
        [DllImport(D,CallingConvention=CallingConvention.Cdecl)]internal static extern int pa_clock_snapshot(nint handle,out AudioDeviceSnapshot snapshot);
        [DllImport(D,CallingConvention=CallingConvention.Cdecl)]internal static extern int pa_reset(nint handle);
        [DllImport(D,CallingConvention=CallingConvention.Cdecl)]internal static extern int pa_rate_ratio(nint handle,float ratio);
        [DllImport(D,CallingConvention=CallingConvention.Cdecl)]internal static extern int pa_stream_gain(nint handle,float gain);
        [DllImport(D,CallingConvention=CallingConvention.Cdecl)]internal static extern int pa_write(nint handle,float[] samples,int offsetFrames,int frames,float gain,out int written);
        [DllImport(D,CallingConvention=CallingConvention.Cdecl)]internal static extern int pa_run(nint handle,int running);
        [DllImport(D,CallingConvention=CallingConvention.Cdecl)]internal static extern void pa_close(nint handle);
        [DllImport(D,CallingConvention=CallingConvention.Cdecl)]internal static extern int pa_active();
    }
}
