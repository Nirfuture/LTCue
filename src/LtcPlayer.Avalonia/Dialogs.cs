using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using LtcPlayer.App;
using System.Globalization;
namespace LtcPlayer.Avalonia;
public static class StopDialog
{
 public static Task<bool> ShowAsync(Window owner,bool exit)
 {
  var keep=new Button{Content="出力を続ける",IsDefault=true,IsCancel=true};var stop=new Button{Content=exit?"出力を停止して終了":"出力を停止"};
  var dialog=new Window{Title="出力停止の確認",Width=450,Height=180,CanResize=false,Topmost=true,WindowStartupLocation=WindowStartupLocation.CenterOwner,
   Content=new StackPanel{Margin=new Thickness(22),Spacing=20,Children={new TextBlock{Text=exit?"出力を停止して終了しますか？":"出力を停止しますか？"},new StackPanel{Orientation=Orientation.Horizontal,Spacing=12,HorizontalAlignment=HorizontalAlignment.Right,Children={keep,stop}}}}};
  keep.Click+=(_,_)=>dialog.Close(false);stop.Click+=(_,_)=>dialog.Close(true);dialog.Opened+=(_,_)=>keep.Focus();dialog.KeyDown+=(_,e)=>{if(e.Key==Key.Escape){e.Handled=true;dialog.Close(false);}};
  return dialog.ShowDialog<bool>(owner);
 }
}
public record SettingsResult(InputSettings Settings,OutputDisplay? Display,string? DisplayName=null);
internal sealed record OutputDisplayChoice(OutputDisplay? Display,string? Name,bool Connected)
{
 public override string ToString()=>Connected?Display!.Value.ToString():Name is {} name?name+"（未接続）":"未選択";
}
public sealed class SettingsDialog : Window
{
 public Func<SettingsResult,Task>? ApplySettings{get;set;}
 internal SettingsResult? LastAppliedResult{get;private set;}
 internal TextBlock ChangeStatus{get;}=new(){TextWrapping=global::Avalonia.Media.TextWrapping.Wrap};
 internal Button ApplyButton{get;}=new(){Content="適用"};
 internal ComboBox OutputSelector{get;}
 internal ComboBox ModeSelector{get;}
 internal ComboBox AudioOutputSelector{get;}=new(){HorizontalAlignment=HorizontalAlignment.Stretch};
 internal Button AudioRefreshButton{get;}=new(){Content=new LucideIcon{Kind="refresh-cw"},Padding=new Thickness(7)};
 internal Button InputRefreshButton{get;}=new(){Content=new LucideIcon{Kind="refresh-cw"},Padding=new Thickness(7)};
 internal InputPeakBar InputLevelBar{get;}=new(){Height=5,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,10,0)};
 internal TextBlock InputLevelText{get;}=new(){Text="入力なし",FontSize=12,VerticalAlignment=VerticalAlignment.Center};
 internal TextBlock InputReceptionText{get;}=new(){Text="LTC未受信",FontSize=12};
 readonly global::Avalonia.Threading.DispatcherTimer inputLevelTimer=new(){Interval=TimeSpan.FromMilliseconds(100)};
 internal bool InputLevelTimerRunning=>inputLevelTimer.IsEnabled;
 internal int InputLevelRefreshes{get;private set;}
 public SettingsDialog(InputSettings current,OutputDisplay? display,bool readOnly,bool audioEnabled=false,LtcMonitorService? monitor=null,string? displayName=null)
 {
  Title="設定";Width=680;Height=540;MinWidth=360;MinHeight=320;CanResize=true;Topmost=true;WindowStartupLocation=WindowStartupLocation.CenterOwner;
  var error=ChangeStatus;
  ToolTip.SetTip(InputRefreshButton,"LTC入力デバイス一覧を再読み込み");ToolTip.SetTip(AudioRefreshButton,"音声出力デバイス一覧を再読み込み");global::Avalonia.Automation.AutomationProperties.SetName(InputRefreshButton,"LTC入力デバイス一覧を再読み込み");global::Avalonia.Automation.AutomationProperties.SetName(AudioRefreshButton,"音声出力デバイス一覧を再読み込み");
  var input=new ComboBox{HorizontalAlignment=HorizontalAlignment.Stretch};
  void ReloadInput(string id){var items=AudioInputService.Devices().ToList();if(!items.Any(d=>d.Id==id))items.Add(new(){Id=id,Name=id==""?"未選択":current.DeviceName+"（未接続）"});input.ItemsSource=items;input.SelectedItem=items.First(d=>d.Id==id);}
  void ReloadAudio(string id){var items=new List<AudioDevice>{new(){Id="",Name="システム既定"}};items.AddRange(AudioOutputSink.Devices().Select(d=>new AudioDevice{Id=d.Id,Name=d.Name}));if(!items.Any(d=>d.Id==id))items.Add(new(){Id=id,Name="未接続の音声出力先"});AudioOutputSelector.ItemsSource=items;AudioOutputSelector.SelectedItem=items.First(d=>d.Id==id);}
  try{ReloadInput(current.DeviceId);}catch(Exception e){error.Text=e.Message;input.ItemsSource=new[]{new AudioDevice{Id=current.DeviceId,Name=current.DeviceName}};input.SelectedIndex=0;}
  try{ReloadAudio(current.AudioDeviceId);}catch(Exception e){error.Text=e.Message;AudioOutputSelector.ItemsSource=new[]{new AudioDevice{Id=current.AudioDeviceId,Name="保存済みの音声出力先"}};AudioOutputSelector.SelectedIndex=0;}
  InputRefreshButton.Click+=(_,_)=>{try{ReloadInput((input.SelectedItem as AudioDevice?)?.Id??current.DeviceId);}catch(Exception e){error.Text=e.Message;}};
  AudioRefreshButton.Click+=(_,_)=>{try{ReloadAudio((AudioOutputSelector.SelectedItem as AudioDevice?)?.Id??current.AudioDeviceId);}catch(Exception e){error.Text=e.Message;}};
  displayName??=display?.Name;
  var choices=VideoRenderer.Displays().Select(d=>new OutputDisplayChoice(d,d.Name,true)).ToList();
  var selected=choices.FirstOrDefault(d=>d.Name==displayName);
  if(selected==null){selected=new(display,displayName,false);choices.Insert(0,selected);}
  var output=OutputSelector=new ComboBox{ItemsSource=choices,HorizontalAlignment=HorizontalAlignment.Stretch,SelectedItem=selected};
  ModeSelector=new ComboBox{Name="OutputMode",ItemsSource=new[]{OutputPresentation.Label(OutputPresentationMode.Standard),OutputPresentation.Label(OutputPresentationMode.Compatibility)},SelectedIndex=(int)current.OutputMode,HorizontalAlignment=HorizontalAlignment.Stretch};
  var channel=new ComboBox{ItemsSource=new[]{"L","R"},SelectedIndex=current.Channel};var rate=new ComboBox{ItemsSource=TimecodeRate.Modes,SelectedItem=TimecodeRate.Modes.First(r=>r.Name==current.Rate)};
  TextBox Box(double value)=>new(){Text=value.ToString(CultureInfo.InvariantCulture)};
  var offset=Box(current.Offset);var freewheel=Box(current.Freewheel);var resync=Box(current.Resync);
  foreach(var control in new Control[]{input,channel,rate,offset,freewheel,resync,output,ModeSelector})control.IsEnabled=!readOnly;
  var fields=new StackPanel{Spacing=18};
  StackPanel Section(string title,bool enabled){var section=new StackPanel{Spacing=8,IsEnabled=enabled};section.Children.Add(new TextBlock{Text=title,FontSize=16,FontWeight=global::Avalonia.Media.FontWeight.SemiBold});fields.Children.Add(section);return section;}
  var labels=new Dictionary<Control,TextBlock>();
  void Add(StackPanel section,string label,Control control){var row=new Grid{ColumnDefinitions=new ColumnDefinitions("180,*")};var title=new TextBlock{Text=label,VerticalAlignment=VerticalAlignment.Center,TextWrapping=global::Avalonia.Media.TextWrapping.Wrap,Margin=new Thickness(0,0,12,0)};row.Children.Add(title);labels[control]=title;if(control is Grid deviceRow)foreach(var child in deviceRow.Children)labels[child]=title;Grid.SetColumn(control,1);row.Children.Add(control);section.Children.Add(row);}
  Grid DeviceRow(Control selector,Button refresh){var grid=new Grid{ColumnDefinitions=new ColumnDefinitions("*,Auto")};grid.Children.Add(selector);Grid.SetColumn(refresh,1);refresh.Margin=new Thickness(8,0,0,0);grid.Children.Add(refresh);return grid;}
  var ltc=Section("LTC入力",!readOnly);Add(ltc,"デバイス",DeviceRow(input,InputRefreshButton));Add(ltc,"チャンネル",channel);Add(ltc,"形式",rate);Add(ltc,"補正（フレーム）",offset);Add(ltc,"未受信時の補間（秒）",freewheel);Add(ltc,"再同期閾値（フレーム）",resync);
  AudioOutputSelector.IsEnabled=!audioEnabled;AudioRefreshButton.IsEnabled=!audioEnabled;InputRefreshButton.IsEnabled=!readOnly;
  var audio=Section("音声出力",!audioEnabled);Add(audio,"デバイス",DeviceRow(AudioOutputSelector,AudioRefreshButton));
  if(audioEnabled)audio.Children.Add(new TextBlock{Text="音声出力先を変更するには、音声出力をOFFにしてください。",TextWrapping=global::Avalonia.Media.TextWrapping.Wrap});
  var video=Section("映像出力",!readOnly);Add(video,"ディスプレイ",output);Add(video,"表示方式",ModeSelector);
  video.Children.Add(new TextBlock{Text="通常は標準（Flip）を使用します。全画面出力が不安定な場合は互換（BitBlt）を選んでください。",TextWrapping=global::Avalonia.Media.TextWrapping.Wrap,Foreground=global::Avalonia.Media.Brush.Parse("#A5B0BF"),Margin=new Thickness(0,6,0,0)});
  fields.Children.Clear();ltc.Children.RemoveAt(0);audio.Children.RemoveAt(0);video.Children.RemoveAt(0);
  var tabs=new TabControl{ItemsSource=new[]{new TabItem{FontSize=14,Header="LTC入力",Content=ltc},new TabItem{FontSize=14,Header="音声出力",Content=audio},new TabItem{FontSize=14,Header="映像出力",Content=video}},HorizontalContentAlignment=HorizontalAlignment.Stretch};
  ltc.Margin=audio.Margin=video.Margin=new Thickness(0,16,0,0);
  var meterRows=new StackPanel{Spacing=5};
  var meterRow=new Grid{ColumnDefinitions=new ColumnDefinitions("180,*,Auto")};meterRow.Children.Add(new TextBlock{Text="入力ピーク",VerticalAlignment=VerticalAlignment.Center});Grid.SetColumn(InputLevelBar,1);meterRow.Children.Add(InputLevelBar);Grid.SetColumn(InputLevelText,2);meterRow.Children.Add(InputLevelText);meterRows.Children.Add(meterRow);
  var receptionRow=new Grid{ColumnDefinitions=new ColumnDefinitions("180,*")};Grid.SetColumn(InputReceptionText,1);receptionRow.Children.Add(InputReceptionText);meterRows.Children.Add(receptionRow);
  var pendingInput=new TextBlock{Text="未適用の選択です。入力レベルは適用中の入力を表示しています。",IsVisible=false,FontSize=12,TextWrapping=global::Avalonia.Media.TextWrapping.Wrap,Foreground=global::Avalonia.Media.Brush.Parse("#A5B0BF")};meterRows.Children.Add(pendingInput);
  input.SelectionChanged+=(_,_)=>pendingInput.IsVisible=(input.SelectedItem is AudioDevice item&&item.Id!=current.DeviceId)||channel.SelectedIndex!=current.Channel;
  channel.SelectionChanged+=(_,_)=>pendingInput.IsVisible=(input.SelectedItem is AudioDevice item&&item.Id!=current.DeviceId)||channel.SelectedIndex!=current.Channel;
  // Keep monitoring outside the disabled settings subtree during video output.
  ((TabItem)tabs.Items[0]!).Content=null;
  var inputTab=new StackPanel{Spacing=12,Children={ltc,meterRows}};
  ((TabItem)tabs.Items[0]!).Content=inputTab;
  void UpdateInputLevel(){
   InputLevelRefreshes++;var value=monitor?.InputLevel??default;
   InputLevelBar.Value=value.HasSamples?Math.Clamp(value.Dbfs+60,0,60):0;
   InputLevelBar.Foreground=global::Avalonia.Media.Brush.Parse(value.NearLimit?"#E6B566":"#55B2F6");
   InputLevelText.Text=!value.HasSamples?"入力なし":double.IsNegativeInfinity(value.Dbfs)?"−∞ dBFS":$"{value.Dbfs:F1} dBFS"+(value.NearLimit?" · 上限付近":"");
   var state=monitor?.Clock.Read().State??SyncState.WAITING;
   InputReceptionText.Text=state switch{SyncState.LOCKED or SyncState.RESYNC=>"LTC受信中",SyncState.FREEWHEEL=>"LTC補間中",SyncState.ERROR=>"LTC入力エラー",_=>"LTC未受信"};
   InputReceptionText.Foreground=global::Avalonia.Media.Brush.Parse(state is SyncState.LOCKED or SyncState.RESYNC?"#80D5B1":"#A5B0BF");
  }
  inputLevelTimer.Tick+=(_,_)=>UpdateInputLevel();Opened+=(_,_)=>{UpdateInputLevel();if(monitor!=null)inputLevelTimer.Start();};Closed+=(_,_)=>inputLevelTimer.Stop();
  fields.Children.Add(tabs);
  var apply=ApplyButton;var back=new Button{Content="閉じる",IsCancel=true};bool applying=false;OutputDisplay? appliedDisplay=selected.Display;string? appliedDisplayName=selected.Name;
  var buttons=new StackPanel{Orientation=Orientation.Horizontal,Spacing=10,HorizontalAlignment=HorizontalAlignment.Right,Children={back,apply}};
  var layout=new Grid{Margin=new Thickness(20),RowDefinitions=new RowDefinitions("Auto,*,Auto,Auto")};
  var hint=new TextBlock{Text="映像出力中はLTC入力と映像出力の設定を変更できません。",IsVisible=readOnly,Margin=new Thickness(0,0,0,10),TextWrapping=global::Avalonia.Media.TextWrapping.Wrap};layout.Children.Add(hint);
  var scroll=new ScrollViewer{Content=fields,HorizontalScrollBarVisibility=global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,VerticalScrollBarVisibility=global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto};Grid.SetRow(scroll,1);layout.Children.Add(scroll);Grid.SetRow(error,2);error.Margin=new Thickness(0,8,0,0);layout.Children.Add(error);Grid.SetRow(buttons,3);buttons.Margin=new Thickness(0,12,0,0);layout.Children.Add(buttons);Content=layout;
  var tracked=new List<(Control Control,Func<bool> Changed)>();
  bool NumericChanged(TextBox box,double original)=>!double.TryParse(box.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out var value)||value!=original;
  void Track(Control control,Func<bool> changed){tracked.Add((control,changed));control.PropertyChanged+=(_,e)=>{if(e.Property==TextBox.TextProperty||e.Property==global::Avalonia.Controls.Primitives.SelectingItemsControl.SelectedItemProperty||e.Property==global::Avalonia.Controls.Primitives.SelectingItemsControl.SelectedIndexProperty)UpdateDirty(true);};}
  void UpdateDirty(bool edited=false){
   bool dirty=false;
   foreach(var item in tracked){bool changed=item.Changed();dirty|=changed;if(item.Control is global::Avalonia.Controls.Primitives.TemplatedControl field){if(changed)field.BorderBrush=global::Avalonia.Media.Brush.Parse("#E6B566");else field.ClearValue(global::Avalonia.Controls.Primitives.TemplatedControl.BorderBrushProperty);}if(labels.TryGetValue(item.Control,out var title)){title.FontWeight=changed?global::Avalonia.Media.FontWeight.SemiBold:global::Avalonia.Media.FontWeight.Normal;if(changed)title.Foreground=global::Avalonia.Media.Brush.Parse("#E6B566");else title.ClearValue(TextBlock.ForegroundProperty);}}
   var tabFields=new[]{new Control[]{input,channel,rate,offset,freewheel,resync},new Control[]{AudioOutputSelector},new Control[]{output,ModeSelector}};var tabNames=new[]{"LTC入力","音声出力","映像出力"};
   for(int index=0;index<3;index++){bool changed=tracked.Any(item=>tabFields[index].Contains(item.Control)&&item.Changed());((TabItem)tabs.Items[index]!).Header=tabNames[index]+(changed?" ●":"");}
   pendingInput.IsVisible=(input.SelectedItem is AudioDevice device&&device.Id!=current.DeviceId)||channel.SelectedIndex!=current.Channel;
   apply.IsEnabled=dirty&&!applying&&(!readOnly||!audioEnabled);
   if(dirty){error.Text="未適用の変更があります";error.Foreground=global::Avalonia.Media.Brush.Parse("#E6B566");}
   else if(edited){error.Text="";}
  }
  Track(input,()=>input.SelectedItem is not AudioDevice device||device.Id!=current.DeviceId);
  Track(channel,()=>channel.SelectedIndex!=current.Channel);Track(rate,()=>rate.SelectedItem is not TimecodeRate value||value.Name!=current.Rate);
  Track(offset,()=>NumericChanged(offset,current.Offset));Track(freewheel,()=>NumericChanged(freewheel,current.Freewheel));Track(resync,()=>NumericChanged(resync,current.Resync));
  Track(AudioOutputSelector,()=>AudioOutputSelector.SelectedItem is not AudioDevice device||device.Id!=current.AudioDeviceId);
  Track(output,()=>output.SelectedItem is not OutputDisplayChoice value||value.Name!=appliedDisplayName||!Nullable.Equals(value.Display,appliedDisplay));
  Track(ModeSelector,()=>ModeSelector.SelectedIndex!=(int)current.OutputMode);
  UpdateDirty();WindowPlacement.Track(this);back.Click+=(_,_)=>Close(null);Closing+=(_,e)=>{if(applying)e.Cancel=true;};
  apply.Click+=async(_,_)=>{
   if(applying)return;
   try{
    if(input.SelectedItem is not AudioDevice i||AudioOutputSelector.SelectedItem is not AudioDevice a||output.SelectedItem is not OutputDisplayChoice d||rate.SelectedItem is not TimecodeRate m)throw new InvalidDataException("入出力を選択してください。");
    var updated=current with{AudioDeviceId=a.Id,OutputMode=(OutputPresentationMode)ModeSelector.SelectedIndex,DeviceId=i.Id,DeviceName=i.Name,Channel=channel.SelectedIndex,Rate=m.Name,Offset=int.Parse(offset.Text!,CultureInfo.InvariantCulture),Freewheel=double.Parse(freewheel.Text!,CultureInfo.InvariantCulture),Resync=double.Parse(resync.Text!,CultureInfo.InvariantCulture)};
    OutputPresentation.Validate(updated.OutputMode);_=new MasterClock(m,updated.Freewheel,updated.Resync);
    var result=new SettingsResult(updated,d.Display,d.Name);applying=true;apply.IsEnabled=false;back.IsEnabled=false;fields.IsEnabled=false;
    if(ApplySettings!=null)await ApplySettings(result);
    current=updated;appliedDisplay=d.Display;appliedDisplayName=d.Name;LastAppliedResult=result;UpdateDirty();error.Text="適用しました";error.Foreground=global::Avalonia.Media.Brush.Parse("#80D5B1");
   }catch(Exception e){error.Text=e.Message;error.Foreground=global::Avalonia.Media.Brush.Parse("#E6B566");}
   finally{applying=false;back.IsEnabled=true;fields.IsEnabled=true;apply.IsEnabled=tracked.Any(item=>item.Changed())&&(!readOnly||!audioEnabled);}
  };
 }
}
