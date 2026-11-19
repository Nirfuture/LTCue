using Avalonia.Controls;
using Avalonia.Input;
namespace LtcPlayer.Avalonia;

public sealed class WorkspaceScrollViewer : ScrollViewer
{
 protected override Type StyleKeyOverride=>typeof(ScrollViewer);
 protected override void OnGotFocus(GotFocusEventArgs e)
 {
  // DataGrid focuses itself on a row click. Bringing its entire (possibly
  // taller than the workspace) bounds into view hides the editor toolbar.
  // Its own row navigation already scrolls the selected cell into view.
  // Keep normal focus following for Tab navigation and child editors.
  if(e.Source is DataGrid && e.NavigationMethod is NavigationMethod.Pointer or NavigationMethod.Unspecified)return;
  base.OnGotFocus(e);
 }
}
