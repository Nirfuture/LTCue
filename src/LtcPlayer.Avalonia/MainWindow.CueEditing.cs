using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LtcPlayer.App;
namespace LtcPlayer.Avalonia;

public partial class MainWindow
{
 readonly Cursor timecodeCursor=new(StandardCursorType.Ibeam);
 readonly Cursor readOnlyCursor=new(StandardCursorType.Arrow);
 CueRow? editingTimecode;
 string[]? pendingTimecode;
 static string[] TimecodeParts(CueRow row)=>[row.Hours,row.Minutes,row.Seconds,row.Frames];
 static int TimecodePart(TextBox box)=>box.Tag?.ToString() switch{"Hours"=>0,"Minutes"=>1,"Seconds"=>2,_=>3};
 void TimecodeGotFocus(object? sender,GotFocusEventArgs e)
 {
  if(sender is not TextBox {DataContext:CueRow row})return;
  if(!CanEditProgram){CancelTimecodeEdit();FocusManager?.ClearFocus();return;}
  if(editingTimecode==row)return;
  CommitTimecodeEdit();editingTimecode=row;pendingTimecode=TimecodeParts(row);
 }
 void TimecodeTextChanged(object? sender,TextChangedEventArgs e)
 {
  if(sender is TextBox box&&box.DataContext==editingTimecode&&pendingTimecode!=null&&box.IsFocused)
   pendingTimecode[TimecodePart(box)]=box.Text??"";
 }
 void TimecodeLostFocus(object? sender,RoutedEventArgs e)
 {
  // Moving among the four fields belongs to one edit. Defer until focus settles.
  Dispatcher.UIThread.Post(()=>{
   if(FocusManager?.GetFocusedElement() is TextBox box&&box.Classes.Contains("timecode")&&box.DataContext==editingTimecode)return;
   CommitTimecodeEdit();
  });
 }
 void TimecodeKeyDown(object? sender,KeyEventArgs e)
 {
  if(e.Key is not (Key.Enter or Key.Escape))return;
  e.Handled=true;if(e.Key==Key.Escape)CancelTimecodeEdit();else CommitTimecodeEdit();FocusManager?.ClearFocus();
 }
 void CommitTimecodeEdit()
 {
  var row=editingTimecode;var parts=pendingTimecode;editingTimecode=null;pendingTimecode=null;
  if(row==null||parts==null)return;
  try{if(DefinitionAuthority&&Editor.Rows.Contains(row))Editor.SetTimecode(row,parts);}catch(InvalidOperationException)when(!DefinitionAuthority){}
  RestoreTimecodeControls(row);
 }
 void CancelTimecodeEdit(){var row=editingTimecode;editingTimecode=null;pendingTimecode=null;if(row!=null)RestoreTimecodeControls(row);}
 void RestoreTimecodeControls(CueRow row)
 {
  var parts=TimecodeParts(row);
  foreach(var box in Grid.GetVisualDescendants().OfType<TextBox>().Where(b=>b.Classes.Contains("timecode")&&b.DataContext==row))box.SetCurrentValue(TextBox.TextProperty,parts[TimecodePart(box)]);
 }
 void UpdateCueEditingControls()
 {
  bool allowed=CanEditProgram;Grid.IsReadOnly=!allowed;
  if(!allowed&&editingTimecode!=null){CancelTimecodeEdit();if(FocusManager?.GetFocusedElement() is TextBox box&&box.Classes.Contains("timecode"))FocusManager.ClearFocus();}
  foreach(var box in Grid.GetVisualDescendants().OfType<TextBox>().Where(b=>b.Classes.Contains("timecode"))){box.Cursor=allowed?timecodeCursor:readOnlyCursor;ToolTip.SetTip(box,allowed?"Enterで確定／Escで取消":ProgramEditAccess.Reason);}
  foreach(var check in Grid.GetVisualDescendants().OfType<CheckBox>().Where(b=>b.Classes.Contains("cue-enabled"))){ToolTip.SetTip(check,allowed?"LTC同期の対象":ProgramEditAccess.Reason);}
 }
 void CueEnabledClick(object? sender,RoutedEventArgs e)
 {
  if(sender is not CheckBox {DataContext:CueRow row} check)return;
  CommitTimecodeEdit();try{if(CanEditProgram)Editor.SetEnabled(row,check.IsChecked==true);}catch(InvalidOperationException)when(!DefinitionAuthority){}
  check.SetCurrentValue(CheckBox.IsCheckedProperty,row.Enabled);
 }
}
