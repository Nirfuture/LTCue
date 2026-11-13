using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
namespace LtcPlayer.Avalonia;
public partial class MainWindow
{
 bool updatingLayout;
 internal bool CompactLayout{get;private set;}
 void InitializeResponsiveLayout(){
  WindowPlacement.Track(this);
  SizeChanged+=(_,_)=>UpdateResponsiveLayout();
  Control<ScrollViewer>("WorkspaceScroll").SizeChanged+=(_,_)=>UpdateResponsiveLayout();
  Control<Grid>("PreviewPane").SizeChanged+=(_,_)=>UpdateResponsiveLayout();
  Opened+=(_,_)=>UpdateResponsiveLayout();
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
   body.RowDefinitions=new RowDefinitions(compact?"Auto,14,Auto":"Auto");
   global::Avalonia.Controls.Grid.SetColumn(preview,compact?0:2);global::Avalonia.Controls.Grid.SetRow(preview,compact?2:0);
   scroll.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;
   preview.RowDefinitions=new RowDefinitions("Auto,Auto,Auto");
   }
   body.Height=double.NaN;
   editor.Height=compact?Math.Clamp(scroll.Bounds.Height*.36,220,400):Math.Max(360,scroll.Bounds.Height);
   double previewWidth=Math.Max(200,(preview.Bounds.Width>0?preview.Bounds.Width:420)-26);
   var size=Surface.CanvasSize;
   viewport.Height=Math.Clamp(previewWidth*size.Height/Math.Max(1.0,size.Width),160,420);
   UpdatePreviewGeometry();
  }finally{updatingLayout=false;}
 }
}
