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
 internal void PlayManual(CueRow row){if(CanManualOutput&&row.Enabled)_ = Safe(()=>PlayManualAsync(row,true));}
 internal void StopManual(){if(CanManualOutput)_ = Safe(()=>Preview.StopAsync());}
 async Task PlayManualAsync(CueRow row,bool restart){
  StartupMeasurementTrace.Mark("play_request");
  if(closing||busy||!playback.ManualAvailable||Surface.Canvas.SharedDeviceError<0)return;
  if(armed&&(!CanManualOutput||!row.Enabled))return;
  await StopSyncPreviewAsync();if(!playback.ManualAvailable)return;
  bool same=previewCue==row.CueId&&string.Equals(Preview.LoadedPath,row.FilePath,StringComparison.OrdinalIgnoreCase);
  previewSelection=row;rebinding=true;try{Grid.SelectedItem=row;}finally{rebinding=false;}
  if(!playback.UsesSharedAudio&&!armed)Preview.AllowAudioForNextPlay();
  if(restart||!same){previewCue=row.CueId;CancelScrub();if(!await Preview.LoadAsync(row.FilePath))return;}
  if(LtcHasPriority||Preview.Armed){await UpdateAutoPreviewAsync();return;}
  if(!restart&&same&&Preview.Snapshot.State==PreviewState.Playing)await Preview.PauseAsync();else await Preview.PlayAsync();
  UpdateRowControls();
 }
 DeliveryState delivery;bool keyboardSeeking,closePending;Guid? previewCue;int surfaceGeneration;
 OutputDiagnostics? outputDiagnostics;
 Task<bool>? stopRequest;
 internal Func<bool,Task<bool>>? ConfirmStopForTest {get;set;}
 internal DeliveryState Delivery=>delivery;
 internal LtcMonitorService Monitor=>monitor;
 void CancelScrub(){scrubbing=false;keyboardSeeking=false;surfaceGeneration++;}
 void TickStatus()
 {
  UpdateAudioUi();
  if(armed&&Surface.Ready&&Surface.Canvas.OutputDisconnected)_ = Safe(HandleOutputDisconnectedAsync);
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
  Control<TextBlock>("LtcStateLabel").Foreground=clock.State is SyncState.LOCKED or SyncState.RESYNC?Brush.Parse("#80D5B1"):clock.State==SyncState.ERROR?Brush.Parse("#E6B566"):Brush.Parse("#A5B0BF");
  if(SyncOwnsSurface&&observation is {} t){
   var cue=Editor.Rows.FirstOrDefault(c=>c.CueId==t.CueId);
   Control<TextBlock>("PreviewName").Text=t.CueId.HasValue?CueName(t.CueId):"再生待機";
   SetPreviewTimes(t.FramePts,cue?.Duration,cue?.FrameRate??0);
   Control<TextBlock>("RunInfo").Text=$"LTC同期 · スキップフレーム数 {t.Skipped}";
   UpdatePreviewNotice(t.Warning.StartsWith("映像がLTCから")||t.Warning.StartsWith("準備待ち:")||t.Warning.Contains("LTCの現在位置へ再同期中")?"":t.Warning);
  }else{
   Control<TextBlock>("PreviewName").Text=manualState.Name==""?"キューを選択してください":manualState.Name;
   SetPreviewTimes(manualState.Position,manualState.Duration,Preview.FrameRate);
   Control<TextBlock>("RunInfo").Text=manualState.State switch{PreviewState.Playing=>"手動再生",PreviewState.Paused=>"手動再生 · 一時停止",PreviewState.Stopped=>"手動再生 · 停止",PreviewState.Ended=>"手動再生 · 終了",_=>""};
   UpdatePreviewNotice(manualState.State==PreviewState.Error?manualState.Notice:"");
  }
  UpdateTimingStatus();UpdateRowControls();UpdatePreviewGeometry();
 }
 void UpdateDeliveryButton()
 {
  var button=Control<Button>("ArmButton");button.Content=delivery switch{DeliveryState.Starting=>"出力準備中",DeliveryState.Stopping=>"停止中",DeliveryState.Streaming or DeliveryState.ConfirmingStop=>"出力停止",_=>"出力開始"};
  button.Background=new SolidColorBrush(Color.Parse(delivery is DeliveryState.Streaming or DeliveryState.ConfirmingStop?"#BC303A":"#303844"));
  string reason=DeliveryBlockReason();
  button.IsEnabled=!busy&&!closing&&(delivery is DeliveryState.Streaming || delivery==DeliveryState.Stopped&&reason=="");
  ToolTip.SetTip(button,reason==""?(delivery==DeliveryState.Streaming?"出力を停止します（確認あり）":"LTCに同期して出力を開始します"):reason);
  var hint=Control<TextBlock>("DeliveryHint");hint.IsVisible=delivery==DeliveryState.Stopped&&reason!="";hint.Text=hint.IsVisible?"出力できません："+reason:"";
  Control<TextBlock>("ModeLabel").Text="プレビュー";

  Control<Button>("SettingsButton").IsEnabled=delivery is DeliveryState.Stopped or DeliveryState.Streaming;
  UpdateActivity();UpdatePreviewGeometry();
 }
 internal string DeliveryBlockReason()
 {
  if(closing)return "終了処理中です。";
  if(Surface.Ready&&Surface.Canvas.SharedDeviceError<0)return "共通GPUの描画に失敗しました。アプリを再起動してください。";
  if(busy)return "編集・読込処理中です。";
  if(delivery!=DeliveryState.Stopped)return "";
  if(!Editor.Rows.Any(r=>r.Enabled))return "有効なキューを1件以上登録してください。";
  if(!Editor.CanArm)return Editor.Rows.Any(r=>r.Enabled&&r.Error=="同一開始TCの競合")?"開始TCが重複しています。時刻を変更するか、不要なキューの「有効」を外してください。":Editor.ValidationMessage;
  if(selectedDisplay is not {} display||!VideoRenderer.Displays().Any(d=>d.Name==display.Name&&d.Width==display.Width&&d.Height==display.Height))return "設定から接続済みの映像出力を選択してください。";
  return "";
 }
 void UpdateRowControls()
 {
  foreach(var separator in Grid.GetVisualDescendants().OfType<TextBlock>().Where(t=>t.Classes.Contains("tc-frame-separator")))separator.Text=Editor.Rate.Drop?";":":";
  foreach(var rowControl in Grid.GetVisualDescendants().OfType<DataGridRow>()){
   bool live=(armed||SyncOwnsSurface)&&rowControl.DataContext is CueRow cue&&cue.CueId==observation?.CueId;
   rowControl.Background=live?new SolidColorBrush(Color.Parse("#205C43")):Brushes.Transparent;
   foreach(string key in new[]{"DataGridRowSelectedBackgroundBrush","DataGridRowSelectedHoveredBackgroundBrush","DataGridRowSelectedUnfocusedBackgroundBrush","DataGridRowSelectedHoveredUnfocusedBackgroundBrush"}){
    if(live)rowControl.Resources[key]=rowControl.Background!;else rowControl.Resources.Remove(key);
   }
   ToolTip.SetTip(rowControl,live?(armed?"現在出力中のキュー":"現在プレビュー中のキュー"):null);
  }
  foreach(var b in Grid.GetVisualDescendants().OfType<Button>())if(b.DataContext is CueRow row){
   bool current=previewCue==row.CueId;
   if(b.Classes.Contains("cue-play")){b.IsEnabled=(!armed&&playback.ManualAvailable||CanManualOutput&&row.Enabled)&&!busy&&!closing&&!Preview.IsBusy&&(!Surface.Ready||Surface.Canvas.SharedDeviceError==0);bool pause=!armed&&!SyncOwnsSurface&&current&&Preview.Snapshot.State==PreviewState.Playing;if(b.Tag is not bool wasPause||wasPause!=pause){b.Tag=pause;b.Content=new LucideIcon{Kind=pause?"pause":"play"};}ToolTip.SetTip(b,armed?"このキューを先頭から出力":pause?"一時停止":current&&Preview.Snapshot.State==PreviewState.Paused?"再開":"再生");}
   if(b.Classes.Contains("cue-stop"))b.IsEnabled=!busy&&!closing&&(armed?CanManualOutput&&observation is {Manual:true} t&&t.CueId==row.CueId:current&&!LtcHasPriority&&!SyncOwnsSurface);
  }
 }
 async void RowPlayClick(object? sender,RoutedEventArgs e){if(sender is Button{DataContext:CueRow row})await Safe(()=>PlayManualAsync(row,armed));}
 async void RowStopClick(object? sender,RoutedEventArgs e){if(LtcHasPriority)return;if(sender is Button{DataContext:CueRow row}&&row.CueId==previewCue&&(!armed||CanManualOutput))await Safe(()=>Preview.StopAsync());}
 async void SettingsClick(object? sender,RoutedEventArgs e)
 {
  await Safe(async()=>{
   var dialog=new SettingsDialog(settings,selectedDisplay,armed,audioRequested,monitor,selectedDisplayName){ApplySettings=ApplySettingsAsync};
   await dialog.ShowDialog<SettingsResult?>(this);
  });
 }
 internal async Task ApplySettingsAsync(SettingsResult result)
 {
  if(busy||applyingSettings||closing||closePending)throw new InvalidOperationException("別の処理が完了してから設定を適用してください。");
  var old=settings;var next=result.Settings with{AudioVolume=old.AudioVolume};
  var oldDisplay=selectedDisplay;string? oldName=selectedDisplayName;
  var requested=result.Display;string? requestedName=result.DisplayName??requested?.Name;
  OutputPresentation.Validate(next.OutputMode);var mode=TimecodeRate.Modes.First(r=>r.Name==next.Rate);_=new MasterClock(mode,next.Freewheel,next.Resync);
  if(next.Channel is <0 or >1)throw new InvalidDataException("入力チャンネルはLまたはRを指定してください。");
  bool selectionChanged=requestedName!=oldName||!Nullable.Equals(requested,oldDisplay);
  if(selectionChanged&&requestedName!=null){
   var displays=VideoRenderer.Displays();
#if VERIFICATION
   displays=SettingsDisplaysForTest?.Invoke()??displays;
#endif
   if(requested is not {} output||!displays.Any(d=>d.Name==output.Name&&d.Width==output.Width&&d.Height==output.Height&&d.X==output.X&&d.Y==output.Y))throw new InvalidDataException("指定した出力画面が接続されていません。");
  }
  bool inputChanged=next.DeviceId!=old.DeviceId||next.Channel!=old.Channel||next.Rate!=old.Rate||next.Freewheel!=old.Freewheel||next.Resync!=old.Resync;
  var size=Surface.CanvasSize;
  bool canvasChanged=requested is {} display&&(size.Width!=display.Width||size.Height!=display.Height);
  bool videoChanged=selectionChanged||next.OutputMode!=old.OutputMode;
  bool timingChanged=next.Offset!=old.Offset||next.Rate!=old.Rate;
  bool coverChanged=next.CoverPath!=old.CoverPath||next.FadeSeconds!=old.FadeSeconds;
  if(armed&&(inputChanged||videoChanged||canvasChanged||timingChanged||coverChanged))throw new InvalidOperationException("出力中はLTC入力と映像設定を変更できません。");
  if(audioRequested&&next.AudioDeviceId!=old.AudioDeviceId)throw new InvalidOperationException("音声出力先を変更するには音声出力をOFFにしてください。");
  if(!double.IsFinite(next.FadeSeconds)||next.FadeSeconds<0||next.FadeSeconds>60)throw new InvalidDataException("フェード時間は0〜60秒で指定してください。");
  var previousClock=monitor.Clock;bool inputAttempted=false;
  busy=true;applyingSettings=true;UpdateEnabled();
  try{
   var image=next.CoverPath==old.CoverPath?cover.Image:await CoverImageLoader.LoadAsync(next.CoverPath);
   using var prepared=canvasChanged?Surface.PrepareCanvas(requested!.Value.Width,requested.Value.Height):null;
#if VERIFICATION
   SettingsApplyStageForTest?.Invoke("prepared");
#endif
   using var pending=new PendingSettings(Program.Option("--settings")??InputSettings.PathName,next,requestedName);
   if(inputChanged||timingChanged||canvasChanged){CancelScrub();await StopSyncPreviewAsync();}
   if(canvasChanged||next.Rate!=old.Rate)await Preview.StopAsync();
   if(inputChanged){inputAttempted=true;await monitor.ConfigureAsync(next);}
#if VERIFICATION
   SettingsApplyStageForTest?.Invoke("before-persist");
#endif
   pending.Commit();
   // Everything that can be prepared or persisted has succeeded. Publishing the
   // prepared canvas does not recreate resources or perform file/device opening.
   prepared?.Commit();
   settings=next;selectedDisplay=requested;selectedDisplayName=requestedName;cover.Image=image;
   if(next.CoverPath!=old.CoverPath)coverLoadWarning="";
   Control<NumericUpDown>("FadeSeconds").Value=(decimal)next.FadeSeconds;
   if(Editor.Rate!=mode){Editor.Rate=mode;Editor.Validate();}
   UpdateResponsiveLayout();StatusMessage("設定を適用しました。");
  }catch(Exception failure){
   if(inputAttempted){try{await monitor.ConfigureAsync(old,restoredClock:previousClock);}catch(Exception rollback){throw new AggregateException("設定の適用とLTC入力の復旧に失敗しました。設定を再確認してください。",failure,rollback);}}
   throw;
  }
  finally{applyingSettings=false;busy=false;UpdateEnabled();}
 }

 async void DeliveryClick(object? sender,RoutedEventArgs e)=>await Safe(async()=>{if(delivery==DeliveryState.Stopped)await ArmAsync();else if(delivery==DeliveryState.Streaming)await RequestStopAsync(false);});
 internal Task<bool> RequestStopAsync(bool exit)
 {
  if(!armed)return Task.FromResult(true);if(stopRequest!=null)return stopRequest;
  var task=ConfirmAndStopAsync(exit);stopRequest=task;return ClearRequestAsync(task);
 }
 async Task<bool> ClearRequestAsync(Task<bool> task){try{return await task;}finally{if(ReferenceEquals(stopRequest,task))stopRequest=null;}}
 async Task<bool> ConfirmAndStopAsync(bool exit)
 {
  if(delivery==DeliveryState.Starting){await DisarmAsync();return true;}
  delivery=DeliveryState.ConfirmingStop;UpdateDeliveryButton();
  bool accepted=ConfirmStopForTest!=null?await ConfirmStopForTest(exit):await StopDialog.ShowAsync(this,exit);
  if(accepted)await DisarmAsync();else if(armed){delivery=DeliveryState.Streaming;UpdateDeliveryButton();}
  return accepted;
 }
 async void OnWindowClosing(object? sender,WindowClosingEventArgs e)
 {
  if(closeAllowed)return;e.Cancel=true;if(closing||closePending)return;closePending=true;
  try{if(armed&&!await RequestStopAsync(true))return;closing=true;UpdateEnabled();await DisarmAsync();await StopSyncPreviewAsync();coverTimer.Stop();activityDelay.Stop();await playback.DisposeAsync();await Preview.DisposeAsync();await monitor.DisposeAsync();timer.Stop();closeAllowed=true;Close();}
  catch(Exception ex){closing=false;StatusMessage(ex.Message);UpdateEnabled();}
  finally{closePending=false;}
 }
