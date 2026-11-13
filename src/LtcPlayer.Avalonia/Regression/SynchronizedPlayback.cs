using Dispatcher = LtcPlayer.Avalonia.UiDispatcher;
namespace LtcPlayer.App;
public record SyncTelemetry(SyncState State,string Ltc,double Target,double? FramePts,int Presented,int Repeats,int Skipped,int Seeks)
{
    public double HostSeconds {get;init;}=MasterClock.Timestamp();
}
public sealed class SynchronizedPlayback
{
    private readonly Dispatcher dispatcher;
    public SynchronizedPlayback(Dispatcher dispatcher)=>this.dispatcher=dispatcher;
    public async Task RunAsync(string path,OutputDisplay display,MasterClock clock,double start,double offsetSeconds,Action<SyncTelemetry> telemetry,CancellationToken token)
    {
        var decoder=new MediaDecoder(reuseAnalysis:true,gpuScaling:true);
        await decoder.InspectAsync(path,token);
        VideoRenderer? renderer=null; FrameBuffer? buffer=null;
        VideoFrame? current=null,next=null; var black=new byte[checked(display.Width*display.Height*4)];
        int generation=-1,presented=0,repeats=0,skipped=0,seeks=0; double lastTarget=double.NaN,previousPts=double.NaN,lastInterval=0.04;
        byte[]? displayed=null;
        try
        {
            await dispatcher.InvokeAsync(()=>renderer=new VideoRenderer(display));
            while(!token.IsCancellationRequested)
            {
                var snapshot=clock.Read(); double target=snapshot.Seconds-start+offsetSeconds;
                byte[]? pixels=null;VideoFrame? texture=null;
                if(snapshot.State==SyncState.WAITING || target<0) pixels=black;
                else if(snapshot.State is not (SyncState.LOST or SyncState.ERROR))
                {
                    if(buffer==null || snapshot.Generation!=generation || (!double.IsNaN(lastTarget) && (target<lastTarget-0.08 || target-lastTarget>0.5)))
                    {
                        if(buffer!=null) await buffer.DisposeAsync();
                        buffer=new FrameBuffer(path,display.Width,display.Height,Math.Max(0,target-0.2),token,decoder);
                        current=next=null; generation=snapshot.Generation; ++seeks; previousPts=double.NaN;
                        // Hold the last output while the new decode window is being prepared.
                    }
                    lastTarget=target; int advanced=0;
                    if(next==null) buffer.TryRead(out next);
                    while(next!=null && next.Seconds<=target+0.000001)
                    {
                        if(current!=null) {lastInterval=Math.Clamp(next.Seconds-current.Seconds,0.001,1);}
                        current=next; next=null; ++advanced; buffer.TryRead(out next);
                    }
                    skipped+=Math.Max(0,advanced-1);
                    if(current!=null)
                    {
                        pixels=buffer.Ended && next==null && target>=current.Seconds+lastInterval?black:current.Pixels;
                        if(pixels!=black)texture=current;
                        if(previousPts==current.Seconds && pixels!=black) ++repeats;
                        previousPts=current.Seconds;
                    }
                    else if(buffer.Ended) pixels=black;
                }
                if(pixels!=null)
                {
                    await dispatcher.InvokeAsync(()=>{token.ThrowIfCancellationRequested(); if(texture!=null)renderer!.Present(texture);else renderer!.Present(pixels);});
                    ++presented; displayed=pixels;
                }
                telemetry(new(snapshot.State,snapshot.Label,target,displayed==black?null:current?.Seconds,presented,repeats,skipped,seeks));
                await Task.Delay(8,token).ConfigureAwait(false);
            }
        }
        finally {if(buffer!=null) await buffer.DisposeAsync(); if(renderer!=null) await dispatcher.InvokeAsync(renderer.Dispose);}
    }
}

