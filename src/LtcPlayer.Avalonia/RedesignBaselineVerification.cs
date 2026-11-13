using System.Diagnostics;
using System.Text.Json;
using Avalonia.Controls;
using LtcPlayer.App;
namespace LtcPlayer.Avalonia;

// Captures the pre-migration behaviour. A documented defect is evidence, not a PASS assertion.
public static class RedesignBaselineVerification
{
    public static async Task RunAsync(MainWindow w,string folder)
    {
        Directory.CreateDirectory(folder); var observations=new List<object>();
        var process=Process.GetCurrentProcess();
        try
        {
            var viewport=w.FindControl<Grid>("PreviewViewport")!;
            var progress=w.FindControl<ProgressBar>("ActivityBar")!;
            var label=w.FindControl<TextBlock>("ActivityLabel")!;
            await Task.Delay(100);
            var before=viewport.Bounds;
            progress.IsVisible=true;label.Text="動画を読み込み中…";label.IsVisible=true;
            await Task.Delay(100);var during=viewport.Bounds;
            progress.IsVisible=false;label.IsVisible=false;await Task.Delay(100);var after=viewport.Bounds;
            observations.Add(new{test="progress-layout",before,during,after,GeometryChanged=before!=during,Interpretation="Activity visibility changes the star-sized video viewport; this is one source of flicker, not a complete diagnosis."});
            string media=Program.Option("--media")??Path.Combine(AppContext.BaseDirectory,"TestMedia","HAP_1_1080p_2997_10s.mov");
            var elapsed=Stopwatch.StartNew();await w.Preview.LoadAsync(media);double loadMs=elapsed.Elapsed.TotalMilliseconds;
            elapsed.Restart();await w.Preview.PlayAsync();double playMs=elapsed.Elapsed.TotalMilliseconds;
            await Task.Delay(1000);var playing=w.Preview.Snapshot;
            await w.Preview.PauseAsync();elapsed.Restart();await w.Preview.SeekAsync(2);double seekMs=elapsed.Elapsed.TotalMilliseconds;
            process.Refresh();observations.Add(new{test="manual-hap",loadMs,playMs,seekMs,playing.Presented,playing.Position,WorkingSet=process.WorkingSet64,Handles=process.HandleCount});
            observations.Add(new{test="output-transition-source-audit",Result="Current ArmAsync stops sync preview, calls SetArmedAsync, re-inspects enabled cues and recreates presentation HWND; output stop reconstructs preview. These are removed only in the following migration stages."});
            File.WriteAllText(Path.Combine(folder,"observations.json"),JsonSerializer.Serialize(observations,new JsonSerializerOptions{WriteIndented=true}));
        }
        catch(Exception e){File.WriteAllText(Path.Combine(folder,"failure.txt"),e.ToString());MainWindow.Shutdown(1);return;}
        finally{await w.Preview.StopAsync();await w.Monitor.DisposeAsync();}
        MainWindow.Shutdown(0);
    }
}
