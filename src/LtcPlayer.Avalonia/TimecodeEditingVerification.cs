using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using LtcPlayer.App;

namespace LtcPlayer.Avalonia;

public static class TimecodeEditingVerification
{
    public static async Task RunAsync(MainWindow w, string folder)
    {
        Directory.CreateDirectory(folder); var results = new List<object>(); bool failed = false;
        void Check(bool condition, string reason) { if (!condition) throw new IOException(reason); }
        async Task Test(string name, Func<Task> body)
        {
            try { await body(); results.Add(new { test = name, status = "PASS" }); }
            catch (Exception e) { failed = true; results.Add(new { test = name, status = "FAIL", detail = e.ToString() }); }
            File.WriteAllText(Path.Combine(folder, "results.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        }
        void Capture(Window dialog, string name)
        {
            using var image = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(dialog.ClientSize.Width), (int)Math.Ceiling(dialog.ClientSize.Height)), new Vector(96, 96));
            image.Render(dialog); image.Save(Path.Combine(folder, name + ".png"));
        }
        var rate = TimecodeRate.Modes.Single(r => r.Name == "25");
        var a = new CueRow { FilePath = Program.Option("--media")! };
        CueRow b = null!, c = null!;
        TimecodeEditDialog Dialog(TimecodeEditMode mode, Func<OperationAccess>? access = null) =>
            new(w.Editor, mode, new HashSet<Guid> { b.CueId, c.CueId }, 3, access ?? (() => OperationAccess.Permit), w.Editor.SetTimecodes);
        try
        {
            await w.ApplySettingsAsync(new(new(Rate: "25"), VideoRenderer.Displays().First(d => d.Primary != 0)));
            await ShowProgramService.RefreshAsync(a, CancellationToken.None); a.SetStart("00:00:05:00");
            b = a.CloneDisabled(); b.Enabled = true; b.SetStart("00:00:20:00");
            c = a.CloneDisabled(); c.SetStart("00:00:35:00"); w.Editor.Add(a); w.Editor.Add(b); w.Editor.Add(c);
            await Test("enabled error count excludes disabled cues and overlap warnings", () => {
                b.SourceError = "missing"; c.SourceError = "missing"; w.Editor.Validate();
                Check(w.FindControl<TextBlock>("EnabledCueCount")!.Text == "2" && w.FindControl<TextBlock>("ErrorCueCount")!.Text == "1", "disabled error counted");
                b.ClearSourceError(); c.ClearSourceError(); b.AudioIssue = "audio"; w.Editor.Validate();
                Check(w.FindControl<TextBlock>("ErrorCueCount")!.Text == "1", "audio issue missing");
                b.AudioIssue = ""; b.SetStart("00:00:06:00");
                Check(a.Warning != "" && w.FindControl<TextBlock>("ErrorCueCount")!.Text == "0", "overlap counted as error"); b.SetStart("00:00:20:00");
                return Task.CompletedTask;
            });
            await Test("TC header menu exists and contains bulk operations", async () => {
                await Task.Delay(120);
                var button = w.GetVisualDescendants().OfType<Button>().Single(x => global::Avalonia.Automation.AutomationProperties.GetName(x) == "開始TCメニュー");
                Check(button.IsEnabled, "menu unavailable while waiting"); Capture(w, "main"); button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(70);
                Check(button.ContextMenu?.ItemsSource?.Cast<object>().OfType<MenuItem>().Count() == 4, "menu entries missing");
                button.ContextMenu!.Close();
            });
            await Test("CSV view has file and preview with no editable TC text", async () => {
                var d = Dialog(TimecodeEditMode.Csv); var task = d.ShowDialog(w);
                try {
                    await Task.Delay(100); Check(d.TimecodesText == null && !d.GetVisualDescendants().OfType<TextBox>().Any(t=>t.IsEffectivelyVisible&&!t.IsReadOnly&&t.Focusable), "CSV exposes TC text field");
                    d.SetCsvForPreview("開始TC\r\n\"00:00:05:00\"\r\n00:00:18:00\r\n00:00:33:00", "starts.csv");
                    Check(d.Apply.IsEnabled && d.Plan?.Changes.Count == 3, "CSV preview invalid"); await Task.Delay(80); Check(d.Preview.GetVisualDescendants().OfType<DataGridRow>().Any(), "CSV rows not visible"); d.Width=d.MinWidth;d.Height=d.MinHeight;await Task.Delay(80);var actionPoint=d.Apply.TranslatePoint(default,d)!.Value;Check(actionPoint.Y+d.Apply.Bounds.Height<=d.ClientSize.Height,"CSV footer clipped");Capture(d, "csv-minimum");d.Width=610;d.Height=560;await Task.Delay(80);Capture(d, "csv");
                    d.SetCsvForPreview("00:00:05:00,other", "bad.csv"); Check(!d.Apply.IsEnabled && d.ErrorText.Contains("1列"), "multi-column accepted");
                } finally { d.Close(); await task; }
            });
            await Test("text mismatch and bad rows cannot partially change cues", async () => {
                var before = w.Editor.Rows.Select(r => r.StartTimecode).ToArray(); var d = Dialog(TimecodeEditMode.Text); var task = d.ShowDialog(w);
                try {
                    await Task.Delay(80); d.TimecodesText!.Text = "00:00:01:00"; d.RefreshPlan();
                    Check(!d.Apply.IsEnabled && d.ErrorText.Contains("入力は1件"), "mismatch accepted");
                    d.TimecodesText.Text = "00:00:01:00\nbad\n00:00:03:00"; d.RefreshPlan(); d.TryApply();
                    Check(!d.Applied && w.Editor.Rows.Select(r => r.StartTimecode).SequenceEqual(before), "partial text commit"); Capture(d, "text-invalid");
                } finally { d.Close(); await task; }
            });
            await Test("arrange selected cues preserves manual video session and output canvas", async () => {
                await w.PlayCueForTestAsync(a); await Task.Delay(120); await w.Preview.PauseAsync();
                var session = w.Preview.PlaybackSessionId; var canvas = w.Surface.Canvas.SessionId; var position = w.Preview.Snapshot.Position; string first = a.StartTimecode;
                var d = Dialog(TimecodeEditMode.Arrange); var task = d.ShowDialog(w);
                try {
                    await Task.Delay(80); d.Target.SelectedIndex = 1; d.First.Text = "00:01:00:00"; d.Gap.Value = 3; d.RefreshPlan();
                    Check(d.Plan?.Changes.Count == 2 && d.Apply.IsEnabled, "selected plan"); await Task.Delay(80); Check(d.Preview.GetVisualDescendants().OfType<DataGridRow>().Any(), "preview rows not visible"); Capture(d, "arrange"); d.TryApply(); await task;
                    Check(d.Applied && a.StartTimecode == first && b.StartTimecode == "00:01:00:00" && !c.Enabled, "wrong target");
                    Check(w.Preview.PlaybackSessionId == session && w.Surface.Canvas.SessionId == canvas && Math.Abs(w.Preview.Snapshot.Position - position) < .05, "TC edit rebuilt playback");
                } finally { if (!d.Applied) { d.Close(); await task; } }
            });
            await Test("LTC acquisition while dialog open blocks final commit", async () => {
                bool allowed = true; var d = Dialog(TimecodeEditMode.Text, () => allowed ? OperationAccess.Permit : OperationAccess.Deny("LTC同期中")); var task = d.ShowDialog(w);
                try {
                    await Task.Delay(80); d.TimecodesText!.Text = "00:00:01:00\n00:00:02:00\n00:00:03:00"; d.RefreshPlan();
                    var before = w.Editor.Rows.Select(r => r.StartTimecode).ToArray(); allowed = false; d.TryApply();
                    Check(!d.Applied && !d.Apply.IsEnabled && w.Editor.Rows.Select(r => r.StartTimecode).SequenceEqual(before), "locked commit");
                } finally { d.Close(); await task; }
            });
            await Test("source changed since preview is refused without partial commit", async () => {
                var d = Dialog(TimecodeEditMode.Text); var task = d.ShowDialog(w);
                try {
                    await Task.Delay(80); d.TimecodesText!.Text = "00:00:01:00\n00:00:02:00\n00:00:03:00"; d.RefreshPlan();
                    string before = a.StartTimecode; b.SetStart("00:02:00:00"); d.TryApply();
                    Check(!d.Applied && a.StartTimecode == before && !d.Apply.IsEnabled && d.ErrorText.Contains("変わりました"), "stale proposal applied");
                } finally { d.Close(); await task; }
            });
            await Test("addition preferences persist without changing input or playback", async () => {
                var clock = w.Monitor.Clock; var session = w.Preview.PlaybackSessionId; var canvas = w.Surface.Canvas.SessionId;
                var d = new CuePlacementDialog(new(), () => OperationAccess.Permit, w.SaveCuePlacement); var task = d.ShowDialog(w);
                try {
                    await Task.Delay(80); d.Mode.SelectedIndex = 1; d.Gap.Value = 3; Capture(d, "addition"); d.TrySave(); await task;
                    Check(InputSettings.Load(Program.Option("--settings")).CuePlacement == new CuePlacementPreferences(true, 3), "preferences not saved");
                    Check(ReferenceEquals(clock, w.Monitor.Clock) && w.Preview.PlaybackSessionId == session && w.Surface.Canvas.SessionId == canvas, "preferences reset playback");
                } finally { d.Close(); }
            });
            await Test("failed preference persistence retains active settings", async () => {
                string file = Program.Option("--settings")!; var before = InputSettings.Load(file).CuePlacement;
                var d = new CuePlacementDialog(before, () => OperationAccess.Permit, w.SaveCuePlacement); var task = d.ShowDialog(w);
                try {
                    await Task.Delay(80); d.Gap.Value = 4;
                    using (var held = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read)) d.TrySave();
                    Check(InputSettings.Load(file).CuePlacement == before && d.IsVisible, "failed save committed");
                } finally { d.Close(); await task; }
            });
            await Test("automatic addition follows disabled tail and validates imported metadata", async () => {
                await w.Preview.StopAsync();
                var before = w.Editor.Rows.Select(CueTimecodeSource.Capture).ToArray(); var prefs = InputSettings.Load(Program.Option("--settings")).CuePlacement;
                string expected = CueTimecodes.AppendStart(before, prefs.GapSeconds, rate);
                await w.Editor.ImportAsync([a.FilePath], "00:00:00:00", prefs);
                Check(w.Editor.Rows.Last().StartTimecode == expected.Replace(';', ':') && w.Editor.Rows.Last().Duration > 0, "auto add start");
            });
        }
        catch (Exception e) { failed = true; File.WriteAllText(Path.Combine(folder, "fatal.txt"), e.ToString()); }
        finally { await w.Coordinator.DisposeAsync(); await w.Preview.DisposeAsync(); await w.Monitor.DisposeAsync(); MainWindow.Shutdown(failed ? 1 : 0); }
    }
}
