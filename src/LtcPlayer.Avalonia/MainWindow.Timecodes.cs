using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using LtcPlayer.App;

namespace LtcPlayer.Avalonia;

public partial class MainWindow
{
    Button? timecodeMenuButton;
    bool timecodeDialogOpen;
    decimal lastArrangementGap;

    void TimecodeMenuAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Button button) { timecodeMenuButton = button; UpdateTimecodeMenu(); }
    }

    void UpdateTimecodeMenu()
    {
        if (timecodeMenuButton is not {} button) return;
        button.IsEnabled = CanEditProgram && !timecodeDialogOpen;
        ToolTip.SetShowOnDisabled(button, true);
        ToolTip.SetTip(button, button.IsEnabled ? "開始TCの一括入力・追加設定" : timecodeDialogOpen ? "開始TCを編集中" : ProgramEditAccess.Reason);
    }

    void TimecodeMenuClick(object? sender, RoutedEventArgs e)
    {
        if (!CanEditProgram || timecodeDialogOpen || sender is not Button button) return;
        CommitTimecodeEdit();
        MenuItem Item(string label, TimecodeEditMode mode)
        {
            var item = new MenuItem { Header = label, IsEnabled = Editor.Rows.Count > 0 };
            item.Click += async (_, _) => await Safe(() => OpenTimecodeDialogAsync(mode));
            return item;
        }
        var addition = new MenuItem { Header = "追加時の開始TC…" };
        addition.Click += async (_, _) => await Safe(OpenCuePlacementDialogAsync);
        var menu = new ContextMenu { ItemsSource = new object[] {
            Item("CSVから入力…", TimecodeEditMode.Csv), Item("テキストから入力…", TimecodeEditMode.Text),
            Item("間隔で配置…", TimecodeEditMode.Arrange), new Separator(), addition } };
        menu.Closed += (_, _) => button.ContextMenu = null;
        button.ContextMenu = menu; menu.Open(button);
    }

    internal async Task OpenTimecodeDialogAsync(TimecodeEditMode mode)
    {
        if (!CanEditProgram || timecodeDialogOpen || Editor.Rows.Count == 0) return;
        CommitTimecodeEdit();
        timecodeDialogOpen = true; UpdateTimecodeMenu();
        var selected = SelectedCueIds().ToHashSet();
        var order = Editor.Rows.Select(r => r.CueId).ToArray();
        try
        {
            var dialog = new TimecodeEditDialog(Editor, mode, selected, lastArrangementGap,
                () => ProgramEditAccess,
                plan => {
                    if (!order.SequenceEqual(Editor.Rows.Select(r => r.CueId)))
                        throw new InvalidOperationException("一覧が変わりました。開き直してください");
                    Editor.SetTimecodes(plan);
                });
            await dialog.ShowDialog(this);
            if (dialog.Applied && mode == TimecodeEditMode.Arrange) lastArrangementGap = dialog.GapSeconds;
        }
        finally { timecodeDialogOpen = false; UpdateTimecodeMenu(); }
    }

    internal async Task OpenCuePlacementDialogAsync()
    {
        if (!CanEditProgram || timecodeDialogOpen) return;
        CommitTimecodeEdit(); timecodeDialogOpen = true; UpdateTimecodeMenu();
        try
        {
            var dialog = new CuePlacementDialog(settings.CuePlacement, () => ProgramEditAccess, SaveCuePlacement);
            await dialog.ShowDialog(this);
        }
        finally { timecodeDialogOpen = false; UpdateTimecodeMenu(); }
    }

    internal void SaveCuePlacement(CuePlacementPreferences preferences)
    {
        var access = ProgramEditAccess;
        if (!access.Allowed) throw new InvalidOperationException(access.Reason);
        preferences.Validate();
        var next = settings with { CuePlacement = preferences };
        using var pending = new PendingSettings(Program.Option("--settings") ?? InputSettings.PathName, next, selectedDisplayName);
        pending.Commit();
        settings = next; // Persistence failure must leave the live preferences unchanged.
    }
}
