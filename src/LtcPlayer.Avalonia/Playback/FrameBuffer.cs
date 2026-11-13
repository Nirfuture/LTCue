using System.Threading.Channels;
namespace LtcPlayer.App;
public sealed class FrameBuffer : IAsyncDisposable
{
    private readonly CancellationTokenSource cancel;
    private readonly Channel<VideoFrame> frames=Channel.CreateBounded<VideoFrame>(8);
    private readonly Task worker;
    public bool Ended=>frames.Reader.Completion.IsCompletedSuccessfully;
    public FrameBuffer(string path,int width,int height,double seek,CancellationToken token,MediaDecoder? decoder=null)
    {
        cancel=CancellationTokenSource.CreateLinkedTokenSource(token);
        worker=Task.Run(async ()=>
        {
            try {await foreach(var f in (decoder??new MediaDecoder()).DecodeAsync(path,width,height,cancel.Token,seek)) await frames.Writer.WriteAsync(f,cancel.Token); frames.Writer.TryComplete();}
            catch(Exception e){frames.Writer.TryComplete(e);}
        });
    }
    public ValueTask<bool> WaitForDataAsync(CancellationToken token)=>frames.Reader.WaitToReadAsync(token);
    public bool TryRead(out VideoFrame? frame)
    {
        if(frames.Reader.Completion.IsFaulted) frames.Reader.Completion.GetAwaiter().GetResult();
        return frames.Reader.TryRead(out frame);
    }
    public async ValueTask DisposeAsync(){cancel.Cancel(); await worker; cancel.Dispose();}
}
