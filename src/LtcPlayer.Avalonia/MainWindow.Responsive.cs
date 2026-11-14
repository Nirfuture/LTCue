using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
namespace LtcPlayer.Avalonia;
public partial class MainWindow
{
 bool updatingLayout;
 internal bool CompactLayout{get;private set;}
 bool initialContentHeightApplied;
 Size? initialRequestedSize;
 void InitializeResponsiveLayout(){
  WindowPlacement.Track(this);
  SizeChanged+=(_,_)=>UpdateResponsiveLayout();
  Control<ScrollViewer>("WorkspaceScroll").SizeChanged+=(_,_)=>UpdateResponsiveLayout();
  Control<Grid>("PreviewPane").SizeChanged+=(_,_)=>UpdateResponsiveLayout();
  Opened+=(_,_)=>{initialRequestedSize=new Size(Width,Height);UpdateResponsiveLayout();};
 }
 internal void UpdateResponsiveLayout(){
  if(updatingLayout)return;updatingLayout=true;
  try{
   bool compact=Bounds.Width<1100||Bounds.Height>=Bounds.Width;
   var body=Control<Grid>("Workspace");var scroll=Control<ScrollViewer>("WorkspaceScroll");var editor=Control<Border>("EditorPane");var preview=Control<Grid>("PreviewPane");var viewport=Control<Grid>("PreviewViewport");
   Control<Grid>("MainLayout").Margin=new Thickness(compact?12:22);
   if(compact!=CompactLayout||body.RowDefinitions.Count==0){
   CompactLayout=compact;
   body.ColumnDefinitions=new ColumnDefinitions(compact?"*":"*,18,420");
   body.RowDefinitions=new RowDefinitions(compact?"Auto,14,Auto":"*");
   global::Avalonia.Controls.Grid.SetColumn(preview,compact?0:2);global::Avalonia.Controls.Grid.SetRow(preview,compact?2:0);
   scroll.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;
   preview.RowDefinitions=new RowDefinitions("Auto,Auto,Auto");
   }
   body.Height=double.NaN;
   // The list uses available height; right-side cards retain their natural height.
   body.MinHeight=compact?0:Math.Max(360,scroll.Bounds.Height);
   // Bound the list viewport even with many rows; it must not size the window.
   editor.Height=compact?Math.Clamp(scroll.Bounds.Height*.36,220,400):Math.Max(Math.Max(360,scroll.Bounds.Height),preview.Bounds.Height);
   editor.VerticalAlignment=compact?global::Avalonia.Layout.VerticalAlignment.Top:global::Avalonia.Layout.VerticalAlignment.Stretch;
   preview.VerticalAlignment=global::Avalonia.Layout.VerticalAlignment.Top;
   double previewWidth=Math.Max(200,(preview.Bounds.Width>0?preview.Bounds.Width:420)-26);
   var size=Surface.CanvasSize;
   viewport.Height=Math.Clamp(previewWidth*size.Height/Math.Max(1.0,size.Width),160,420);
   UpdatePreviewGeometry();
  }finally{updatingLayout=false;}
 }
 // Called once after initial content is measured, never from resize events.
 internal void FitInitialContentHeight()
 {
  if(initialContentHeightApplied)return;
  initialContentHeightApplied=true;
  if(CompactLayout||WindowState!=WindowState.Normal)return;
  // Initial loading is asynchronous. Respect a resize made while it was loading.
  if(initialRequestedSize is {} requested&&(Math.Abs(Width-requested.Width)>1||Math.Abs(Height-requested.Height)>1))return;
  var scroll=Control<ScrollViewer>("WorkspaceScroll");var preview=Control<Grid>("PreviewPane");
  double surrounding=ClientSize.Height-scroll.Bounds.Height;
  Height=Math.Max(MinHeight,surrounding+Math.Max(360,preview.Bounds.Height));
  WindowPlacement.Fit(this);
 }
}
