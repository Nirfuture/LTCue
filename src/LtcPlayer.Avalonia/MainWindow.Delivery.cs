using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LtcPlayer.App;
using System.ComponentModel;
namespace LtcPlayer.Avalonia;
public enum DeliveryState{Stopped,Starting,Streaming,ConfirmingStop,Stopping}
public partial class MainWindow
{
 readonly LtcMonitorService monitor=new();OutputDisplay? selectedDisplay;string? selectedDisplayName;
 bool applyingSettings;
 void SaveSettings()
 {
  using var pending=new PendingSettings(Program.Option("--settings")??InputSettings.PathName,settings,selectedDisplayName);
  pending.Commit();
 }
#if VERIFICATION
 internal Action<string>? SettingsApplyStageForTest{get;set;}
 internal Func<OutputDisplay[]>? SettingsDisplaysForTest{get;set;}
#endif
 void LoadOutputSelection()
 {
  string file=Program.Option("--settings")??InputSettings.PathName;
  if(File.Exists(file)){using var doc=System.Text.Json.JsonDocument.Parse(File.ReadAllText(file));if(doc.RootElement.TryGetProperty("OutputDisplayName",out var name))selectedDisplayName=name.GetString();}
  var displays=VideoRenderer.Displays();selectedDisplay=selectedDisplayName==null?displays.FirstOrDefault():displays.Where(d=>d.Name==selectedDisplayName).Select(d=>(OutputDisplay?)d).FirstOrDefault();
 }
 internal bool CanManualOutput=>delivery==DeliveryState.Streaming&&playback.ManualAvailable;
 #if VERIFICATION
 internal Task PlayCueForTestAsync(CueRow row)=>PlayManualAsync(row,false);
#endif
 internal void PlayManual(CueRow row){if(ManualPlayAccess.Allowed)_ = Safe(()=>PlayManualAsync(row,false));}
 internal void StopManual(){if(ManualTransportAccess.Allowed)_ = Safe(()=>Preview.StopAsync());}
 async Task PlayManualAsync(CueRow row,bool restart){
  StartupMeasurementTrace.Mark("play_request");
  if(!ManualPlayAccess.Allowed)return;
  await StopSyncPreviewAsync();if(!ManualPlayAccess.Allowed)return;
  bool same=Preview.LoadedVersionMatches&&previewCue==row.CueId&&string.Equals(Preview.LoadedPath,row.FilePath,StringComparison.OrdinalIgnoreCase);
  if(!playback.UsesSharedAudio)Preview.AllowAudioForNextPlay();
  if(restart||!same){
   CancelScrub();
   string requestedPath=row.FilePath;
   bool Adopt(){
    if(!ManualControlAccess.Allowed||LtcHasPriority||Preview.IsSyncOwned||!Editor.Rows.Contains(row)||row.FilePath!=requestedPath)return false;
    previewCue=row.CueId;row.ClearSourceError();Editor.Validate();
    return ManualControlAccess.Allowed&&!LtcHasPriority&&!Preview.IsSyncOwned&&Editor.Rows.Contains(row)&&row.FilePath==requestedPath;
   }
   try{
    if(Path.GetExtension(requestedPath).Equals(".mp4",StringComparison.OrdinalIgnoreCase)){
     await Preview.LoadAndPlayAsync(requestedPath,Adopt);UpdateRowControls();return;
    }
    if(!await Preview.LoadAsync(requestedPath))return;previewCue=row.CueId;row.ClearSourceError();Editor.Validate();
   }
   catch(Exception e)when(e is IOException or InvalidDataException){Preview.Analysis.Invalidate(row.FilePath);row.SourceError=e.Message;Editor.Validate();throw;}
  }
  if(LtcHasPriority||Preview.IsSyncOwned){await UpdateAutoPreviewAsync();return;}
  if(!restart&&same&&Preview.Snapshot.State==PreviewState.Playing)await Preview.PauseAsync();else await Preview.PlayAsync();
  UpdateRowControls();
 }
 DeliveryState delivery;bool keyboardSeeking,closePending;Guid? previewCue;IPointer? seekPointer;
 OutputDiagnostics? outputDiagnostics;
 Task<bool>? stopRequest;
 internal Func<bool,Task<bool>>? ConfirmStopForTest {get;set;}
 internal DeliveryState Delivery=>delivery;
 internal LtcMonitorService Monitor=>monitor;
 void CancelScrub(){scrubbing=false;keyboardSeeking=false;ToolTip.SetIsOpen(Control<Slider>("Seek"),false);var pointer=seekPointer;seekPointer=null;pointer?.Capture(null);}
 void TickStatus()
 {
  UpdateAudioUi();
  if(outputSessionActive&&Surface.Ready&&Surface.Canvas.OutputDisconnected)_ = Safe(HandleOutputDisconnectedAsync);
  if(Surface.Ready&&Surface.Canvas.SharedDeviceError<0)_ = Safe(HandleSharedGraphicsFailureAsync);
  var clock=monitor.Clock.Read();Control<TextBlock>("LtcLabel").Text=clock.Label;
  Control<TextBlock>("LtcLabel").Opacity=clock.State is SyncState.LOST or SyncState.ERROR?.45:1;
  if(Surface.Ready){
   var preview=Preview.Snapshot;
   observation=SyncOwnsSurface?playback.Observation:null;
   if(observation==null){
    var manual=preview.FramePts.HasValue;
    observation=new(clock.State,clock.Label,manual?previewCue:null,null,preview.Position,preview.FramePts,preview.Presented,0,0,0,preview.Notice){Manual=manual,Holding=preview.State!=PreviewState.Playing};
   }
   observation=observation with{Monitor=Surface.Canvas.PreviewStatus(),OutputMode=Surface.Canvas.OutputAttached?Surface.Canvas.OutputMode:settings.OutputMode};
  }
  if(observation!=null)outputDiagnostics?.Observe(observation);
  var manualState=Preview.Snapshot;
  Control<TextBlock>("ModeLabel").Text="プレビュー";
  Control<TextBlock>("LtcStateLabel").Text=clock.State switch{SyncState.LOCKED or SyncState.RESYNC=>"LTC受信中",SyncState.FREEWHEEL=>"LTC補間中",SyncState.LOST=>"LTC未受信",SyncState.ERROR=>"LTC入力エラー",_=>"LTC待機"};
  Control<TextBlock>("LtcStateLabel").Foreground=clock.State is SyncState.LOCKED or SyncState.RESYNC?ThemePalette.Success:clock.State==SyncState.ERROR?ThemePalette.Warning:ThemePalette.SecondaryText;
  if(SyncOwnsSurface&&observation is {} t){
   var cue=Editor.Rows.FirstOrDefault(c=>c.CueId==t.CueId);
   Control<TextBlock>("PreviewName").Text=t.CueId.HasValue?CueName(t.CueId):"再生待機";
   SetPreviewTimes(t.FramePts,cue?.Duration,cue?.FrameRate??0);
   UpdateSeekPosition(t.FramePts??0,cue?.Duration??0);
   Control<TextBlock>("RunInfo").Text="LTC同期";
   ToolTip.SetTip(Control<TextBlock>("RunInfo"),null);
   UpdatePreviewNotice(t.Notice);
  }else{
   ToolTip.SetTip(Control<TextBlock>("RunInfo"),null);
   Control<TextBlock>("PreviewName").Text=manualState.Name==""?"未再生":manualState.Name;
   SetPreviewTimes(manualState.Position,manualState.Duration,Preview.FrameRate);
   Control<TextBlock>("RunInfo").Text=manualState.State switch{PreviewState.Playing=>"手動再生",PreviewState.Paused=>"一時停止",PreviewState.Stopped=>"停止",PreviewState.Ended=>"終了",_=>""};
   UpdatePreviewNotice(manualState.State==PreviewState.Error?manualState.Notice:"");
  }
  UpdateTimingStatus();UpdateOperationControls();UpdateRowControls();UpdatePreviewGeometry();
 }
 void UpdateDeliveryButton()
 {
  var button=Control<Button>("ArmButton");button.Content=delivery switch{DeliveryState.Starting=>"出力準備中",DeliveryState.Stopping=>"停止中",DeliveryState.Streaming or DeliveryState.ConfirmingStop=>"出力停止",_=>"出力開始"};
  button.Background=(delivery is DeliveryState.Streaming or DeliveryState.ConfirmingStop?ThemePalette.OutputActive:ThemePalette.OutputIdle);
  string reason=DeliveryBlockReason();
  button.IsEnabled=!busy&&!closing&&(delivery is DeliveryState.Streaming || delivery==DeliveryState.Stopped&&reason=="");
  ToolTip.SetTip(button,reason==""?(delivery==DeliveryState.Streaming?"出力停止":"出力開始"):reason);
  var hint=Control<TextBlock>("DeliveryHint");hint.IsVisible=delivery==DeliveryState.Stopped&&reason!="";hint.Text=hint.IsVisible?"出力できません："+reason:"";
  Control<TextBlock>("ModeLabel").Text="プレビュー";

  Control<Button>("SettingsButton").IsEnabled=!choosingCover&&!closing&&!closePending&&delivery is DeliveryState.Stopped or DeliveryState.Streaming;
  UpdateActivity();UpdatePreviewGeometry();
 }
 internal string DeliveryBlockReason()
 {
  if(closing)return "終了中";
  if(Surface.Ready&&Surface.Canvas.SharedDeviceError<0)return "GPU描画エラー。再起動してください";
  if(busy)return "読込・編集中";
  if(delivery!=DeliveryState.Stopped)return "";
  if(selectedDisplay is not {} display||!VideoRenderer.Displays().Any(d=>d.Name==display.Name&&d.Width==display.Width&&d.Height==display.Height))return "映像出力先が未選択です";
  return "";
 }
 void UpdateRowControls()
 {
  foreach(var separator in Grid.GetVisualDescendants().OfType<TextBlock>().Where(t=>t.Classes.Contains("tc-frame-separator")))separator.Text=Editor.Rate.Drop?";":":";
  foreach(var rowControl in Grid.GetVisualDescendants().OfType<DataGridRow>()){
   bool live=(outputSessionActive||SyncOwnsSurface)&&rowControl.DataContext is CueRow cue&&cue.CueId==observation?.CueId;
   rowControl.Background=live?ThemePalette.PlayingCue:Brushes.Transparent;
   foreach(string key in new[]{"DataGridRowSelectedBackgroundBrush","DataGridRowSelectedHoveredBackgroundBrush","DataGridRowSelectedUnfocusedBackgroundBrush","DataGridRowSelectedHoveredUnfocusedBackgroundBrush"}){
    if(live)rowControl.Resources[key]=rowControl.Background!;else rowControl.Resources.Remove(key);
   }
   ToolTip.SetTip(rowControl,live?(outputSessionActive?"出力中":"プレビュー中"):null);
  }
  foreach(var b in Grid.GetVisualDescendants().OfType<Button>())if(b.DataContext is CueRow row){
   bool current=previewCue==row.CueId;
   if(b.Classes.Contains("cue-play")){b.IsEnabled=ManualPlayAccess.Allowed;bool pause=!LtcHasPriority&&!SyncOwnsSurface&&current&&Preview.Snapshot.State==PreviewState.Playing;if(b.Tag is not bool wasPause||wasPause!=pause){b.Tag=pause;b.Content=new LucideIcon{Kind=pause?"pause":"play"};}ToolTip.SetTip(b,pause?"一時停止":current&&!LtcHasPriority&&!SyncOwnsSurface&&Preview.Snapshot.State==PreviewState.Paused?"再開":"再生");}
   if(b.Classes.Contains("cue-stop"))b.IsEnabled=current&&ManualTransportAccess.Allowed;
  }
 }
 async void RowPlayClick(object? sender,RoutedEventArgs e){if(sender is Button{DataContext:CueRow row})await Safe(()=>PlayManualAsync(row,false));}
 async void RowStopClick(object? sender,RoutedEventArgs e){if(sender is Button{DataContext:CueRow row}&&row.CueId==previewCue&&ManualTransportAccess.Allowed)await Safe(()=>Preview.StopAsync());}
 async void SettingsClick(object? sender,RoutedEventArgs e)
 {
  if(choosingCover||closing||closePending)return;
  await Safe(async()=>{
   var dialog=new SettingsDialog(settings,selectedDisplay,outputSessionActive,audioRequested,monitor,selectedDisplayName){ApplySettings=ApplySettingsAsync};
   await dialog.ShowDialog<SettingsResult?>(this);
  });
 }
 internal async Task ApplySettingsAsync(SettingsResult result)
 {
  if(busy||editingProgram||choosingCover||applyingSettings||closing||closePending)throw new InvalidOperationException("別の処理が実行中です");
  var old=settings;var next=result.Settings with{AudioVolume=old.AudioVolume};
  var oldDisplay=selectedDisplay;string? oldName=selectedDisplayName;
  var requested=result.Display;string? requestedName=result.DisplayName??requested?.Name;
  OutputPresentation.Validate(next.OutputMode);var mode=TimecodeRate.Modes.First(r=>r.Name==next.Rate);_=new MasterClock(mode,next.Freewheel,next.Resync);
  if(next.Channel is <0 or >1)throw new InvalidDataException("チャンネルはLまたはR");
  bool selectionChanged=requestedName!=oldName||!Nullable.Equals(requested,oldDisplay);
  if(selectionChanged&&requestedName!=null){
   var displays=VideoRenderer.Displays();
#if VERIFICATION
   displays=SettingsDisplaysForTest?.Invoke()??displays;
#endif
   if(requested is not {} output||!displays.Any(d=>d.Name==output.Name&&d.Width==output.Width&&d.Height==output.Height&&d.X==output.X&&d.Y==output.Y))throw new InvalidDataException("出力先が未接続です");
  }
  bool inputChanged=next.DeviceId!=old.DeviceId||next.Channel!=old.Channel||next.Rate!=old.Rate;
  bool policyChanged=next.Freewheel!=old.Freewheel||next.Resync!=old.Resync;
  var size=Surface.CanvasSize;
  bool canvasChanged=requested is {} display&&(size.Width!=display.Width||size.Height!=display.Height);
  bool videoChanged=selectionChanged||next.OutputMode!=old.OutputMode;
  bool offsetChanged=next.Offset!=old.Offset;
  if(outputSessionActive&&(videoChanged||canvasChanged))throw new InvalidOperationException("出力中は変更できません");
  if(audioRequested&&next.AudioDeviceId!=old.AudioDeviceId)throw new InvalidOperationException("音声出力中は変更できません");
  if(!double.IsFinite(next.FadeSeconds)||next.FadeSeconds<0||next.FadeSeconds>60)throw new InvalidDataException("フェードは0～60秒");
  var previousClock=monitor.Clock;bool inputAttempted=false;
  busy=true;applyingSettings=true;UpdateEnabled();
  try{
   var image=next.CoverPath==old.CoverPath?cover.Image:await CoverImageLoader.LoadAsync(next.CoverPath);
   using var prepared=canvasChanged?Surface.PrepareCanvas(requested!.Value.Width,requested.Value.Height):null;
   var coverTarget=prepared!=null?prepared.Session:(Surface.Ready?Surface.Canvas:null);
   using var preparedCover=(next.CoverPath!=old.CoverPath||canvasChanged)?coverTarget?.PrepareCover(image):null;
#if VERIFICATION
   SettingsApplyStageForTest?.Invoke("prepared");
#endif
   using var pending=new PendingSettings(Program.Option("--settings")??InputSettings.PathName,next,requestedName);
   if(inputChanged){CancelScrub();await playback.StopForInputChangeAsync();}
   else if(canvasChanged){CancelScrub();await StopSyncPreviewAsync();}
   if(canvasChanged)await Preview.StopAsync();
   if(inputChanged){inputAttempted=true;await monitor.ConfigureAsync(next,requireReady:true);}
#if VERIFICATION
   SettingsApplyStageForTest?.Invoke("before-persist");
#endif
   if(closing||closePending)throw new OperationCanceledException("設定変更を中止しました");
   preparedCover?.Validate();pending.Commit();
   // Everything that can be prepared or persisted has succeeded. Publishing the
   // prepared canvas does not recreate resources or perform file/device opening.
   preparedCover?.Commit();prepared?.Commit();
   if(policyChanged&&!inputChanged)monitor.Clock.UpdatePolicy(next.Freewheel,next.Resync);
   if(offsetChanged||inputChanged)playback.SetSynchronizationOffset(mode.FramesToSeconds(next.Offset));
   settings=next;selectedDisplay=requested;selectedDisplayName=requestedName;cover.Image=image;
   if(preparedCover!=null)Surface.AcceptPreparedCover(image);
   if(next.CoverPath!=old.CoverPath)coverLoadWarning="";
   Control<NumericUpDown>("FadeSeconds").Value=(decimal)next.FadeSeconds;
   if(Editor.Rate!=mode){Editor.Rate=mode;Editor.Validate();}
   UpdateResponsiveLayout();StatusMessage("設定を適用しました。");
  }catch(Exception failure){
   if(inputAttempted&&!closing&&!closePending){try{await monitor.ConfigureAsync(old,restoredClock:previousClock,requireReady:true);}catch(Exception rollback){throw new AggregateException("設定変更失敗。LTC入力も復旧できません",failure,rollback);}}
   throw;
  }
  finally{applyingSettings=false;busy=false;UpdateEnabled();}
 }

