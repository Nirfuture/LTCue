using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using LtcPlayer.App;

namespace LtcPlayer.Avalonia;

internal sealed class CueFormatDialog : Window
{
 internal CueFormatDialog(CueFormatPreferences current,Action<CueFormatPreferences> save)
 {
  Title="解像度の表示設定";
  Width=610;Height=540;MinWidth=560;MinHeight=420;Topmost=true;
  WindowStartupLocation=WindowStartupLocation.CenterOwner;
  Background=ThemePalette.PanelBackground;
  var mode=new ComboBox{ItemsSource=new[]{"プリセット名","解像度"},SelectedIndex=(int)current.Mode,HorizontalAlignment=HorizontalAlignment.Stretch};
  AutomationProperties.SetName(mode,"解像度の表示");
  var rows=new StackPanel{Spacing=8};
  var inputs=new List<(TextBox Width,TextBox Height,TextBox Name)>();
  const string columns="100,24,100,*,70";
  Grid PresetGrid()=>new(){ColumnDefinitions=new ColumnDefinitions(columns),ColumnSpacing=8};
  void AddRow(string width,string height,string name)
  {
   var w=new TextBox{Text=width,Watermark="幅",HorizontalAlignment=HorizontalAlignment.Stretch};
   var h=new TextBox{Text=height,Watermark="高さ",HorizontalAlignment=HorizontalAlignment.Stretch};
   var n=new TextBox{Text=name,Watermark="表示名",HorizontalAlignment=HorizontalAlignment.Stretch};
   AutomationProperties.SetName(w,"幅（px）");AutomationProperties.SetName(h,"高さ（px）");AutomationProperties.SetName(n,"表示名");
   var line=PresetGrid();
   var cross=new TextBlock{Text="×",VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center};
   var remove=new Button{Content="削除",HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Center};
   var entry=(w,h,n);inputs.Add(entry);
   remove.Click+=(_,_)=>{inputs.Remove(entry);rows.Children.Remove(line);};
   Control[] controls=[w,cross,h,n,remove];
   for(int i=0;i<controls.Length;i++){Grid.SetColumn(controls[i],i);line.Children.Add(controls[i]);}
   rows.Children.Add(line);
  }
  foreach(var p in current.Presets)AddRow(p.Width.ToString(),p.Height.ToString(),p.Name);
  var header=PresetGrid();
  void Heading(string text,int column){var label=new TextBlock{Text=text,Foreground=ThemePalette.SecondaryText};Grid.SetColumn(label,column);header.Children.Add(label);}
  Heading("幅（px）",0);Heading("高さ（px）",2);Heading("表示名",3);
  var top=new StackPanel{Spacing=12};
  top.Children.Add(TimecodeEditDialog.Field("解像度の表示",mode));
  top.Children.Add(new TextBlock{Text="解像度プリセット",FontWeight=FontWeight.SemiBold,Margin=new Thickness(0,8,0,0)});
  top.Children.Add(header);
  // Reserve space for the vertical scrollbar in both the heading and editable rows.
  header.Margin=new Thickness(0,0,20,0);
  var scroll=new ScrollViewer{Content=rows,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
  rows.Margin=new Thickness(0,0,20,0);
  var error=new TextBlock{TextWrapping=TextWrapping.Wrap,Foreground=ThemePalette.ErrorText};
  var add=new Button{Content="プリセットを追加"};
  add.Click+=(_,_)=>{AddRow("","","");scroll.ScrollToEnd();inputs[^1].Width.Focus();};
  var apply=new Button{Content="適用",IsDefault=true};
  var cancel=new Button{Content="キャンセル",IsCancel=true};
  cancel.Click+=(_,_)=>Close();
  apply.Click+=(_,_)=>{
   try{
    var presets=inputs.Select((v,i)=>{
     if(!int.TryParse(v.Width.Text,out int w)||w<=0||!int.TryParse(v.Height.Text,out int h)||h<=0)
      throw new InvalidDataException($"プリセット {i+1}行目：幅・高さは正の整数を入力してください");
     return new ResolutionPreset(w,h,(v.Name.Text??"").Trim());
    }).ToArray();
    var preferences=new CueFormatPreferences{Mode=(CueResolutionMode)mode.SelectedIndex,Presets=presets};
    preferences.Validate();save(preferences);Close();
   }catch(Exception ex){error.Text=ex.Message;}
  };
  var bottom=new StackPanel{Spacing=12,Margin=new Thickness(0,12,0,0)};
  bottom.Children.Add(add);bottom.Children.Add(error);
  bottom.Children.Add(new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Spacing=8,Children={cancel,apply}});
  var layout=new Grid{RowDefinitions=new RowDefinitions("Auto,*,Auto"),Margin=new Thickness(20)};
  top.Margin=new Thickness(0,0,0,8);layout.Children.Add(top);
  Grid.SetRow(scroll,1);layout.Children.Add(scroll);
  Grid.SetRow(bottom,2);layout.Children.Add(bottom);Content=layout;
  KeyDown+=(_,e)=>{if(e.Key==Key.Escape){e.Handled=true;Close();}};
  WindowPlacement.Track(this);
 }
}
