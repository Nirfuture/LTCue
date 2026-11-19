using System.Threading.Channels;
namespace LtcPlayer.App;
public sealed class FrameBuffer : IAsyncDisposable
{
    private readonly CancellationTokenSource cancel;
    private readonly Channel<VideoFrame> frames=Channel.CreateBounded<VideoFrame>(8);
    private readonly Task worker;
#if VERIFICATION
    internal static int ActiveWorkers;
    internal int Queued=>frames.Reader.Count;
#endif
    Exception? error;
    public Exception? Error=>Volatile.Read(ref error);
    public bool Ended=>frames.Reader.Completion.IsCompletedSuccessfully;
    public FrameBuffer(string path,int width,int height,double seek,CancellationToken token,MediaDecoder? decoder=null)
    {
        cancel=CancellationTokenSource.CreateLinkedTokenSource(token);
        worker=Task.Run(async ()=>
        {
#if VERIFICATION
            Interlocked.Increment(ref ActiveWorkers);
#endif
            try {await foreach(var f in (decoder??new MediaDecoder()).DecodeAsync(path,width,height,cancel.Token,seek)) await frames.Writer.WriteAsync(f,cancel.Token); frames.Writer.TryComplete();}
            catch(OperationCanceledException)when(cancel.IsCancellationRequested){frames.Writer.TryComplete();}
            catch(Exception e){Volatile.Write(ref error,e);frames.Writer.TryComplete(e);}
#if VERIFICATION
            finally{Interlocked.Decrement(ref ActiveWorkers);}
#endif
        });
    }
    public ValueTask<bool> WaitForDataAsync(CancellationToken token)=>frames.Reader.WaitToReadAsync(token);
    public bool TryRead(out VideoFrame? frame)
    {
        if(Error is {} failure)throw failure;
        if(frames.Reader.Completion.IsFaulted) frames.Reader.Completion.GetAwaiter().GetResult();
        return frames.Reader.TryRead(out frame);
    }
    public async ValueTask DisposeAsync(){cancel.Cancel(); await worker; cancel.Dispose();}
}
