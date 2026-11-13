using Avalonia.Controls;
using Avalonia.Input;
using LtcPlayer.App;
namespace LtcPlayer.Avalonia;
public partial class MainWindow
{
 bool revealingCueFile;
 void CueFilePointerPressed(object? sender,PointerPressedEventArgs e)
 {
  if(sender is not Control target||!e.GetCurrentPoint(target).Properties.IsRightButtonPressed)return;
  // Do not select/load a different cue merely to inspect its file location.
  e.Handled=true;OpenCueFileMenu(target);
 }
 void CueFileContextRequested(object? sender,ContextRequestedEventArgs e)
 {
  if(sender is not Control target)return;e.Handled=true;OpenCueFileMenu(target);
 }
 void OpenCueFileMenu(Control target)
 {
  if(target.ContextMenu?.IsOpen==true||target.DataContext is not CueRow cue)return;
  string path=cue.FilePath;
  var reveal=new MenuItem{Header="エクスプローラーで場所を開く",IsEnabled=!string.IsNullOrWhiteSpace(path)};
  reveal.Click+=async(_,_)=>await Safe(async()=>{
   if(revealingCueFile||closing)return;revealingCueFile=true;
   try{await FileLocation.RevealAsync(path);}finally{revealingCueFile=false;}
  });
  target.ContextMenu=new ContextMenu{ItemsSource=new[]{reveal}};target.ContextMenu.Open(target);
 }
}
