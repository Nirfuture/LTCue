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
 MaterialMonitor? materials;
 void StartMaterialMonitor(){
  materials=new MaterialMonitor(
   async()=>await Dispatcher.UIThread.InvokeAsync(()=>closing?Array.Empty<MaterialRequest>():Editor.Rows.Select(r=>new MaterialRequest(r.CueId,r.FilePath,r.SourceRevision,r.Duration>0,r.SourceError!="",r.MaterialStatusRevision)).ToArray()),
   async results=>await Dispatcher.UIThread.InvokeAsync(()=>{
    if(closing)return;bool changed=false;
    foreach(var result in results){
     var row=Editor.Rows.FirstOrDefault(r=>r.CueId==result.Request.Id&&r.FilePath==result.Request.Path&&r.SourceRevision==result.Request.Revision&&r.MaterialStatusRevision==result.Request.StatusRevision);
     if(row==null)continue;
     if(result.Info is {} info)row.AcceptMedia(info);else{Preview.Analysis.Invalidate(row.FilePath);row.SourceError=result.Error;}
     changed=true;
    }
    if(changed)Editor.Validate();
   }),()=>Preview.IsBusy||playback.Audio.State is SharedAudioState.Preparing or SharedAudioState.Resyncing,()=>new MediaDecoder(reuseAnalysis:true,indexCache:Preview.IndexCache,analysis:Preview.Analysis),Preview.Analysis.Invalidate);
 }

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
  playback.AudioMaterialFailed+=(source,error)=>Dispatcher.UIThread.Post(()=>{
   if(closing)return;
   var current=playback.ReadAudioPlayhead();
   if(current.SessionId!=source.SessionId||current.CueId!=source.CueId||current.Generation!=source.Generation||current.SourceRevision!=source.SourceRevision)return;
   foreach(var row in Editor.Rows.Where(r=>r.FilePath==source.Path&&(r.SourceRevision==source.SourceRevision||source.SourceRevision==0)))row.AudioIssue=error;
   UpdateProgramSummary();
  });
  playback.MaterialFailed+=failure=>Dispatcher.UIThread.Post(()=>{
   if(closing||failure.SessionId!=playback.SynchronizationSession||!Editor.Rows.Any(r=>r.CueId==failure.Cue.CueId&&r.FilePath==failure.Cue.FilePath&&r.SourceRevision==failure.Cue.SourceRevision))return;
   Preview.Analysis.Invalidate(failure.Cue.FilePath);
   foreach(var row in Editor.Rows.Where(r=>r.FilePath==failure.Cue.FilePath))row.SourceError=failure.Reason;
   Editor.Validate();
  });
  Editor.PlaybackChanged+=()=>programRevision++;
  coverTimer.Tick+=(_,_)=>{if(presentationReady&&!closing)try{Surface.RefreshCover();if(outputSessionActive&&Surface.Canvas.PollStopRequest())_ = Safe(async()=>{await RequestStopAsync(false);});}catch(Exception e){if(Surface.Ready&&Surface.Canvas.SharedDeviceError<0)_ = Safe(HandleSharedGraphicsFailureAsync);else _ = Safe(async()=>{if(outputSessionActive)await DisarmAsync();StatusMessage(e.Message);});}UpdateCoverLabel();};
  Control<NumericUpDown>("FadeSeconds").ValueChanged+=(_,_)=>{if(!presentationReady||applyingSettings)return;var value=Control<NumericUpDown>("FadeSeconds").Value;if(value is {} seconds){settings=settings with{FadeSeconds=(double)seconds};try{SaveSettings();}catch(Exception e){StatusMessage(e.Message);}}};
  coverTimer.Start();
 }
 async Task LoadPresentationSettingsAsync(){
  var display=selectedDisplay;Surface.ConfigureCanvas(display?.Width??1920,display?.Height??1080);UpdateResponsiveLayout();
  try{cover.Image=await CoverImageLoader.LoadAsync(settings.CoverPath);}catch(Exception e){cover.Image=null;coverLoadWarning=e.Message;StatusMessage("蓋絵の読込失敗（黒で表示）："+e.Message);}
  Control<NumericUpDown>("FadeSeconds").Value=(decimal)settings.FadeSeconds;
 }
 internal void SetCoverVisible(bool value){cover.Set(value,settings.FadeSeconds);UpdateCoverLabel();}
 internal void SetFadeSeconds(decimal seconds){Control<NumericUpDown>("FadeSeconds").Value=Math.Clamp(seconds,0,60);}
 void FadePresetClick(object? sender,RoutedEventArgs e){if(sender is Button{Tag:string value}&&decimal.TryParse(value,System.Globalization.NumberStyles.Number,System.Globalization.CultureInfo.InvariantCulture,out var seconds))SetFadeSeconds(seconds);}
 void FadeSecondsLostFocus(object? sender,RoutedEventArgs e){if(Control<NumericUpDown>("FadeSeconds").Value==null)SetFadeSeconds((decimal)settings.FadeSeconds);}
 void CoverClick(object? sender,RoutedEventArgs e)=>SetCoverVisible(Control<ToggleButton>("CoverButton").IsChecked==true);
 void UpdateCoverLabel(){
  var button=Control<ToggleButton>("CoverButton");button.IsChecked=cover.IsCovered;
  Control<TextBlock>("CoverActionLabel").Text=cover.IsCovered?"非表示":"表示";
  var name=Control<TextBlock>("CoverName");name.Text=settings.CoverPath==""?"画像なし":Path.GetFileName(settings.CoverPath);ToolTip.SetTip(name,coverLoadWarning!=""?coverLoadWarning:settings.CoverPath);
  Control<Button>("ChooseCoverButton").IsEnabled=CanChangeCover;
  Control<Button>("ClearCoverButton").IsEnabled=CanChangeCover&&settings.CoverPath!="";
  ToolTip.SetTip(Control<Button>("ChooseCoverButton"),CanChangeCover?"蓋絵を変更":CoverChangeAccess.Reason);
  ToolTip.SetTip(Control<Button>("ClearCoverButton"),CanChangeCover?"蓋絵を解除":CoverChangeAccess.Reason);
  ToolTip.SetTip(button,cover.Image==null?(coverLoadWarning!=""?"蓋絵の読込失敗（黒で表示）："+coverLoadWarning:"画像未登録：黒で表示"):cover.Image.Path);
 }
 void OnPlaybackFailed(Exception e){if(Surface.Ready&&Surface.Canvas.SharedDeviceError<0)_ = Safe(HandleSharedGraphicsFailureAsync);else _ = Safe(async()=>{if(outputSessionActive)await DisarmAsync();StatusMessage("同期再生エラー："+e.Message);});}
 async Task UpdateAutoPreviewAsync(){
  if(!presentationReady||closing||closePending||busy||!Surface.Ready||Surface.Canvas.SharedDeviceError<0||MasterClock.Timestamp()<playback.RetryAt)return;
  try{
   var size=Surface.CanvasSize;
   await playback.ReconcileAsync(programRevision,Editor.CanArm,()=>Editor.Timeline.ToList(),new OutputDisplay{Width=size.Width,Height=size.Height},Editor.Rate.FramesToSeconds(settings.Offset));
   UpdateEnabled();
  }catch(Exception e){playback.DeferRetry();StatusMessage("同期エラー："+e.Message);await StopSyncPreviewAsync();}
 }
#if VERIFICATION
 internal Task StopSynchronizationForTest()=>StopSyncPreviewAsync();
#endif
 internal async Task StopSyncPreviewAsync(){await playback.StopSynchronizationAsync();Control<TextBlock>("RunInfo").Text="";UpdateEnabled();}
}
