using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.LogicalTree;
using LtcPlayer.App;
using System.Text.Json;
namespace LtcPlayer.Avalonia;
public static class SettingsApplyVerification
{
 public static async Task RunAsync(MainWindow w,string folder){
  Directory.CreateDirectory(folder);bool failed=false;var results=new List<object>();SettingsDialog? d=null;Task<SettingsResult?>? shown=null;int applications=0;
  void Check(bool ok,string message){if(!ok)throw new IOException(message);}
  async Task Wait(Func<bool> condition){double end=MasterClock.Timestamp()+4;while(!condition()){if(MasterClock.Timestamp()>end)throw new TimeoutException(d?.ChangeStatus.Text);await Task.Delay(10);}}
  async Task Test(string name,Func<Task> body){try{await body();results.Add(new{test=name,status="PASS"});}catch(Exception e){failed=true;results.Add(new{test=name,status="FAIL",detail=e.ToString()});}File.WriteAllText(Path.Combine(folder,"results.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));}
  async Task Apply(SettingsResult result){applications++;await w.ApplySettingsAsync(result);}
  void Click()=>d!.ApplyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
  try{
   var display=VideoRenderer.Displays().First(x=>x.Primary!=0);await w.ApplySettingsAsync(new(new(),display));d=new SettingsDialog(InputSettings.Load(Program.Option("--settings")),display,false,false,w.Monitor){ApplySettings=Apply};shown=d.ShowDialog<SettingsResult?>(w);await Task.Delay(120);
   await Test("refresh actions use circular arrow icons",()=>{Check(d.InputRefreshButton.Content is LucideIcon{Kind:"refresh-cw"}&&d.AudioRefreshButton.Content is LucideIcon{Kind:"refresh-cw"},"reload icons missing");Check(!d.ApplyButton.IsEnabled,"unchanged apply enabled");return Task.CompletedTask;});
   await Test("unapplied edits are highlighted and reverting clears them",()=>{d.ModeSelector.SelectedIndex=1;Check(d.ApplyButton.IsEnabled&&d.ChangeStatus.Text=="未適用の変更があります"&&d.ModeSelector.BorderBrush?.ToString()=="#ffe6b566","dirty state absent");d.ModeSelector.SelectedIndex=0;Check(!d.ApplyButton.IsEnabled&&d.ChangeStatus.Text=="","reverted change stayed dirty");return Task.CompletedTask;});
   await Test("apply persists changes without closing and supports repeated apply",async()=>{
    int starts=w.Monitor.Starts;d.ModeSelector.SelectedIndex=1;Click();await Wait(()=>d.LastAppliedResult?.Settings.OutputMode==OutputPresentationMode.Compatibility);Check(d.IsVisible&&!shown.IsCompleted&&InputSettings.Load(Program.Option("--settings")).OutputMode==OutputPresentationMode.Compatibility,"apply closed or did not persist");Check(!d.ApplyButton.IsEnabled&&d.ChangeStatus.Text=="適用しました","dirty state not reset");d.ModeSelector.SelectedIndex=0;Click();await Wait(()=>d.LastAppliedResult?.Settings.OutputMode==OutputPresentationMode.Standard);Check(d.IsVisible&&applications==2&&w.Monitor.Starts==starts,"repeat apply/input lifecycle mismatch");
   });
   await Test("apply failure keeps edits and permits retry",async()=>{
    d.ApplySettings=_=>throw new IOException("Synthetic apply failure");d.ModeSelector.SelectedIndex=1;Click();await Wait(()=>d.ChangeStatus.Text=="Synthetic apply failure");Check(d.IsVisible&&d.ApplyButton.IsEnabled&&d.LastAppliedResult?.Settings.OutputMode==OutputPresentationMode.Standard&&d.ModeSelector.BorderBrush?.ToString()=="#ffe6b566","failure discarded pending edits");d.ApplySettings=Apply;Click();await Wait(()=>d.LastAppliedResult?.Settings.OutputMode==OutputPresentationMode.Compatibility);Check(!d.ApplyButton.IsEnabled&&d.ChangeStatus.Text=="適用しました","retry failed");
   });
   await Test("in-flight apply is guarded against double activation",async()=>{
    var gate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);int calls=0;d.ApplySettings=async result=>{calls++;await gate.Task;await Apply(result);};d.ModeSelector.SelectedIndex=0;Click();Click();Check(calls==1&&!d.ApplyButton.IsEnabled,"double apply dispatched");gate.SetResult();await Wait(()=>d.LastAppliedResult?.Settings.OutputMode==OutputPresentationMode.Standard);Check(d.IsVisible,"in-flight completion closed panel");
   });
   await Test("close preserves applied settings and discards only pending edits",async()=>{
    d.ModeSelector.SelectedIndex=1;d.GetLogicalDescendants().OfType<TabControl>().Single().SelectedIndex=2;await Task.Delay(80);using(var image=new RenderTargetBitmap(new PixelSize((int)d.ClientSize.Width,(int)d.ClientSize.Height),new Vector(96,96))){image.Render(d);image.Save(Path.Combine(folder,"pending-changes.png"));}d.Close(null);await shown;Check(InputSettings.Load(Program.Option("--settings")).OutputMode==OutputPresentationMode.Standard&&!d.InputLevelTimerRunning,"close changed applied settings or kept timer");
   });
   var settingsFile=Program.Option("--settings")!;
   var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
   void ReloadSelection()=>typeof(MainWindow).GetMethod("LoadOutputSelection",flags)!.Invoke(w,null);
   string? SavedDisplay(){using var doc=JsonDocument.Parse(File.ReadAllText(settingsFile));return doc.RootElement.GetProperty("OutputDisplayName").GetString();}
   await Test("unconnected output stays selected while audio-only settings apply",async()=>{
    const string missing="MISSING-SETTINGS-VERIFICATION-DISPLAY";var current=InputSettings.Load(settingsFile);
    using(var pending=new PendingSettings(settingsFile,current,missing))pending.Commit();ReloadSelection();
    var canvas=w.Surface.Canvas.SessionId;var clock=w.Monitor.Clock;int starts=w.Monitor.Starts;
    d=new SettingsDialog(current,null,false,false,w.Monitor,missing){ApplySettings=w.ApplySettingsAsync};shown=d.ShowDialog<SettingsResult?>(w);await Task.Delay(80);
    Check(d.OutputSelector.SelectedItem is OutputDisplayChoice{Display:null,Name:missing,Connected:false}&&!d.ApplyButton.IsEnabled,"missing output silently replaced or initially dirty");
    var device=new AudioDevice{Id="MISSING-AUDIO-OUTPUT",Name="未接続の音声出力先"};d.AudioOutputSelector.ItemsSource=new[]{new AudioDevice{Id="",Name="システム既定"},device};d.AudioOutputSelector.SelectedItem=device;
    Click();await Wait(()=>d.LastAppliedResult!=null);Check(d.ChangeStatus.Text=="適用しました"&&SavedDisplay()==missing,"audio apply changed output selection");
    Check(w.Surface.Canvas.SessionId==canvas&&ReferenceEquals(w.Monitor.Clock,clock)&&w.Monitor.Starts==starts,"audio-only apply recreated video/input");
    d.Close(null);await shown;
   });
   await Test("input and video mode apply without a connected output",async()=>{
    var current=InputSettings.Load(settingsFile);var canvas=w.Surface.Canvas.SessionId;var next=current with{Channel=1,OutputMode=OutputPresentationMode.Compatibility};
    await w.ApplySettingsAsync(new(next,null,SavedDisplay()));Check(InputSettings.Load(settingsFile).Channel==1&&SavedDisplay()=="MISSING-SETTINGS-VERIFICATION-DISPLAY"&&w.Surface.Canvas.SessionId==canvas,"unrelated settings require output or changed canvas");
    var dialog=new SettingsDialog(next,null,false,false,w.Monitor,SavedDisplay());Check(!dialog.ApplyButton.IsEnabled,"reopened missing selection dirty");dialog.Close();
    await w.ApplySettingsAsync(new(new(),display));
   });
   await Test("invalid explicit output is rejected without partial changes",async()=>{
    var before=File.ReadAllText(settingsFile);var canvas=w.Surface.Canvas.SessionId;var clock=w.Monitor.Clock;
    bool rejected=false;try{await w.ApplySettingsAsync(new(InputSettings.Load(settingsFile),null,"OTHER-MISSING-DISPLAY"));}catch(InvalidDataException){rejected=true;}
    Check(rejected&&File.ReadAllText(settingsFile)==before&&w.Surface.Canvas.SessionId==canvas&&ReferenceEquals(w.Monitor.Clock,clock),"invalid selection partially applied");
   });
   var fake=new OutputDisplay{Name="SETTINGS-STAGED-CANVAS",Width=640,Height=360};w.SettingsDisplaysForTest=()=>new[]{display,fake};
   await Test("prepared canvas is discarded on failure without stopping manual playback",async()=>{
    var media=Path.Combine(Program.Option("--fixtures")!,"h264-aac.mp4");await w.Preview.UseSharedAudioAsync();await w.Preview.LoadAsync(media);await w.Preview.PlayAsync();await Task.Delay(80);
    var canvas=w.Surface.Canvas.SessionId;var playback=w.Preview.PlaybackSessionId;var before=File.ReadAllText(settingsFile);int starts=w.Monitor.Starts;
    w.SettingsApplyStageForTest=stage=>{if(stage=="prepared")throw new IOException("Synthetic prepared resource failure");};bool rejected=false;
    try{await w.ApplySettingsAsync(new(InputSettings.Load(settingsFile) with{Channel=1},fake));}catch(IOException){rejected=true;}finally{w.SettingsApplyStageForTest=null;}
    Check(rejected&&w.Surface.Canvas.SessionId==canvas&&w.Surface.CanvasSize==(display.Width,display.Height)&&File.ReadAllText(settingsFile)==before&&w.Monitor.Starts==starts,"preparation replaced active state");
    Check(w.Preview.Snapshot.State==PreviewState.Playing&&w.Preview.PlaybackSessionId==playback,"preparation failure stopped playback");await w.Preview.StopAsync();
   });
   await Test("staging write failure does not change runtime state",async()=>{
    var canvas=w.Surface.Canvas.SessionId;var clock=w.Monitor.Clock;var before=File.ReadAllText(settingsFile);int starts=w.Monitor.Starts;
    var args=Program.Arguments.ToArray();int index=Array.IndexOf(Program.Arguments,"--settings");
    bool rejected=false;try{Program.Arguments[index+1]=Path.Combine(folder,"missing-parent","settings.json");await w.ApplySettingsAsync(new(InputSettings.Load(settingsFile) with{Channel=1},fake));}catch(DirectoryNotFoundException){rejected=true;}finally{Program.Arguments=args;}
    Check(rejected&&w.Surface.Canvas.SessionId==canvas&&ReferenceEquals(w.Monitor.Clock,clock)&&w.Monitor.Starts==starts&&File.ReadAllText(settingsFile)==before,"failed staging touched active state");
   });
   await Test("input startup failure restores original clock and discards staged canvas",async()=>{
    var canvas=w.Surface.Canvas.SessionId;var clock=w.Monitor.Clock;var before=File.ReadAllText(settingsFile);int starts=w.Monitor.Starts;
    w.Monitor.BeforeStartForTest=()=>{w.Monitor.BeforeStartForTest=null;throw new IOException("Synthetic input start failure");};bool rejected=false;
    try{await w.ApplySettingsAsync(new(InputSettings.Load(settingsFile) with{Channel=1},fake));}catch(IOException){rejected=true;}
    Check(rejected&&w.Surface.Canvas.SessionId==canvas&&ReferenceEquals(w.Monitor.Clock,clock)&&w.Monitor.Starts==starts+1&&File.ReadAllText(settingsFile)==before,"input failure left partial state");
   });
   await Test("settings replacement failure preserves canvas cover fade selection and saved values",async()=>{
    string red=Path.Combine(folder,"cover-red.png"),blue=Path.Combine(folder,"cover-blue.png");
    RegressionWindow.SavePng(red,new byte[]{0,0,255,255},1,1);RegressionWindow.SavePng(blue,new byte[]{255,0,0,255},1,1);
    await w.ApplySettingsAsync(new(InputSettings.Load(settingsFile) with{CoverPath=red,FadeSeconds=1},display));
    var current=InputSettings.Load(settingsFile);var next=current with{Channel=1,CoverPath=blue,FadeSeconds=2};var image=w.Cover.Image;var clock=w.Monitor.Clock;var canvas=w.Surface.Canvas.SessionId;var before=File.ReadAllText(settingsFile);
    using var fileLock=new FileStream(settingsFile,FileMode.Open,FileAccess.Read,FileShare.Read);
    bool rejected=false;try{await w.ApplySettingsAsync(new(next,fake));}catch(IOException){rejected=true;}catch(UnauthorizedAccessException){rejected=true;}
    Check(rejected&&w.Surface.Canvas.SessionId==canvas&&w.Surface.CanvasSize==(display.Width,display.Height),"failed save committed canvas");
    Check(ReferenceEquals(w.Cover.Image,image)&&w.FindControl<NumericUpDown>("FadeSeconds")!.Value==1&&ReferenceEquals(w.Monitor.Clock,clock),"failed save committed cover/fade/input");
    Check(File.ReadAllText(settingsFile)==before&&SavedDisplay()==display.Name,"failed save changed persisted state");
    Check(Directory.GetFiles(Path.GetDirectoryName(settingsFile)!,Path.GetFileName(settingsFile)+".*.tmp").Length==0,"staged settings leaked");
   });
   await Test("retry commits prepared resources and UI without duplicate fade saves",async()=>{
    var current=InputSettings.Load(settingsFile);var next=current with{Channel=1,CoverPath=Path.Combine(folder,"cover-blue.png"),FadeSeconds=2};var canvas=w.Surface.Canvas.SessionId;
    await w.ApplySettingsAsync(new(next,fake));Check(w.Surface.Canvas.SessionId!=canvas&&w.Surface.CanvasSize==(640,360),"new canvas not committed");
    Check(InputSettings.Load(settingsFile)==next,"successful retry persisted values mismatch");Check(SavedDisplay()==fake.Name,"successful retry display mismatch");Check(w.Cover.Image?.Path==Path.GetFullPath(next.CoverPath),"successful retry cover mismatch");Check(w.FindControl<NumericUpDown>("FadeSeconds")!.Value==2,"successful retry fade mismatch");
    w.SettingsDisplaysForTest=null;await w.ApplySettingsAsync(new(new(),display));
   });
  }catch(Exception e){failed=true;File.WriteAllText(Path.Combine(folder,"fatal.txt"),e.ToString());}
  finally{w.SettingsApplyStageForTest=null;w.SettingsDisplaysForTest=null;w.Monitor.BeforeStartForTest=null;if(d?.IsVisible==true){d.Close(null);if(shown!=null)await shown;}await w.Coordinator.DisposeAsync();await w.Preview.DisposeAsync();await w.Monitor.DisposeAsync();MainWindow.Shutdown(failed?1:0);}
 }
}
