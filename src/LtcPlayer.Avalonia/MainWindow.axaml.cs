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
 InputSettings settings=new();bool busy,armed,closeAllowed,closing,updatingSeek,dragging,scrubbing,rebinding;long selectionVersion;
 CancellationTokenSource? productionCancel;
 TaskCompletionSource? armPrepared;
 ProgramObservation? observation;
 CueRow? dragged,previewSelection;Point dragOrigin;
 internal bool IsArmed=>armed;
 internal ProgramObservation? LatestObservation=>observation;
 T Control<T>(string name)where T:Control=>this.FindControl<T>(name)!;
 DataGrid Grid=>Control<DataGrid>("CueGrid");
 
 void StatusMessage(string s)=>Control<TextBlock>("Status").Text=s;
 public MainWindow()
 {
  AvaloniaXamlLoader.Load(this);Grid.ItemsSource=Editor.Rows;
#if VERIFICATION
  // Presentation assertions require an unoccluded test surface. Do not change
  // the product's window policy or treat an occluded presentation as displayed.
  if(Program.Arguments.Any(a=>a.StartsWith("--verify-",StringComparison.Ordinal)))Topmost=true;
#endif
  AddHandler(PointerPressedEvent,(_,e)=>TracePointer("press",e),RoutingStrategies.Tunnel,true);
  AddHandler(PointerReleasedEvent,(_,e)=>TracePointer("release",e),RoutingStrategies.Tunnel,true);
  activityDelay.Tick+=(_,_)=>UpdateActivity();
  Preview=new(Surface);playback=new(Preview,Surface,()=>monitor.Clock,cover);playback.Failed+=OnPlaybackFailed;Preview.Changed+=UpdatePreview;InitializeResponsiveLayout();InitializePresentation();
  Control<Grid>("PreviewViewport").SizeChanged+=(_,_)=>UpdatePreviewGeometry();
  Editor.Changed+=()=>{Control<TextBlock>("ProgramInfo").Text=Editor.ValidationMessage;UpdateEnabled();};
  Grid.SelectionChanged+=async(_,_)=>await SelectPreviewAsync();
  Control<Slider>("Volume").PropertyChanged+=(_,e)=>{if(e.Property.Name=="Value")ApplyVolume();};
  DragDrop.AddDragOverHandler(Grid,DragOver);DragDrop.AddDropHandler(Grid,Drop);
  Grid.PointerCaptureLost+=(_,_)=>CancelCueDrag();
  Deactivated+=(_,_)=>CancelCueDrag();
  Grid.AddHandler(PointerMovedEvent,HandleMoved,RoutingStrategies.Tunnel,true);
  Grid.AddHandler(PointerReleasedEvent,HandleReleased,RoutingStrategies.Tunnel,true);
  var seek=Control<Slider>("Seek");
  void Scrub(PointerEventArgs e){double x=e.GetPosition(seek).X;seek.Value=Math.Clamp((x-5)/Math.Max(1,seek.Bounds.Width-10),0,1)*seek.Maximum;}
  seek.AddHandler(KeyDownEvent,(_,e)=>{if(e.Key is Key.Left or Key.Right or Key.Home or Key.End or Key.PageUp or Key.PageDown)keyboardSeeking=true;},RoutingStrategies.Tunnel);
  seek.PointerCaptureLost+=(_,_)=>{scrubbing=false;};
  seek.LostFocus+=(_,_)=>{keyboardSeeking=false;};
  Deactivated+=(_,_)=>CancelScrub();
  seek.AddHandler(PointerPressedEvent,(_,e)=>{if(armed||SyncOwnsSurface||LtcHasPriority||!e.GetCurrentPoint(seek).Properties.IsLeftButtonPressed)return;scrubbing=true;e.Pointer.Capture(seek);Scrub(e);e.Handled=true;},RoutingStrategies.Tunnel,true);
  seek.AddHandler(PointerMovedEvent,(_,e)=>{if(scrubbing){Scrub(e);e.Handled=true;}},RoutingStrategies.Tunnel,true);
  seek.AddHandler(PointerReleasedEvent,async(_,e)=>{if(!scrubbing)return;Scrub(e);scrubbing=false;e.Pointer.Capture(null);e.Handled=true;await SeekAsync();},RoutingStrategies.Tunnel,true);
  timer.Tick+=async(_,_)=>{TickStatus();await UpdateAutoPreviewAsync();};
  AddHandler(KeyDownEvent,async(_,e)=>{if(e.Key==Key.Escape){e.Handled=true;if(dragged!=null){CancelCueDrag();return;}if(armed)await Safe(async()=>{await RequestStopAsync(false);});else if(!LtcHasPriority&&!SyncOwnsSurface)await Safe(()=>Preview.StopAsync());}},RoutingStrategies.Tunnel);
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
   if(!Program.Arguments.Any(a=>a.StartsWith("--verify-",StringComparison.Ordinal))||Program.Option("--verify-audio-ui")!=null||Program.Option("--verify-preview-ui")!=null||Program.Option("--verify-timing-status")!=null||Program.Option("--verify-ui-design")!=null||Program.Option("--verify-ui-corrections")!=null)await InitializeAudioUiAsync();
   await monitor.ConfigureAsync(settings);timer.Start();await Task.Delay(200);Surface.Black();Editor.Validate();StatusMessage("準備完了。動画を追加してください。");
   if(Program.Option("--program") is {} file)await Edit(()=>Editor.LoadAsync(file));
   else if(loadDefault)await Edit(()=>DefaultTestShow.LoadAsync(Editor));

   presentationReady=true;
#if VERIFICATION
   if(Program.Option("--verify-startup-measure") is {} startupFolder)await StartupMeasurementVerification.RunAsync(this,startupFolder);
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
  }catch(Exception e){StatusMessage(e.Message);var folder=Program.Option("--verify-startup-measure")??Program.Option("--verify-preview-clip")??Program.Option("--verify-timing-status")??Program.Option("--verify-rendering-health")??Program.Option("--verify-resize-anchor")??Program.Option("--verify-audio-start")??Program.Option("--verify-settings-apply")??Program.Option("--verify-input-level")??Program.Option("--verify-ui-corrections")??Program.Option("--verify-ui-design")??Program.Option("--verify-coordinator")??Program.Option("--verify-preview-ui")??Program.Option("--verify-audio-ui")??Program.Option("--verify-audio-connected")??Program.Option("--verify-audio-service")??Program.Option("--verify-audio-decode")??Program.Option("--verify-audio-foundation")??Program.Option("--verify-playback-integration")??Program.Option("--verify-redesign-baseline")??Program.Option("--verify-shared-canvas")??Program.Option("--verify-phase5")??Program.Option("--verify-ui-update")??Program.Option("--verify-monitor")??Program.Option("--verify-preview-recovery")??Program.Option("--verify-delivery-operations")??Program.Option("--verify-default-show");if(folder!=null){Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"fatal.txt"),e.ToString());Shutdown(1);}}
 }
 internal static void Shutdown(int code){if(Application.Current?.ApplicationLifetime is global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime d)d.Shutdown(code);}
 async Task Safe(Func<Task> action){try{await action();}catch(OperationCanceledException){}catch(Exception e){StatusMessage(e.Message);}}
 async Task Edit(Func<Task> action)
 {
  if(busy||armed||closing)return;busy=true;UpdateEnabled();
  try{await StopSyncPreviewAsync();await action();if(settings.Rate!=Editor.Rate.Name){await Preview.StopAsync();settings=settings with{Rate=Editor.Rate.Name};await monitor.ConfigureAsync(settings);SaveSettings();}StatusMessage("編集を反映しました。");}
  catch(Exception e){StatusMessage(e.Message);}finally{busy=false;Editor.Validate();UpdateEnabled();}
 }
 async Task SelectPreviewAsync()
 {
  if(armed||closing||rebinding||LtcHasPriority||SyncOwnsSurface||Grid.SelectedItem is not CueRow row)return;
  // DataGrid may deliver the same programmatic selection after its next layout.
  // A drag consumes that selection without changing the current media source.
  if(ReferenceEquals(previewSelection,row))return;
  previewSelection=row;long version=++selectionVersion;
  previewCue=row.CueId;CancelScrub();
  await Safe(async()=>{if(!await Preview.LoadAsync(row.FilePath)||version!=selectionVersion)return;UpdatePreview(Preview.Snapshot);});
 }
 void ApplyVolume(){double value=Control<Slider>("Volume").Value;bool mute=false;Preview?.SetVolume(value,mute);playback.SetVolume((float)value,mute);if(audioUiReady){settings=settings with{AudioVolume=value};try{SaveSettings();}catch(Exception e){StatusMessage(e.Message);}}}
 void SetPreviewTimes(double? position,double? duration,double rate){var left=MediaTimeDisplay.Format(position,rate);var right=MediaTimeDisplay.Format(duration,rate);Control<TextBlock>("PreviewPosition").Text=left;Control<TextBlock>("PreviewDuration").Text=right;Control<TextBlock>("PreviewTime").Text=left+" / "+right;}
 void UpdatePreview(PreviewSnapshot p)
 {
  if(p.State==PreviewState.Error&&armed)_ = Safe(async()=>{await DisarmAsync();StatusMessage("再生エラー："+p.Notice);});
  UpdateRowControls();UpdateActivity();UpdatePreviewGeometry();if(SyncOwnsSurface||LtcHasPriority)return;
  Control<TextBlock>("PreviewName").Text=p.Name==""?"キューを選択してください":p.Name;
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
  SetActivity(waiting,delivery==DeliveryState.Starting?"出力を準備中…":delivery==DeliveryState.Stopping?"出力を停止中…":Preview.IsBusy?Preview.BusyMessage:busy?"処理中…":"");
  Control<Slider>("Seek").IsEnabled=!Preview.IsBusy&&!busy&&!armed&&!LtcHasPriority&&!SyncOwnsSurface&&(!Surface.Ready||Surface.Canvas.SharedDeviceError==0);
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
  bool edit=!armed&&!busy&&!closing;
  Control<StackPanel>("EditorTools").IsEnabled=edit;Grid.IsEnabled=!busy&&!closing;Grid.IsReadOnly=armed;

  UpdateDeliveryButton();
  Control<StackPanel>("PreviewControls").IsVisible=true;UpdateRowControls();
 }
 async void AddClick(object? s,RoutedEventArgs e)
 {
  if(armed||busy)return;var files=await StorageProvider.OpenFilePickerAsync(new(){Title="動画を登録",AllowMultiple=true,FileTypeFilter=[new("対応動画"){Patterns=["*.mp4","*.mov"]}]});
  if(files.Count>0)await Edit(()=>Editor.ImportAsync(files.Select(f=>f.TryGetLocalPath()??throw new IOException("ローカルファイルを指定してください。")),new CueRow().StartTimecode));
 }
 async void DeleteClick(object? s,RoutedEventArgs e){if(Grid.SelectedItem is CueRow row)await Edit(async()=>{await Preview.StopAsync();Editor.Delete(row);});}
 async void CloneClick(object? s,RoutedEventArgs e){if(Grid.SelectedItem is CueRow row)await Edit(async()=>{RebindGrid(Editor.Clone(row));await SelectPreviewAsync();});}
 void RebindGrid(CueRow row)
 {
  bool previous=rebinding;rebinding=true;try{Grid.ItemsSource=null;Grid.ItemsSource=Editor.Rows;Grid.SelectedItem=row;}finally{rebinding=previous;}
 }
 async void SaveClick(object? s,RoutedEventArgs e)
 {
  if(armed||busy)return;Grid.CommitEdit();var file=await StorageProvider.SaveFilePickerAsync(new(){Title="上演プログラム保存",DefaultExtension="json",FileTypeChoices=[new("上演プログラム"){Patterns=["*.json"]}]});
  if(file?.TryGetLocalPath() is {} path)await Edit(()=>Editor.SaveAsync(path));
 }
 async void LoadClick(object? s,RoutedEventArgs e)
 {
  if(armed||busy)return;var files=await StorageProvider.OpenFilePickerAsync(new(){Title="上演プログラム読込",FileTypeFilter=[new("上演プログラム"){Patterns=["*.json"]}]});
  if(files.FirstOrDefault()?.TryGetLocalPath() is {} path)await Edit(async()=>{await Preview.StopAsync();await Editor.LoadAsync(path);});
 }
 async void PreviewPlayClick(object? s,RoutedEventArgs e)=>await Safe(async()=>{if(Grid.SelectedItem is not CueRow row)throw new InvalidOperationException("キューを選択してください。");if(!string.Equals(Preview.LoadedPath,row.FilePath,StringComparison.OrdinalIgnoreCase)&&!await Preview.LoadAsync(row.FilePath))return;await Preview.PlayAsync();});
 async void PreviewPauseClick(object? s,RoutedEventArgs e)=>await Safe(()=>Preview.PauseAsync());
 async void PreviewStopClick(object? s,RoutedEventArgs e)=>await Safe(()=>Preview.StopAsync());
 async Task SeekAsync(){if(!updatingSeek&&!armed&&!LtcHasPriority&&!SyncOwnsSurface)await Safe(()=>Preview.SeekAsync(Control<Slider>("Seek").Value));}
 async void SeekKeyUp(object? s,KeyEventArgs e){if(e.Key is Key.Left or Key.Right or Key.Home or Key.End or Key.PageUp or Key.PageDown){try{await SeekAsync();}finally{keyboardSeeking=false;UpdatePreview(Preview.Snapshot);}}}
 void HandlePressed(object? sender,PointerPressedEventArgs e){if(!armed&&!busy&&sender is Control c&&e.GetCurrentPoint(c).Properties.IsLeftButtonPressed){dragged=c.DataContext as CueRow;dragOrigin=e.GetPosition(Grid);dragging=false;cueDragPointer=e.Pointer;e.Pointer.Capture(Grid);e.Handled=true;}}
 void HandleMoved(object? sender,PointerEventArgs e)
 {
  if(armed||busy||dragged==null||!e.GetCurrentPoint(Grid).Properties.IsLeftButtonPressed)return;
  var p=e.GetPosition(Grid);if(!dragging&&Math.Abs(p.X-dragOrigin.X)+Math.Abs(p.Y-dragOrigin.Y)<8)return;
  dragging=true;UpdateCueInsertion(p,true);e.Handled=true;
 }
 void HandleReleased(object? sender,PointerReleasedEventArgs e)
 {
  if(dragged==null)return;var row=dragged;bool move=dragging;
  if(move)UpdateCueInsertion(e.GetPosition(Grid),false);
  int from=Editor.Rows.IndexOf(row),target=InsertionMoveIndex(from,cueInsertionSlot);
  CancelCueDrag();e.Pointer.Capture(null);e.Handled=true;
  if(armed||busy||!move||target<0||target==from)return;
  try{MoveCue(row,target);}catch(Exception ex){StatusMessage(ex.Message);}
 }
 void DragOver(object? s,DragEventArgs e){e.DragEffects=!armed&&!busy&&e.DataTransfer.Formats.Contains(DataFormat.File)?DragDropEffects.Copy:DragDropEffects.None;e.Handled=true;}
 async void Drop(object? s,DragEventArgs e)
 {
  if(armed||busy)return;e.Handled=true;
  if(e.DataTransfer.Formats.Contains(DataFormat.File)&&e.DataTransfer.TryGetFiles() is {} files){await Edit(()=>Editor.ImportAsync(files.Select(f=>f.TryGetLocalPath()).OfType<string>(),new CueRow().StartTimecode));return;}
 }
}


