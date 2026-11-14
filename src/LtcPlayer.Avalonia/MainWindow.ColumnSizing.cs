using Avalonia.Controls;
using Avalonia.Input;
namespace LtcPlayer.Avalonia;

public partial class MainWindow
{
 // A single handle divides the shared space between two nonadjacent columns.
 // The DataGrid continues to own layout, scrolling and minimum-width handling.
 sealed record ColumnResize(Control Handle,IPointer Pointer,DataGridColumn Video,DataGridColumn Status,
  double StartX,double VideoWidth,double TotalWidth);
 ColumnResize? columnResize;
 void InitializeColumnSizing()
 {
  Deactivated+=(_,_)=>FinishColumnResize();
  SizeChanged+=(_,_)=>FinishColumnResize();
 }
 void ColumnResizePressed(object? sender,PointerPressedEventArgs e)
 {
  if(sender is not Control handle||!e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed)return;
  FinishColumnResize();
  var video=Grid.Columns.Single(c=>Equals(c.Tag,"video"));var status=Grid.Columns.Single(c=>Equals(c.Tag,"status"));
  columnResize=new(handle,e.Pointer,video,status,e.GetPosition(this).X,video.ActualWidth,video.ActualWidth+status.ActualWidth);
  e.Pointer.Capture(handle);e.Handled=true;
 }
 void ColumnResizeMoved(object? sender,PointerEventArgs e)
 {
  if(columnResize is not {} drag||e.Pointer!=drag.Pointer)return;
  var video=drag.Video;var status=drag.Status;
  double width=Math.Clamp(drag.VideoWidth+e.GetPosition(this).X-drag.StartX,video.MinWidth,Math.Max(video.MinWidth,drag.TotalWidth-status.MinWidth));
  // Keep star sizing so later window resizing retains the chosen proportion.
  video.Width=new(width,DataGridLengthUnitType.Star);
  status.Width=new(Math.Max(status.MinWidth,drag.TotalWidth-width),DataGridLengthUnitType.Star);
  e.Handled=true;
 }
 void ColumnResizeReleased(object? sender,PointerReleasedEventArgs e)
 {
  if(columnResize?.Pointer!=e.Pointer)return;
  FinishColumnResize();e.Handled=true;
 }
 void ColumnResizeCaptureLost(object? sender,PointerCaptureLostEventArgs e)=>FinishColumnResize();
 void FinishColumnResize()
 {
  var drag=columnResize;columnResize=null;
  if(drag!=null&&ReferenceEquals(drag.Pointer.Captured,drag.Handle))drag.Pointer.Capture(null);
 }
}
