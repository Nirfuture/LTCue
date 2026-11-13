using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
namespace LtcPlayer.Avalonia;
public partial class MainWindow
{
 void CueHandleAttached(object? sender,VisualTreeAttachmentEventArgs e){if(sender is Control handle&&handle.GetVisualAncestors().OfType<DataGridCell>().FirstOrDefault() is {} cell)cell.Classes.Add("drag-handle-cell");}
 internal void MoveCue(LtcPlayer.App.CueRow row,int target)
 {
  if(armed||busy||closing)return;
  // ObservableCollection.Move can change DataGrid selection before rebinding.
  // Neither selection notification may reload the currently playing source.
  previewSelection=row;bool previous=rebinding;rebinding=true;
  try{Editor.Move(row,target);RebindGrid(row);StatusMessage("表示順を変更しました。");}
  finally{rebinding=previous;}
 }
 int? cueInsertionSlot;
 IPointer? cueDragPointer;
 internal static int InsertionMoveIndex(int from,int? slot)=>slot is {} i?i-(from<i?1:0):-1;
 void CancelCueDrag(){var pointer=cueDragPointer;cueDragPointer=null;dragged=null;dragging=false;cueInsertionSlot=null;Control<Border>("CueInsertionLine").IsVisible=false;pointer?.Capture(null);}
 internal void UpdateCueInsertion(Point point,bool autoScroll)
 {
  var line=Control<Border>("CueInsertionLine");line.IsVisible=false;cueInsertionSlot=null;
  if(point.X<0||point.X>Grid.Bounds.Width||point.Y<0||point.Y>Grid.Bounds.Height)return;
  var rows=Grid.GetVisualDescendants().OfType<DataGridRow>().Where(r=>r.DataContext is LtcPlayer.App.CueRow).Select(r=>new{Row=r,Point=r.TranslatePoint(default,Grid)}).Where(r=>r.Point.HasValue).OrderBy(r=>r.Point!.Value.Y).ToArray();
  if(rows.Length==0)return;
  double first=rows[0].Point!.Value.Y;
  if(point.Y<Math.Max(first,38))return;
  if(autoScroll){var scroll=Grid.GetVisualDescendants().OfType<ScrollViewer>().OrderByDescending(s=>s.Extent.Height-s.Viewport.Height).FirstOrDefault();if(scroll!=null){double delta=point.Y<first+20?-16:point.Y>Grid.Bounds.Height-30?16:0;scroll.Offset=new Vector(scroll.Offset.X,Math.Clamp(scroll.Offset.Y+delta,0,Math.Max(0,scroll.Extent.Height-scroll.Viewport.Height)));}}
  int slot=Editor.Rows.IndexOf((LtcPlayer.App.CueRow)rows[^1].Row.DataContext!)+1;double y=rows[^1].Point!.Value.Y+rows[^1].Row.Bounds.Height;
  foreach(var entry in rows){double top=entry.Point!.Value.Y;if(point.Y<top+entry.Row.Bounds.Height/2){slot=Editor.Rows.IndexOf((LtcPlayer.App.CueRow)entry.Row.DataContext!);y=top;break;}}
  cueInsertionSlot=slot;
  line.Width=Math.Max(0,Grid.Bounds.Width-14);Canvas.SetLeft(line,0);Canvas.SetTop(line,Math.Clamp(y-2,38,Math.Max(38,Grid.Bounds.Height-4)));line.IsVisible=true;
 }
}
