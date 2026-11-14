using Avalonia.Controls;
namespace LtcPlayer.Avalonia;
public partial class MainWindow
{
 void UpdateProgramSummary()
 {
  int enabled=Editor.Rows.Count(row=>row.Enabled),errors=Editor.Rows.Count(row=>row.Enabled&&row.HasError);
  Control<TextBlock>("EnabledCueCount").Text=enabled.ToString();Control<TextBlock>("ErrorCueCount").Text=errors.ToString();
  var color=errors>0?ThemePalette.ErrorText:ThemePalette.SecondaryText;
  foreach(var name in new[]{"ErrorCueLabel","ErrorCueCount","ErrorCueUnit"})Control<TextBlock>(name).Foreground=color;
 }
}
