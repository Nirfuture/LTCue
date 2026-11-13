using LtcPlayer.App;
using Avalonia.Controls;
using Avalonia.Interactivity;
namespace LtcPlayer.Avalonia;
public partial class MainWindow
{
 readonly PlaybackCoordinator playback;
 internal SharedAudioSnapshot SharedAudio=>playback.Audio;
 sealed record AudioChoice(string Id,string Label){public override string ToString()=>Label;}
 bool audioUiReady,audioChanging,audioRequested;
 async Task InitializeAudioUiAsync()
 {
  await Preview.UseSharedAudioAsync();
  Control<Slider>("Volume").Value=settings.AudioVolume;
  await SetSharedAudioAsync(false,silenceForVerification:Program.Option("--verify-timing-status")!=null||Program.Option("--verify-audio-ui")!=null||Program.Option("--verify-preview-ui")!=null);
  audioUiReady=true;UpdateAudioUi();
 }
 async void AudioEnabledClick(object? sender,RoutedEventArgs e)=>await Safe(()=>ChangeAudioAsync(Control<global::Avalonia.Controls.Primitives.ToggleButton>("AudioEnabled").IsChecked==true));
 internal async Task ChangeAudioAsync(bool enabled)
 {
  if(audioChanging||closing)return;audioChanging=true;audioRequested=enabled;UpdateAudioUi();
  try{await SetSharedAudioAsync(enabled,settings.AudioDeviceId,(float)Control<Slider>("Volume").Value,false);}
  catch{audioRequested=false;throw;}
  finally{audioChanging=false;UpdateAudioUi();}
 }
 void UpdateAudioUi()
 {
  if(!audioUiReady)return;var s=SharedAudio;
  Control<global::Avalonia.Controls.Primitives.ToggleButton>("AudioEnabled").IsChecked=audioRequested;Control<global::Avalonia.Controls.Primitives.ToggleButton>("AudioEnabled").IsEnabled=!audioChanging&&!closing;
  Control<LucideIcon>("AudioPowerIcon").Kind=audioRequested?"volume-2":"volume-x";
  var action=audioRequested?"音声出力を停止":"音声出力を開始";ToolTip.SetTip(Control<global::Avalonia.Controls.Primitives.ToggleButton>("AudioEnabled"),action);global::Avalonia.Automation.AutomationProperties.SetName(Control<global::Avalonia.Controls.Primitives.ToggleButton>("AudioEnabled"),action);
  ToolTip.SetTip(Control<global::Avalonia.Controls.Primitives.ToggleButton>("AudioEnabled"),action+(s.Notice==""?"":"\n"+s.Notice));
  Control<TextBlock>("VolumePercent").Text=$"{Control<Slider>("Volume").Value:P0}";
 }

 internal Task SetSharedAudioAsync(bool enabled,string device="",float volume=.5f,bool muted=false,bool silenceForVerification=false)=>playback.ConfigureAudioAsync(enabled,device,volume,muted,silenceForVerification);
 internal Task DisposeSharedAudioAsync()=>playback.DisposeAudioAsync();
}