#if VERIFICATION
 internal Action? BeforeOutputAttachForTest{get;set;}
#endif
 internal async Task ArmAsync(Func<MasterClock,CancellationToken,Task>? testSource=null)
 {
  if(armed||busy||closing||closePending)return;
  string reason=DeliveryBlockReason();if(reason!="")throw new InvalidOperationException(reason);
  armed=true;delivery=DeliveryState.Starting;Editor.Locked=true;productionCancel=new();var token=productionCancel.Token;CancelScrub();
  var prepared=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);armPrepared=prepared;UpdateEnabled();
  try{
   // Publish Starting before doing native work; queued clicks must not become a stop.
   await Task.Yield();token.ThrowIfCancellationRequested();
   // Synthetic input is a verification seam, never part of a normal output toggle.
   if(testSource!=null){await StopSyncPreviewAsync();await monitor.ConfigureAsync(settings,testSource);}
   if(!playback.UsesSharedAudio)await Preview.SuppressAudioAsync();token.ThrowIfCancellationRequested();
   if(selectedDisplay is not {} display)throw new InvalidDataException("映像出力を選択してください。");
#if VERIFICATION
   BeforeOutputAttachForTest?.Invoke();
#endif
   Surface.Canvas.AttachOutput(display,settings.OutputMode,Program.VerificationOutput);
   outputDiagnostics=new(settings,display);delivery=DeliveryState.Streaming;TickStatus();UpdateEnabled();StatusMessage("出力を開始しました。");
  }catch{await FinishProductionAsync();throw;}
  finally{prepared.TrySetResult();if(ReferenceEquals(armPrepared,prepared))armPrepared=null;}
 }
 async Task FinishProductionAsync()
 {
  try{if(Surface.Ready)Surface.Canvas.DetachOutput();}
  finally{
   armed=false;delivery=DeliveryState.Stopped;Editor.Locked=false;
   productionCancel?.Dispose();productionCancel=null;CancelScrub();UpdateEnabled();
  }
  var log=outputDiagnostics;outputDiagnostics=null;if(log!=null)await log.DisposeAsync();
 }
 internal async Task DisarmAsync()
 {
  if(!armed)return;
  delivery=DeliveryState.Stopping;UpdateDeliveryButton();productionCancel?.Cancel();
  if(armPrepared is {} prepared)await prepared.Task;
  await FinishProductionAsync();StatusMessage("出力を停止しました。");
 }
}
