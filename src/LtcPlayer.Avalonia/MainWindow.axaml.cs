using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LtcPlayer.App;
using System.Globalization;
namespace LtcPlayer.Avalonia;
public partial class MainWindow : Window
{
 internal readonly ShowEditor Editor=new();
 internal PreviewPlayer Preview=null!;
 internal PreviewHost Surface=>Control<PreviewHost>("PreviewSurface");
 readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(100)};
 InputSettings settings=new();bool busy,outputSessionActive,closeAllowed,closing,updatingSeek,dragging,scrubbing;
 CancellationTokenSource? productionCancel;
 TaskCompletionSource? armPrepared;
 ProgramObservation? observation;
 CueRow? dragged;Point dragOrigin;
 internal bool IsOutputActive=>outputSessionActive;
 internal ProgramObservation? LatestObservation=>observation;
 T Control<T>(string name)where T:Control=>this.FindControl<T>(name)!;
 DataGrid Grid=>Control<DataGrid>("CueGrid");
 
 void StatusMessage(string s)=>Control<TextBlock>("Status").Text=s;
 public MainWindow()
 {
  AvaloniaXamlLoader.Load(this);Grid.ItemsSource=Editor.Rows;Grid.SelectionChanged+=(_,_)=>UpdateCueSelectionControls();
#if VERIFICATION
  // Presentation assertions require an unoccluded test surface. Do not change
  // the product's window policy or treat an occluded presentation as displayed.
  if(Program.Arguments.Any(a=>a.StartsWith("--verify-",StringComparison.Ordinal)))Topmost=true;
#endif
  AddHandler(PointerPressedEvent,(_,e)=>TracePointer("press",e),RoutingStrategies.Tunnel,true);
  AddHandler(PointerReleasedEvent,(_,e)=>TracePointer("release",e),RoutingStrategies.Tunnel,true);
  activityDelay.Tick+=(_,_)=>UpdateActivity();
  Preview=new(Surface);playback=new(Preview,Surface,()=>monitor.Clock,cover);Editor.DefinitionAuthority=()=>DefinitionAuthority;Editor.ReorderAuthority=()=>!closing&&!closePending&&delivery is not (DeliveryState.Starting or DeliveryState.Stopping or DeliveryState.ConfirmingStop);playback.Failed+=OnPlaybackFailed;Preview.Changed+=UpdatePreview;InitializeResponsiveLayout();InitializeColumnSizing();InitializePresentation();
  Control<Grid>("PreviewViewport").SizeChanged+=(_,_)=>UpdatePreviewGeometry();
  Editor.Changed+=()=>{UpdateProgramSummary();UpdateEnabled();};
  Control<Slider>("Volume").PropertyChanged+=(_,e)=>{if(e.Property.Name=="Value")ApplyVolume();};
  DragDrop.AddDragOverHandler(Grid,DragOver);DragDrop.AddDropHandler(Grid,Drop);
  Grid.PointerCaptureLost+=(_,_)=>CancelCueDrag();
  Deactivated+=(_,_)=>CancelCueDrag();
  Grid.AddHandler(PointerMovedEvent,HandleMoved,RoutingStrategies.Tunnel,true);
  Grid.AddHandler(PointerReleasedEvent,HandleReleased,RoutingStrategies.Tunnel,true);
  var seek=Control<Slider>("Seek");
  void Scrub(PointerEventArgs e){double x=e.GetPosition(seek).X;seek.Value=Math.Clamp((x-5)/Math.Max(1,seek.Bounds.Width-10),0,1)*seek.Maximum;}
  seek.AddHandler(KeyDownEvent,(_,e)=>{if(e.Key is Key.Left or Key.Right or Key.Home or Key.End or Key.PageUp or Key.PageDown){if(SeekAccess.Allowed)keyboardSeeking=true;else e.Handled=true;}},RoutingStrategies.Tunnel);
  seek.PointerCaptureLost+=(_,_)=>{scrubbing=false;seekPointer=null;};
  seek.LostFocus+=(_,_)=>{keyboardSeeking=false;};
  Deactivated+=(_,_)=>CancelScrub();
  seek.AddHandler(PointerPressedEvent,(_,e)=>{if(!SeekAccess.Allowed||!e.GetCurrentPoint(seek).Properties.IsLeftButtonPressed)return;scrubbing=true;seekPointer=e.Pointer;e.Pointer.Capture(seek);Scrub(e);e.Handled=true;},RoutingStrategies.Tunnel,true);
  seek.AddHandler(PointerMovedEvent,(_,e)=>{if(scrubbing){Scrub(e);e.Handled=true;}},RoutingStrategies.Tunnel,true);
  seek.AddHandler(PointerReleasedEvent,async(_,e)=>{if(!scrubbing)return;Scrub(e);scrubbing=false;seekPointer=null;e.Pointer.Capture(null);e.Handled=true;await SeekAsync();},RoutingStrategies.Tunnel,true);
  timer.Tick+=async(_,_)=>{TickStatus();await UpdateAutoPreviewAsync();};
  AddHandler(KeyDownEvent,async(_,e)=>{if(e.Key==Key.Escape){e.Handled=true;if(columnResize!=null){FinishColumnResize();return;}if(FocusManager?.GetFocusedElement() is TextBox box&&box.Classes.Contains("timecode")){CancelTimecodeEdit();FocusManager.ClearFocus();return;}if(dragged!=null){CancelCueDrag();return;}if(outputSessionActive)await Safe(async()=>{await RequestStopAsync(false);});else if(!LtcHasPriority&&!SyncOwnsSurface)await Safe(()=>Preview.StopAsync());}},RoutingStrategies.Tunnel);
  Opened+=async(_,_)=>await InitializeAsync();
  Closing+=OnWindowClosing;
 }
 void TracePointer(string kind,PointerEventArgs e)
 {
  if(Program.Option("--diagnostic-ui") is not {} file)return;
  var p=e.GetPosition(Grid);var hit=Grid.GetVisualAt(p) as Control;
  File.AppendAllText(file,System.Text.Json.JsonSerializer.Serialize(new{kind,x=p.X,y=p.Y,source=e.Source?.GetType().Name,hit=hit?.GetType().Name,context=hit?.DataContext?.GetType().Name,dragged=dragged?.Name,dragging,seek=Control<Slider>("Seek").Value})+Environment.NewLine);
 }
 string CueName(Guid? id)=>Editor.Rows.FirstOrDefault(x=>x.CueId==id)?.Name??"—";
 async Task InitializeAsync()
 {
  try {
   settings=InputSettings.Load(Program.Option("--settings"));
   bool loadDefault=Program.Option("--program")==null&&(!Program.Arguments.Any(a=>a.StartsWith("--verify-",StringComparison.Ordinal))||Program.Option("--verify-default-show")!=null);
   if(loadDefault&&!File.Exists(Program.Option("--settings")??InputSettings.PathName))settings=settings with{Rate="29.97 DF"};
   Editor.Rate=TimecodeRate.Modes.FirstOrDefault(r=>r.Name==settings.Rate)??TimecodeRate.Modes[1];
   LoadOutputSelection();await LoadPresentationSettingsAsync();
   if(!Program.Arguments.Any(a=>a.StartsWith("--verify-",StringComparison.Ordinal))||Program.Option("--verify-ltc-settings")!=null||Program.Option("--verify-cover-changes")!=null||Program.Option("--verify-audio-ui")!=null||Program.Option("--verify-preview-ui")!=null||Program.Option("--verify-timing-status")!=null||Program.Option("--verify-ui-design")!=null||Program.Option("--verify-ui-copy")!=null||Program.Option("--verify-ui-corrections")!=null)await InitializeAudioUiAsync();
   await monitor.ConfigureAsync(settings);timer.Start();await Task.Delay(200);Surface.Black();Editor.Validate();StatusMessage("");
   if(Program.Option("--program") is {} file)await Edit(()=>Editor.LoadAsync(file));
   else if(loadDefault)await Edit(()=>DefaultTestShow.LoadAsync(Editor));

   await Dispatcher.UIThread.InvokeAsync(FitInitialContentHeight,DispatcherPriority.Loaded);
   presentationReady=true;StartMaterialMonitor();
#if VERIFICATION
   if(Program.Option("--verify-display-columns") is {} columnsFolder)await DisplayColumnsVerification.RunAsync(this,columnsFolder);
   if(Program.Option("--verify-ui-copy") is {} copyFolder)await UiCopyVerification.RunAsync(this,copyFolder);
   if(Program.Option("--verify-cover-height") is {} coverHeightFolder)await CoverHeightVerification.RunAsync(this,coverHeightFolder);
   if(Program.Option("--verify-save-layout") is {} saveLayoutFolder)await SaveLayoutVerification.RunAsync(this,saveLayoutFolder);
   if(Program.Option("--verify-multi-selection") is {} multiFolder)await MultiSelectionVerification.RunAsync(this,multiFolder);
   if(Program.Option("--verify-ltc-settings") is {} ltcSettingsFolder)await LtcSettingsVerification.RunAsync(this,ltcSettingsFolder);
   if(Program.Option("--verify-cover-changes") is {} coverChangesFolder)await CoverChangesVerification.RunAsync(this,coverChangesFolder);
   else if(Program.Option("--verify-hap-preparation") is {} hapFolder)await HapPreparationVerification.RunAsync(this,hapFolder);
   else if(Program.Option("--verify-lost-editing") is {} lostFolder)await LostEditingVerification.RunAsync(this,lostFolder);
   else if(Program.Option("--verify-cue-editing") is {} editingFolder)await CueEditingVerification.RunAsync(this,editingFolder);
   else if(Program.Option("--verify-operation-access") is {} operationFolder)await OperationAccessVerification.RunAsync(this,operationFolder);
   else if(Program.Option("--verify-material-recovery") is {} materialRecoveryFolder)await MaterialRecoveryVerification.RunAsync(this,materialRecoveryFolder);
   else if(Program.Option("--verify-startup-measure") is {} startupFolder)await StartupMeasurementVerification.RunAsync(this,startupFolder);
   else if(Program.Option("--verify-preview-clip") is {} clipFolder)await PreviewClipVerification.RunAsync(this,clipFolder);
   else if(Program.Option("--verify-timing-status") is {} timingFolder)await TimingStatusVerification.RunAsync(this,timingFolder);
   else if(Program.Option("--verify-resize-anchor") is {} resizeFolder)await ResizeAnchorVerification.RunAsync(this,resizeFolder);
   else if(Program.Option("--verify-audio-start") is {} audioStartFolder)await AudioStartVerification.RunAsync(this,audioStartFolder);
   else if(Program.Option("--verify-rendering-health") is {} healthFolder)await RenderingHealthVerification.RunAsync(this,healthFolder);
   else if(Program.Option("--verify-settings-apply") is {} settingsApplyFolder)await SettingsApplyVerification.RunAsync(this,settingsApplyFolder);
   else if(Program.Option("--verify-input-level") is {} inputLevelFolder)await InputLevelVerification.RunAsync(this,inputLevelFolder);
   else if(Program.Option("--verify-ui-corrections") is {} correctionsFolder)await UiCorrectionsVerification.RunAsync(this,correctionsFolder);
   else if(Program.Option("--verify-ui-design") is {} designFolder)await UiDesignVerification.RunAsync(this,designFolder);
   else if(Program.Option("--verify-coordinator") is {} coordinatorFolder)await CoordinatorVerification.RunAsync(this,coordinatorFolder);
   else if(Program.Option("--verify-preview-ui") is {} previewUiFolder)await PreviewUiVerification.RunAsync(this,previewUiFolder);
   else if(Program.Option("--verify-audio-ui") is {} audioUiFolder)await AudioUiVerification.RunAsync(this,audioUiFolder);
   else if(Program.Option("--verify-audio-connected") is {} audioConnectedFolder)await AudioConnectedVerification.RunAsync(this,audioConnectedFolder);
   else if(Program.Option("--verify-audio-service") is {} audioServiceFolder)await AudioServiceVerification.RunAsync(this,audioServiceFolder);
   else if(Program.Option("--verify-audio-decode") is {} audioDecodeFolder)await AudioDecodeVerification.RunAsync(this,audioDecodeFolder);
   if(Program.Option("--verify-audio-foundation") is {} audioFoundationFolder)await AudioFoundationVerification.RunAsync(this,audioFoundationFolder);
   if(Program.Option("--verify-playback-integration") is {} integrationFolder)await PlaybackIntegrationVerification.RunAsync(this,integrationFolder);
   if(Program.Option("--verify-redesign-baseline") is {} baselineFolder)await RedesignBaselineVerification.RunAsync(this,baselineFolder);
   if(Program.Option("--verify-shared-canvas") is {} canvasFolder)await SharedCanvasVerification.RunAsync(this,canvasFolder);
   if(Program.Option("--verify-presentation") is {} presentationFolder)await PresentationVerification.RunAsync(this,presentationFolder);
   if(Program.Option("--verify-cue-ux") is {} cueUxFolder)await CueUxVerification.RunAsync(this,cueUxFolder);
   if(Program.Option("--verify-manual-output") is {} manualFolder)await ManualOutputVerification.RunAsync(this,manualFolder);
   if(Program.Option("--verify-responsive") is {} responsiveFolder)await ResponsiveVerification.RunAsync(this,responsiveFolder);
   if(Program.Option("--verify-output-mode") is {} modeFolder)await OutputModeVerification.RunAsync(this,modeFolder);
   if(Program.Option("--verify-live-startup") is {} liveFolder)await LiveStartupVerification.RunAsync(this,liveFolder);
   if(Program.Option("--verify-resume-latency") is {} latencyFolder)await ResumeLatencyVerification.RunAsync(this,latencyFolder);
   if(Program.Option("--verify-portrait-monitor") is {} portraitFolder)await PortraitMonitorVerification.RunAsync(this,portraitFolder);
   if(Program.Option("--verify-release-features") is {} featureFolder)await ReleaseFeaturesVerification.RunAsync(this,featureFolder);
   if(Program.Option("--verify-sync-recovery-output") is {} syncFolder)await SyncRecoveryVerification.RunAsync(this,syncFolder);
   if(Program.Option("--verify-preview-performance") is {} performanceFolder)await PreviewPerformanceVerification.RunAsync(this,performanceFolder);
   if(Program.Option("--verify-phase2-adapter") is {} phase2Folder){await Phase2Verification.RunAsync(new RegressionWindow(),phase2Folder);Shutdown(RegressionApplication.Current.ExitCode);return;}
   if(Program.Option("--verify-phase5") is {} folder)await Phase5Verification.RunAsync(this,folder);
   if(Program.Option("--verify-monitor") is {} monitorFolder)await MonitorVerification.RunAsync(this,monitorFolder);
   if(Program.Option("--verify-ui-update") is {} updateFolder)await UiUpdateVerification.RunAsync(this,updateFolder);
   if(Program.Option("--verify-time-display") is {} displayFolder)DisplayFormatVerification.Run(this,displayFolder);
   if(Program.Option("--verify-preview-recovery") is {} recoveryFolder)await PreviewRecoveryVerification.RunAsync(this,recoveryFolder);
   if(Program.Option("--verify-delivery-operations") is {} deliveryFolder)await DeliveryOperationsVerification.RunAsync(this,deliveryFolder);
   if(Program.Option("--verify-default-show") is {} defaultFolder)await DefaultTestShow.VerifyAsync(this,defaultFolder);
#endif
  }catch(Exception e){StatusMessage(e.Message);var folder=Program.Option("--verify-cover-height")??Program.Option("--verify-save-layout")??Program.Option("--verify-multi-selection")??Program.Option("--verify-ltc-settings")??Program.Option("--verify-cover-changes")??Program.Option("--verify-hap-preparation")??Program.Option("--verify-lost-editing")??Program.Option("--verify-cue-editing")??Program.Option("--verify-operation-access")??Program.Option("--verify-material-recovery")??Program.Option("--verify-startup-measure")??Program.Option("--verify-preview-clip")??Program.Option("--verify-timing-status")??Program.Option("--verify-rendering-health")??Program.Option("--verify-resize-anchor")??Program.Option("--verify-audio-start")??Program.Option("--verify-settings-apply")??Program.Option("--verify-input-level")??Program.Option("--verify-ui-corrections")??Program.Option("--verify-ui-design")??Program.Option("--verify-coordinator")??Program.Option("--verify-preview-ui")??Program.Option("--verify-audio-ui")??Program.Option("--verify-audio-connected")??Program.Option("--verify-audio-service")??Program.Option("--verify-audio-decode")??Program.Option("--verify-audio-foundation")??Program.Option("--verify-playback-integration")??Program.Option("--verify-redesign-baseline")??Program.Option("--verify-shared-canvas")??Program.Option("--verify-phase5")??Program.Option("--verify-ui-update")??Program.Option("--verify-monitor")??Program.Option("--verify-preview-recovery")??Program.Option("--verify-delivery-operations")??Program.Option("--verify-default-show");if(folder!=null){Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"fatal.txt"),e.ToString());Shutdown(1);}}
 }
 internal static void Shutdown(int code){if(Application.Current?.ApplicationLifetime is global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime d)d.Shutdown(code);}
 async Task Safe(Func<Task> action){try{await action();}catch(OperationCanceledException){}catch(Exception e){StatusMessage(e.Message);}}
 async Task Edit(Func<Task> action)
 {
  if(!CanEditProgram)return;CommitTimecodeEdit();editingProgram=true;UpdateEnabled();
  try{await action();if(settings.Rate!=Editor.Rate.Name){await Preview.StopAsync();settings=settings with{Rate=Editor.Rate.Name};await monitor.ConfigureAsync(settings);SaveSettings();}StatusMessage(Editor.Notices!=""?Editor.Notices:"");}
  catch(Exception e){StatusMessage(e.Message);}finally{editingProgram=false;UpdateEnabled();}
 }
 void ApplyVolume(){double value=Control<Slider>("Volume").Value;bool mute=false;Preview?.SetVolume(value,mute);playback.SetVolume((float)value,mute);if(audioUiReady){settings=settings with{AudioVolume=value};try{SaveSettings();}catch(Exception e){StatusMessage(e.Message);}}}
 void SetPreviewTimes(double? position,double? duration,double rate){var left=MediaTimeDisplay.Format(position,rate);var right=MediaTimeDisplay.Format(duration,rate);Control<TextBlock>("PreviewPosition").Text=left;Control<TextBlock>("PreviewDuration").Text=right;Control<TextBlock>("PreviewTime").Text=left+" / "+right;}
 void UpdatePreview(PreviewSnapshot p)
 {
  if(p.State==PreviewState.Error&&Surface.Ready&&Surface.Canvas.SharedDeviceError<0){_ = Safe(HandleSharedGraphicsFailureAsync);return;}
  if(p.State==PreviewState.Error){
   var row=Editor.Rows.FirstOrDefault(r=>r.CueId==previewCue);
   if(row!=null&&row.SourceError!=p.Notice){row.SourceError=p.Notice;Editor.Validate();}
  }
  UpdateRowControls();UpdateActivity();UpdatePreviewGeometry();if(SyncOwnsSurface||LtcHasPriority)return;
  Control<TextBlock>("PreviewName").Text=p.Name==""?"未再生":p.Name;
  SetPreviewTimes(p.Position,p.Duration,Preview.FrameRate);
  UpdatePreviewNotice(p.State==PreviewState.Error?p.Notice:"");
  var slider=Control<Slider>("Seek");slider.Maximum=Math.Max(.001,p.Duration);
  if(!scrubbing&&!keyboardSeeking){updatingSeek=true;slider.Value=p.Position;updatingSeek=false;}
 }
 readonly DispatcherTimer activityDelay=new(){Interval=TimeSpan.FromMilliseconds(50)};
 double activitySince;
 void UpdateActivity()
 {
  bool waiting=busy||Preview.IsBusy||delivery is DeliveryState.Starting or DeliveryState.Stopping;
  SetActivity(waiting,delivery==DeliveryState.Starting?"出力準備中":delivery==DeliveryState.Stopping?"出力停止中":Preview.IsBusy?Preview.BusyMessage:busy?"処理中":"");
  UpdateOperationControls();
 }
 internal void SetActivity(bool waiting,string message)
 {
  if(waiting&&activitySince==0){activitySince=MasterClock.Timestamp();activityDelay.Start();}
  if(!waiting){activitySince=0;activityDelay.Stop();}
  bool visible=waiting&&MasterClock.Timestamp()-activitySince>=.18;
  var bar=Control<ProgressBar>("ActivityBar");bar.Opacity=visible?1:0;bar.IsIndeterminate=visible;
  var label=Control<TextBlock>("ActivityLabel");label.Opacity=visible?1:0;
  label.Text=message;
 }
 void UpdateEnabled()
 {
  Control<StackPanel>("EditorTools").IsEnabled=!closing;Grid.IsEnabled=!busy&&!closing;Grid.IsReadOnly=!CanEditProgram;
  UpdateOperationControls();

  UpdateDeliveryButton();
  Control<StackPanel>("PreviewControls").IsVisible=true;UpdateRowControls();
 }
 async void AddClick(object? s,RoutedEventArgs e)
 {
  if(!CanEditProgram)return;var files=await StorageProvider.OpenFilePickerAsync(new(){Title="動画を登録",AllowMultiple=true,FileTypeFilter=[new("対応動画"){Patterns=["*.mp4","*.mov"]}]});
  if(files.Count>0)await Edit(()=>Editor.ImportAsync(files.Select(f=>f.TryGetLocalPath()??throw new IOException("ローカルファイルが必要です")),new CueRow().StartTimecode));
 }
 async void DeleteClick(object? s,RoutedEventArgs e){var ids=SelectedCueIds();if(ids.Length>0)await Edit(()=>DeleteCuesAsync(ids));}
 internal Task DeleteCueAsync(CueRow row)=>DeleteCuesAsync([row.CueId]);
 async void CloneClick(object? s,RoutedEventArgs e){var ids=SelectedCueIds();if(ids.Length==1&&Editor.Rows.FirstOrDefault(r=>r.CueId==ids[0]) is {} row)await Edit(()=>{RebindGrid(Editor.Clone(row));return Task.CompletedTask;});}
 void RebindGrid(CueRow row)=>RebindCueSelection([row.CueId],row.CueId);
 async void SaveClick(object? s,RoutedEventArgs e)=>await Safe(ChooseAndSaveProgramAsync);
 async void LoadClick(object? s,RoutedEventArgs e)
 {
  if(!CanEditProgram)return;var files=await StorageProvider.OpenFilePickerAsync(new(){Title="キューリストを開く",FileTypeFilter=[new("キューリスト"){Patterns=["*.json"]}]});
  if(files.FirstOrDefault()?.TryGetLocalPath() is {} path)await Edit(async()=>{var load=await ShowProgramService.LoadAsync(path,CancellationToken.None);if(!DefinitionAuthority)return;if(SyncOwnsSurface){if(!await playback.ClearHeldSourceAsync())return;}else await Preview.ClearAsync();if(!DefinitionAuthority)return;previewCue=null;Editor.Replace(load);});
 }
 internal async Task SeekAsync(){if(!updatingSeek&&SeekAccess.Allowed)await Safe(()=>Preview.SeekAsync(Control<Slider>("Seek").Value));}
 async void SeekKeyUp(object? s,KeyEventArgs e){if(e.Key is Key.Left or Key.Right or Key.Home or Key.End or Key.PageUp or Key.PageDown){try{await SeekAsync();}finally{keyboardSeeking=false;UpdatePreview(Preview.Snapshot);}}}
 void HandlePressed(object? sender,PointerPressedEventArgs e){if(ReorderAccess.Allowed&&sender is Control c&&e.GetCurrentPoint(c).Properties.IsLeftButtonPressed){dragged=c.DataContext as CueRow;dragOrigin=e.GetPosition(Grid);dragging=false;cueDragPointer=e.Pointer;e.Pointer.Capture(Grid);e.Handled=true;}}
 void HandleMoved(object? sender,PointerEventArgs e)
 {
  if(!ReorderAccess.Allowed||dragged==null||!e.GetCurrentPoint(Grid).Properties.IsLeftButtonPressed)return;
  var p=e.GetPosition(Grid);if(!dragging&&Math.Abs(p.X-dragOrigin.X)+Math.Abs(p.Y-dragOrigin.Y)<8)return;
  dragging=true;UpdateCueInsertion(p,true);e.Handled=true;
 }
 void HandleReleased(object? sender,PointerReleasedEventArgs e)
 {
  if(dragged==null)return;var row=dragged;bool move=dragging;
  if(move)UpdateCueInsertion(e.GetPosition(Grid),false);
  int from=Editor.Rows.IndexOf(row),target=InsertionMoveIndex(from,cueInsertionSlot);
  CancelCueDrag();e.Pointer.Capture(null);e.Handled=true;
  if(!ReorderAccess.Allowed||!move||target<0||target==from)return;
  try{MoveCue(row,target);}catch(Exception ex){StatusMessage(ex.Message);}
 }
 void DragOver(object? s,DragEventArgs e){e.DragEffects=CanEditProgram&&e.DataTransfer.Formats.Contains(DataFormat.File)?DragDropEffects.Copy:DragDropEffects.None;e.Handled=true;}
 async void Drop(object? s,DragEventArgs e)
 {
  if(!CanEditProgram)return;e.Handled=true;
  if(e.DataTransfer.Formats.Contains(DataFormat.File)&&e.DataTransfer.TryGetFiles() is {} files){await Edit(()=>Editor.ImportAsync(files.Select(f=>f.TryGetLocalPath()).OfType<string>(),new CueRow().StartTimecode));return;}
 }
}
