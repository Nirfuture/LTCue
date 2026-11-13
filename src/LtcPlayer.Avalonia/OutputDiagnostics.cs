using System.Threading.Channels;
using LtcPlayer.App;
namespace LtcPlayer.Avalonia;

// I/O and serialization belong to the consumer, never to playback or the UI thread.
internal sealed class OutputDiagnostics : IAsyncDisposable
{
    readonly Channel<object> queue=Channel.CreateBounded<object>(new BoundedChannelOptions(32){SingleReader=true,FullMode=BoundedChannelFullMode.DropOldest});
    readonly Task worker;
    public OutputDiagnostics(InputSettings settings,OutputDisplay display)
    {
        worker=Task.Run(async()=>{
            using var log=new SyncRunLog();
            log.Write(new{kind="configuration",settings,display,hostSeconds=MasterClock.Timestamp()});
            await foreach(var value in queue.Reader.ReadAllAsync())log.Write(value);
        });
    }
    public void Observe(ProgramObservation observation)=>queue.Writer.TryWrite(new{kind="programObservation",t=observation});
    public async ValueTask DisposeAsync()
    {
        queue.Writer.TryComplete();
        // Stop/output detach never waits indefinitely for a slow filesystem.
        try{await worker.WaitAsync(TimeSpan.FromMilliseconds(250));}catch(TimeoutException){}catch(IOException){}catch(UnauthorizedAccessException){}
    }
}
