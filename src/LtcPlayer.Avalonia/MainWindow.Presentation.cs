using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.Platform.Storage;
using LtcPlayer.App;
namespace LtcPlayer.Avalonia;
public partial class MainWindow
{
 readonly CoverController cover=new();
 readonly DispatcherTimer coverTimer=new(){Interval=TimeSpan.FromMilliseconds(16)};
 int programRevision;bool presentationReady;string coverLoadWarning="";
 internal PlaybackCoordinator Coordinator=>playback;
 internal Guid? SynchronizationSession=>playback.SynchronizationSession;
 bool SyncOwnsSurface=>playback.OwnsSurface;
 internal bool LtcHasPriority=>playback.LtcHasPriority;
 internal ProgramObservation? PreviewObservation=>playback.Observation;
 internal CoverController Cover=>cover;
 void InitializePresentation(){
  Surface.Cover=cover;
  Editor.PlaybackChanged+=()=>programRevision++;
  coverTimer.Tick+=(_,_)=>{if(presentationReady&&!closing)try{Surface.RefreshCover();if(armed&&Surface.Canvas.PollStopRequest())_ = Safe(async()=>{await RequestStopAsync(false);});}catch(Exception e){if(Surface.Ready&&Surface.Canvas.SharedDeviceError<0)_ = Safe(HandleSharedGraphicsFailureAsync);else _ = Safe(async()=>{if(armed)await DisarmAsync();StatusMessage(e.Message);});}UpdateCoverLabel();};
  Control<NumericUpDown>("FadeSeconds").ValueChanged+=(_,_)=>{if(!presentationReady||applyingSettings)return;var value=Control<NumericUpDown>("FadeSeconds").Value;if(value is {} seconds){settings=settings with{FadeSeconds=(double)seconds};try{SaveSettings();}catch(Exception e){StatusMessage(e.Message);}}};
  coverTimer.Start();
 }
 async Task LoadPresentationSettingsAsync(){
  var display=selectedDisplay;Surface.ConfigureCanvas(display?.Width??1920,display?.Height??1080);UpdateResponsiveLayout();
  try{cover.Image=await CoverImageLoader.LoadAsync(settings.CoverPath);}catch(Exception e){cover.Image=null;coverLoadWarning=e.Message;StatusMessage("蓋画を読み込めません。黒を使用します："+e.Message);}
  Control<NumericUpDown>("FadeSeconds").Value=(decimal)settings.FadeSeconds;
 }
 bool choosingCover;
 bool CanChangeCover=>presentationReady&&!armed&&!busy&&!closing&&!closePending&&!choosingCover;
 async void ChooseCoverClick(object? sender,RoutedEventArgs e){
  if(!CanChangeCover)return;
  choosingCover=true;UpdateCoverLabel();
  try{
   var files=await StorageProvider.OpenFilePickerAsync(new(){Title="蓋画を登録",AllowMultiple=false,FileTypeFilter=[new("PNG・JPEG"){Patterns=["*.png","*.jpg","*.jpeg"]}]});
   if(files.FirstOrDefault()?.TryGetLocalPath() is not {} path)return;
   var image=await CoverImageLoader.LoadAsync(path);
   if(armed||busy||closing||closePending)return;
   SaveCover(path,image);StatusMessage("蓋画を登録しました。");
  }catch(Exception ex){StatusMessage(ex.Message);}
  finally{choosingCover=false;UpdateCoverLabel();}
 }
 void ClearCoverClick(object? sender,RoutedEventArgs e){
  if(!CanChangeCover)return;
  try{SaveCover("",null);StatusMessage("蓋画を解除しました。黒を使用します。");}
  catch(Exception ex){StatusMessage(ex.Message);}
 }
 void SaveCover(string path,CoverImage? image){
  var previous=settings;settings=settings with{CoverPath=path};
  try{SaveSettings();}catch{settings=previous;throw;}
  cover.Image=image;coverLoadWarning="";UpdateCoverLabel();
 }
 internal void SetCoverVisible(bool value){cover.Set(value,settings.FadeSeconds);UpdateCoverLabel();}
 internal void SetFadeSeconds(decimal seconds){Control<NumericUpDown>("FadeSeconds").Value=Math.Clamp(seconds,0,60);}
 void FadePresetClick(object? sender,RoutedEventArgs e){if(sender is Button{Tag:string value}&&decimal.TryParse(value,System.Globalization.NumberStyles.Number,System.Globalization.CultureInfo.InvariantCulture,out var seconds))SetFadeSeconds(seconds);}
 void FadeSecondsLostFocus(object? sender,RoutedEventArgs e){if(Control<NumericUpDown>("FadeSeconds").Value==null)SetFadeSeconds((decimal)settings.FadeSeconds);}
 void CoverClick(object? sender,RoutedEventArgs e)=>SetCoverVisible(Control<ToggleButton>("CoverButton").IsChecked==true);
 void UpdateCoverLabel(){
  var button=Control<ToggleButton>("CoverButton");button.IsChecked=cover.IsCovered;
  Control<TextBlock>("CoverActionLabel").Text=cover.IsCovered?"映像に戻す":"蓋絵を表示";
  var name=Control<TextBlock>("CoverName");name.Text=settings.CoverPath==""?"未登録（黒）":Path.GetFileName(settings.CoverPath);ToolTip.SetTip(name,coverLoadWarning!=""?coverLoadWarning:settings.CoverPath);
  Control<Button>("ChooseCoverButton").IsEnabled=CanChangeCover;
  Control<Button>("ClearCoverButton").IsEnabled=CanChangeCover&&settings.CoverPath!="";
  Control<TextBlock>("CoverStatus").Text=cover.Transitioning?(cover.IsCovered?"蓋画へフェード中":"映像へフェード中"):cover.IsCovered?(cover.Image==null?"蓋画表示中（黒）":"蓋画表示中"):"映像表示中";
  ToolTip.SetTip(button,cover.Image==null?(coverLoadWarning!=""?"画像を読み込めないため黒を使用します："+coverLoadWarning:"未登録のため黒へ切り替えます"):cover.Image.Path);
 }
 void OnPlaybackFailed(Exception e){if(Surface.Ready&&Surface.Canvas.SharedDeviceError<0)_ = Safe(HandleSharedGraphicsFailureAsync);else _ = Safe(async()=>{if(armed)await DisarmAsync();StatusMessage("同期再生エラー："+e.Message);});}
 async Task UpdateAutoPreviewAsync(){
  if(!presentationReady||closing||closePending||busy||!Surface.Ready||Surface.Canvas.SharedDeviceError<0||MasterClock.Timestamp()<playback.RetryAt)return;
  try{
   var size=Surface.CanvasSize;
   await playback.ReconcileAsync(programRevision,Editor.CanArm,()=>ShowProgramService.Validate(Editor.Rows,Editor.Rate),new OutputDisplay{Width=size.Width,Height=size.Height},Editor.Rate.FramesToSeconds(settings.Offset));
   UpdateEnabled();
  }catch(Exception e){playback.DeferRetry();StatusMessage("同期プレビュー："+e.Message);await StopSyncPreviewAsync();}
 }
 internal async Task StopSyncPreviewAsync(){await playback.StopSynchronizationAsync();Control<TextBlock>("RunInfo").Text="";UpdateEnabled();}
}
