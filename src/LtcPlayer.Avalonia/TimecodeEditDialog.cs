using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using LtcPlayer.App;

namespace LtcPlayer.Avalonia;

public enum TimecodeEditMode { Csv, Text, Arrange }
internal sealed record TimecodePreviewRow(string Name, string Before, string After);

internal sealed class TimecodeEditDialog : Window
{
    readonly ShowEditor editor;
    readonly TimecodeEditMode mode;
    readonly TimecodeRate rate;
    readonly CueTimecodeSource[] sources;
    readonly HashSet<Guid> selected;
    readonly Func<OperationAccess> access;
    readonly Action<CueTimecodePlan> apply;
    readonly DispatcherTimer poll = new() { Interval = TimeSpan.FromMilliseconds(100) };
    readonly CancellationTokenSource lifetime = new();
    readonly TextBlock error = new() { Foreground = ThemePalette.ErrorText, TextWrapping = TextWrapping.Wrap };
    readonly TextBlock warning = new() { Foreground = ThemePalette.Warning, TextWrapping = TextWrapping.Wrap };
    readonly TextBlock fileName = new() { Text = "未選択", TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    internal ComboBox Target { get; } = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    internal TextBox? TimecodesText { get; }
    internal TextBox First { get; } = new() { FontFamily = new FontFamily("Consolas"), MaxLength = 11 };
    internal NumericUpDown Gap { get; } = new() { Minimum = 0, Maximum = 86399.999m, Increment = .1m, FormatString = "0.###" };
    internal DataGrid Preview { get; } = new() { Background = ThemePalette.PanelBackground, IsReadOnly = true, AutoGenerateColumns = false, CanUserSortColumns = false, CanUserReorderColumns = false, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal, MinHeight = 130 };
    internal Button Apply { get; } = new() { Content = "反映", IsDefault = true };
    internal CueTimecodePlan? Plan { get; private set; }
    internal bool Applied { get; private set; }
    internal decimal GapSeconds => Gap.Value ?? 0;
    internal string ErrorText => error.Text ?? "";
    string? csv;
    string readError = "";
    bool reading, closed, authorityLost;

    internal TimecodeEditDialog(ShowEditor editor, TimecodeEditMode mode, HashSet<Guid> selected, decimal gap,
        Func<OperationAccess> access, Action<CueTimecodePlan> apply)
    {
        this.editor = editor; this.mode = mode; this.selected = selected; this.access = access; this.apply = apply;
        rate = editor.Rate; sources = editor.Rows.Select(CueTimecodeSource.Capture).ToArray();
        Title = mode switch { TimecodeEditMode.Csv => "CSVから入力", TimecodeEditMode.Text => "テキストから入力", _ => "間隔で配置" };
        Width = 610; Height = 560; MinWidth = 410; MinHeight = mode == TimecodeEditMode.Text ? 520 : mode == TimecodeEditMode.Arrange ? 480 : 430; Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = ThemePalette.PanelBackground;
        var fields = new StackPanel { Spacing = 12 };
        Target.ItemsSource = new[] { $"全キュー（{sources.Length}件）", $"選択キュー（{sources.Count(s => selected.Contains(s.CueId))}件）" };
        Target.SelectedIndex = 0; fields.Children.Add(Field("対象", Target));
        if (mode == TimecodeEditMode.Text)
        {
            TimecodesText = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, Height = 110,
                MaxLength = CueTimecodes.MaximumInputCharacters, FontFamily = new FontFamily("Consolas"), Watermark = "00:00:05:00\n00:00:18:00" };
            fields.Children.Add(Field("開始TC", TimecodesText));
            TimecodesText.TextChanged += (_, _) => RefreshPlan();
        }
        else if (mode == TimecodeEditMode.Csv)
        {
            var choose = new Button { Content = "CSVを選択" };
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            row.Children.Add(choose); fileName.Margin = new Thickness(12, 0, 0, 0); Grid.SetColumn(fileName, 1); row.Children.Add(fileName);
            fields.Children.Add(row);
            choose.Click += async (_, _) => {
                choose.IsEnabled = false;
                try { await ChooseCsvAsync(); }
                finally { if (!closed) choose.IsEnabled = true; }
            };
        }
        else
        {
            try { First.Text = rate.FormatFrames(rate.ParseFrames(sources.First().Start)); }
            catch (Exception e) when (e is InvalidDataException or FormatException or OverflowException) { First.Text = rate.FormatFrames(0); }
            Gap.Value = gap;
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
            var first = Field("先頭の開始TC", First); first.Margin = new Thickness(0, 0, 12, 0); row.Children.Add(first);
            var spacing = Field("動画間隔（秒）", Gap); Grid.SetColumn(spacing, 1); row.Children.Add(spacing); fields.Children.Add(row);
            First.TextChanged += (_, _) => RefreshPlan(); Gap.ValueChanged += (_, _) => RefreshPlan();
        }
        Preview.Columns.Add(new DataGridTemplateColumn { Header = "動画", Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            CellTemplate = new FuncDataTemplate<TimecodePreviewRow>((row, _) => {
                var text = new TextBlock { Text = row?.Name, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
                ToolTip.SetTip(text, row?.Name); return text;
            }) });
        Preview.Columns.Add(new DataGridTextColumn { Header = "変更前", Binding = new Binding(nameof(TimecodePreviewRow.Before)), Width = new DataGridLength(135) });
        Preview.Columns.Add(new DataGridTextColumn { Header = "変更後", Binding = new Binding(nameof(TimecodePreviewRow.After)), Width = new DataGridLength(135) });
        var cancel = new Button { Content = "キャンセル", IsCancel = true };
        cancel.Click += (_, _) => Close(); Apply.Click += (_, _) => TryApply();
        var bottom = new StackPanel { Spacing = 8 };
        bottom.Children.Add(new TextBlock { Text = "LTC形式：" + rate.Name, Foreground = ThemePalette.SecondaryText, FontSize = 12 });
        bottom.Children.Add(warning); bottom.Children.Add(error);
        bottom.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { cancel, Apply } });
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(20) };
        fields.Margin = new Thickness(0, 0, 14, 14); layout.Children.Add(fields);
        Grid.SetRow(Preview, 1); Preview.Margin = new Thickness(0, 0, 0, 14); layout.Children.Add(Preview);
        Grid.SetRow(bottom, 2); layout.Children.Add(bottom); Content = layout;
        Target.SelectionChanged += (_, _) => RefreshPlan();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
        poll.Tick += (_, _) => UpdateAuthority();
        Opened += (_, _) => { poll.Start(); RefreshPlan(); };
        Closed += (_, _) => { closed = true; poll.Stop(); lifetime.Cancel(); lifetime.Dispose(); };
        WindowPlacement.Track(this);
    }

    internal static StackPanel Field(string label, Control control) => new() { Spacing = 6,
        Children = { new TextBlock { Text = label }, control } };

    internal void SetCsvForPreview(string text, string name)
    {
        csv = text; fileName.Text = name; ToolTip.SetTip(fileName, name); readError = ""; RefreshPlan();
    }

    async Task ChooseCsvAsync()
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new() { Title = "開始TCのCSVを開く", FileTypeFilter = [new("CSV") { Patterns = ["*.csv"] }] });
            if (closed || files.FirstOrDefault() is not {} file) return;
            var readToken = lifetime.Token;
            reading = true; csv = null; readError = ""; fileName.Text = file.Name; ToolTip.SetTip(fileName, file.Name); RefreshPlan();
            string text;
            using (file)
            await using (var stream = await file.OpenReadAsync())
            using (var reader = new StreamReader(stream, new UTF8Encoding(false, true), true))
            {
                var content = new StringBuilder(); var buffer = new char[4096]; int count;
                while ((count = await reader.ReadAsync(buffer.AsMemory(), readToken)) > 0)
                {
                    content.Append(buffer, 0, count);
                    if (content.Length > CueTimecodes.MaximumInputCharacters) throw new InvalidDataException("CSVが大きすぎます（最大1Mi文字）");
                }
                text = content.ToString();
            }
            if (!closed) csv = text;
        }
        catch (OperationCanceledException) { }
        catch (DecoderFallbackException) { if (!closed) readError = "CSVをUTF-8で保存して選び直してください"; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException) { if (!closed) readError = e.Message; }
        finally { if (!closed) { reading = false; RefreshPlan(); } }
    }

    internal void RefreshPlan()
    {
        if (closed) return;
        Plan = null; warning.Text = ""; Apply.IsEnabled = false; Preview.ItemsSource = null;
        try
        {
            if (reading) throw new InvalidDataException("CSVを読み込み中");
            if (readError != "") throw new InvalidDataException(readError);
            var targets = Target.SelectedIndex == 1 ? sources.Where(s => selected.Contains(s.CueId)).ToArray() : sources;
            if (mode == TimecodeEditMode.Csv && csv == null) throw new InvalidDataException("CSVを選択してください");
            Plan = mode switch {
                TimecodeEditMode.Arrange => CueTimecodes.Arrange(targets, First.Text ?? "", Gap.Value ?? throw new InvalidDataException("動画間隔を入力してください"), rate),
                TimecodeEditMode.Csv => CueTimecodes.Assign(targets, CueTimecodes.ReadCsv(csv!), rate),
                _ => CueTimecodes.Assign(targets, CueTimecodes.ReadText(TimecodesText!.Text ?? ""), rate)
            };
            Preview.ItemsSource = Plan.Changes.Select(c => new TimecodePreviewRow(Path.GetFileName(c.Source.FilePath), c.Source.Start, c.Start)).ToArray();
            var replacements = Plan.Changes.ToDictionary(c => c.Source.CueId, c => c.Start);
            var starts = new List<long>();
            foreach (var row in editor.Rows.Where(r => r.Enabled))
                try { starts.Add(rate.ParseFrames(replacements.GetValueOrDefault(row.CueId, row.StartTimecode))); }
                catch (Exception e) when (e is FormatException or OverflowException or InvalidDataException) { }
            if (starts.Distinct().Count() != starts.Count) warning.Text = "開始TCが重複する有効キューは同期対象外になります";
            error.Text = ""; UpdateAuthority();
        }
        catch (Exception e) when (e is FormatException or OverflowException or InvalidDataException)
        { Plan = null; error.Text = e.Message; }
    }

    void UpdateAuthority()
    {
        if (closed) return;
        var current = access();
        if (!current.Allowed) authorityLost = true;
        if (authorityLost) { Apply.IsEnabled = false; error.Text = current.Allowed ? "LTC状態が変わりました。開き直してください" : current.Reason; }
        else Apply.IsEnabled = Plan != null && !reading;
    }

    internal void TryApply()
    {
        UpdateAuthority();
        if (!Apply.IsEnabled || Plan == null) return;
        try { apply(Plan); Applied = true; Close(); }
        catch (Exception e) when (e is InvalidOperationException or InvalidDataException)
        { Plan = null; error.Text = e.Message; Apply.IsEnabled = false; }
    }
}

