using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using LtcPlayer.App;

namespace LtcPlayer.Avalonia;

public partial class MainWindow
{
 bool formatDialogOpen;
 void ApplyCueFormat()
 {
  foreach(var row in Editor.Rows)row.FormatPreferences=settings.CueFormat;
  Grid.Columns.Single(c=>Equals(c.Tag,"resolution")).Width=new DataGridLength(ResolutionColumnWidth());
 }
 double ResolutionColumnWidth()
 {
  if(settings.CueFormat.Mode==CueResolutionMode.Dimensions)return 150;
  double Measure(string text,double fontSize)
  {
   var label=new TextBlock{Text=text,FontFamily=Grid.FontFamily,FontSize=fontSize,
    FontWeight=Grid.FontWeight,FontStyle=Grid.FontStyle,FontStretch=Grid.FontStretch};
   label.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));
   return label.DesiredSize.Width;
  }
  // Header padding, the menu button, and breathing room remain visible even with no presets.
  double minimum=Measure("解像度",12)+12+26+8;
  double longest=settings.CueFormat.Presets.Select(p=>p.Name).Append("未登録")
   .Max(name=>Measure(name,Grid.FontSize));
  // Account for cell padding and a little room on either side of the label.
  return Math.Ceiling(Math.Max(minimum,longest+24));
 }
 async void FormatMenuClick(object? sender,RoutedEventArgs e)
 {
  if(sender is not Button button||formatDialogOpen)return;
  formatDialogOpen=true;
  try{
   var dialog=new CueFormatDialog(settings.CueFormat,preferences=>{
    var next=settings with{CueFormat=preferences};
    using var pending=new PendingSettings(Program.Option("--settings")??InputSettings.PathName,next,selectedDisplayName);
    pending.Commit();settings=next;ApplyCueFormat();
   });
   await dialog.ShowDialog(this);
  }
  finally{formatDialogOpen=false;button.Focus();}
 }
}