 async void DeliveryClick(object? sender,RoutedEventArgs e)=>await Safe(async()=>{if(delivery==DeliveryState.Stopped)await ArmAsync();else if(delivery==DeliveryState.Streaming)await RequestStopAsync(false);});
 internal Task<bool> RequestStopAsync(bool exit)
 {
  if(!outputSessionActive)return Task.FromResult(true);if(stopRequest!=null)return stopRequest;
  var task=ConfirmAndStopAsync(exit);stopRequest=task;return ClearRequestAsync(task);
 }
 async Task<bool> ClearRequestAsync(Task<bool> task){try{return await task;}finally{if(ReferenceEquals(stopRequest,task))stopRequest=null;}}
 async Task<bool> ConfirmAndStopAsync(bool exit)
 {
  if(delivery==DeliveryState.Starting){await DisarmAsync();return true;}
  delivery=DeliveryState.ConfirmingStop;UpdateDeliveryButton();
  bool accepted=ConfirmStopForTest!=null?await ConfirmStopForTest(exit):await StopDialog.ShowAsync(this,exit);
  if(accepted)await DisarmAsync();else if(outputSessionActive){delivery=DeliveryState.Streaming;UpdateDeliveryButton();}
  return accepted;
 }
 async void OnWindowClosing(object? sender,WindowClosingEventArgs e)
 {
  if(closeAllowed)return;e.Cancel=true;if(closing||closePending)return;closePending=true;
  try{if(outputSessionActive&&!await RequestStopAsync(true))return;closing=true;UpdateEnabled();await CancelAndWaitForSaveAsync();await CancelAndWaitForCoverAsync();if(materials!=null){await materials.DisposeAsync();materials=null;}await DisarmAsync();await StopSyncPreviewAsync();coverTimer.Stop();activityDelay.Stop();await playback.DisposeAsync();await Preview.DisposeAsync();await monitor.DisposeAsync();timer.Stop();closeAllowed=true;Close();}
  catch(Exception ex){closing=false;StatusMessage(ex.Message);UpdateEnabled();}
  finally{closePending=false;}
 }
#if VERIFICATION
 internal Action? BeforeOutputAttachForTest{get;set;}
#endif
 internal async Task ArmAsync(Func<MasterClock,CancellationToken,Task>? testSource=null)
 {
  if(outputSessionActive||busy||closing||closePending)return;
  string reason=DeliveryBlockReason();if(reason!="")throw new InvalidOperationException(reason);
  outputSessionActive=true;delivery=DeliveryState.Starting;productionCancel=new();var token=productionCancel.Token;CancelScrub();
  var prepared=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);armPrepared=prepared;UpdateEnabled();
  try{
   // Publish Starting before doing native work; queued clicks must not become a stop.
   await Task.Yield();token.ThrowIfCancellationRequested();
   // Synthetic input is a verification seam, never part of a normal output toggle.
   if(testSource!=null){await StopSyncPreviewAsync();await monitor.ConfigureAsync(settings,testSource);}
   if(!playback.UsesSharedAudio)await Preview.SuppressAudioAsync();token.ThrowIfCancellationRequested();
   if(selectedDisplay is not {} display)throw new InvalidDataException("映像出力先が未選択です");
#if VERIFICATION
   BeforeOutputAttachForTest?.Invoke();
#endif
   Surface.Canvas.AttachOutput(display,settings.OutputMode,Program.VerificationOutput);
   outputDiagnostics=new(settings,display);delivery=DeliveryState.Streaming;TickStatus();UpdateEnabled();StatusMessage("");
  }catch{await FinishProductionAsync();throw;}
  finally{prepared.TrySetResult();if(ReferenceEquals(armPrepared,prepared))armPrepared=null;}
 }
 async Task FinishProductionAsync()
 {
  try{if(Surface.Ready)Surface.Canvas.DetachOutput();}
  finally{
   outputSessionActive=false;delivery=DeliveryState.Stopped;
   productionCancel?.Dispose();productionCancel=null;CancelScrub();UpdateEnabled();
  }
  var log=outputDiagnostics;outputDiagnostics=null;if(log!=null)await log.DisposeAsync();
 }
 internal async Task DisarmAsync()
 {
  if(!outputSessionActive)return;
  delivery=DeliveryState.Stopping;UpdateDeliveryButton();productionCancel?.Cancel();
  if(armPrepared is {} prepared)await prepared.Task;
  await FinishProductionAsync();StatusMessage("");
 }
}