internal sealed class CuePlacementDialog : Window
{
    readonly Func<OperationAccess> access;
    readonly Action<CuePlacementPreferences> save;
    readonly DispatcherTimer poll = new() { Interval = TimeSpan.FromMilliseconds(100) };
    internal ComboBox Mode { get; } = new() { ItemsSource = new[] { "00:00:00:00", "既存キューの後ろ" }, HorizontalAlignment = HorizontalAlignment.Stretch };
    internal NumericUpDown Gap { get; } = new() { Minimum = 0, Maximum = 86399.999m, Increment = .1m, FormatString = "0.###" };
    internal Button Save { get; } = new() { Content = "保存", IsDefault = true };
    readonly TextBlock error = new() { Foreground = ThemePalette.ErrorText, TextWrapping = TextWrapping.Wrap };
    bool authorityLost;

    internal CuePlacementDialog(CuePlacementPreferences current, Func<OperationAccess> access, Action<CuePlacementPreferences> save)
    {
        this.access = access; this.save = save;
        Title = "追加時の開始TC"; Width = 430; SizeToContent = SizeToContent.Height; MinWidth = 350;
        Topmost = true; WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = ThemePalette.PanelBackground;
        Mode.SelectedIndex = current.AfterExisting ? 1 : 0; Gap.Value = current.GapSeconds;
        var cancel = new Button { Content = "キャンセル", IsCancel = true }; cancel.Click += (_, _) => Close();
        Save.Click += (_, _) => TrySave();
        Content = new StackPanel { Margin = new Thickness(20), Spacing = 16, Children = {
            TimecodeEditDialog.Field("新規キューの開始TC", Mode), TimecodeEditDialog.Field("動画間隔（秒）", Gap), error,
            new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { cancel, Save } } } };
        Mode.SelectionChanged += (_, _) => UpdateState(); Gap.ValueChanged += (_, _) => UpdateState();
        poll.Tick += (_, _) => UpdateState(); Opened += (_, _) => { poll.Start(); UpdateState(); }; Closed += (_, _) => poll.Stop();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
        WindowPlacement.Track(this);
    }
    void UpdateState()
    {
        Gap.IsEnabled = Mode.SelectedIndex == 1;
        var current = access(); if (!current.Allowed) authorityLost = true;
        Save.IsEnabled = !authorityLost && Gap.Value != null;
        if (authorityLost) error.Text = current.Allowed ? "LTC状態が変わりました。開き直してください" : current.Reason;
    }
    internal void TrySave()
    {
        UpdateState(); if (!Save.IsEnabled) return;
        try { save(new(Mode.SelectedIndex == 1, Gap.Value ?? 0)); Close(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException) { error.Text = e.Message; }
    }
}
