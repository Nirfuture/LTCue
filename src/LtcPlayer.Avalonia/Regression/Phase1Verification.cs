using System.Text.Json;
using Window = LtcPlayer.Avalonia.RegressionWindow;
using Application = LtcPlayer.Avalonia.RegressionApplication;
namespace LtcPlayer.App;
public static class Phase1Verification
{
    public static async Task RunAsync(Window window, string media, string folder)
    {
        Directory.CreateDirectory(folder);
        var results = new List<object>(); bool failed = false;
        async Task Test(string name, Func<Task<object>> action)
        {
            try { results.Add(new { test = name, status = "PASS", detail = await action() }); }
            catch (Exception e) { failed = true; results.Add(new { test = name, status = "FAIL", detail = e.ToString() }); }
            await File.WriteAllTextAsync(Path.Combine(folder, "results.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
        }
        try
        {
            var displays = VideoRenderer.Displays();
            await Test("1080p H264 metadata", async () => {
                var info = await new MediaDecoder().InspectAsync(media, CancellationToken.None);
                if (info.Width != 1920 || info.Height != 1080) throw new Exception("Expected 1920x1080 fixture");
                return info;
            });
            foreach (var display in displays)
                await Test("full screen / " + display.Name, async () => {
                    using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    var r = await new PlaybackService(window.Dispatcher).PlayAsync(media, display, true, limit.Token);
                    if (r.Frames != 50 || Math.Abs(r.LastPts - r.FirstPts - 1.96) > 0.001) throw new Exception("Frame count/PTS mismatch");
                    if (r.Bounds.Left != display.X || r.Bounds.Top != display.Y || r.Bounds.Right - r.Bounds.Left != display.Width || r.Bounds.Bottom - r.Bounds.Top != display.Height) throw new Exception("Output bounds mismatch");
                    return new { display, result = r, uploadReadback = "all pixels match", present = "S_OK for every frame" };
                });
            foreach (var entry in new[] { ("missing", Path.Combine(folder,"missing.mp4")), ("corrupt", Path.Combine(folder,"corrupt.mp4")), ("unsupported", Path.Combine(folder,"unsupported.mp4")) })
                await Test(entry.Item1 + " rejected", async () => {
                    try { await new MediaDecoder().InspectAsync(entry.Item2, CancellationToken.None); }
                    catch (Exception e) when (e is IOException or InvalidDataException) { return e.Message; }
                    throw new Exception("Invalid file accepted");
                });
            await Test("cancellation and restart", async () => {
                using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(700));
                bool stopped = false;
                try { await new PlaybackService(window.Dispatcher).PlayAsync(media, displays[0], false, cancel.Token); }
                catch (OperationCanceledException) when (cancel.IsCancellationRequested) { stopped = true; }
                if (!stopped) throw new Exception("Did not cancel");
                using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var r = await new PlaybackService(window.Dispatcher).PlayAsync(media, displays[0], true, limit.Token);
                if (r.Frames != 50) throw new Exception("Restart incomplete"); return r;
            });
        }
        catch (Exception e) { failed = true; await File.WriteAllTextAsync(Path.Combine(folder,"fatal.txt"), e.ToString()); }
        finally { await File.WriteAllTextAsync(Path.Combine(folder,"exit-code.txt"), failed ? "1" : "0"); Application.Current.Shutdown(failed ? 1 : 0); }
    }
}

