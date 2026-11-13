using System.Diagnostics;
using Dispatcher = LtcPlayer.Avalonia.UiDispatcher;
namespace LtcPlayer.App;
public record PlaybackResult(int Frames, double FirstPts, double LastPts, double ElapsedSeconds, int LateFrames, OutputBounds Bounds);
public sealed class PlaybackService
{
    private readonly MediaDecoder decoder = new();
    private readonly Dispatcher dispatcher;
    public PlaybackService(Dispatcher dispatcher) => this.dispatcher = dispatcher;
    public async Task<PlaybackResult> PlayAsync(string path, OutputDisplay display, bool verify, CancellationToken token)
    {
        await decoder.InspectAsync(path, token);
        VideoRenderer? renderer = null;
        var count = 0; var late = 0; double first = 0, last = 0;
        Stopwatch clock = new(); VideoFrame? final = null; OutputBounds bounds = default;
        try
        {
            await dispatcher.InvokeAsync(() => { renderer = new VideoRenderer(display); bounds = renderer.Bounds(); });
            await foreach (var frame in decoder.DecodeAsync(path, display.Width, display.Height, token).ConfigureAwait(false))
            {
                token.ThrowIfCancellationRequested();
                if (count == 0) { first = frame.Seconds; clock.Start(); }
                else if (frame.Seconds < last) throw new InvalidDataException("PTSが逆行しました。");
                var target = frame.Seconds - first; var wait = target - clock.Elapsed.TotalSeconds;
                if (wait > 0) await Task.Delay(TimeSpan.FromSeconds(wait), token).ConfigureAwait(false);
                if (clock.Elapsed.TotalSeconds - target > 0.05) ++late;
                await dispatcher.InvokeAsync(() => { token.ThrowIfCancellationRequested(); renderer!.Present(frame); });
                ++count; last = frame.Seconds; final = frame;
            }
            if (count == 0) throw new InvalidDataException("復号可能な映像フレームがありません。");
            if (verify) await dispatcher.InvokeAsync(() => {if(final!.TextureFormat==0)renderer!.Verify(final.Pixels);else _=renderer!.Readback();});
            return new PlaybackResult(count, first, last, clock.Elapsed.TotalSeconds, late, bounds);
        }
        finally { if (renderer != null) await dispatcher.InvokeAsync(renderer.Dispose); }
    }
}

