using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using LtcPlayer.App;
namespace LtcPlayer.Avalonia;
public static class OutputModeVerification
{
 static void Check(bool value,string message){if(!value)throw new IOException(message);}
 public static async Task RunAsync(MainWindow window,string folder){
  Directory.CreateDirectory(folder);bool failed=false;var results=new List<object>();
  async Task Test(string name,Func<Task<object>> body){try{results.Add(new{test=name,status="PASS",detail=await body()});}catch(Exception e){failed=true;results.Add(new{test=name,status="FAIL",detail=e.ToString()});}}
  try{
   if(!Program.VerificationOutput)throw new InvalidOperationException("Output-mode verification is limited to small-window output");
   await Test("Legacy settings default to Standard",()=>{
    string file=Path.Combine(folder,"legacy.json");File.WriteAllText(file,"{\"Rate\":\"29.97 DF\"}");Check(InputSettings.Load(file).OutputMode==OutputPresentationMode.Standard,"legacy default");return Task.FromResult<object>("Standard");
   });
   await Test("Both output modes survive save and load",()=>{
    foreach(var mode in Enum.GetValues<OutputPresentationMode>()){string file=Path.Combine(folder,mode+".json");var settings=new InputSettings(OutputMode:mode);settings.Save(file);Check(InputSettings.Load(file)==settings,"mode roundtrip");Check(File.ReadAllText(file).Contains(mode.ToString()),"readable mode in JSON");}return Task.FromResult<object>("Standard / Compatibility");
   });
   await Test("Invalid mode is rejected",()=>{
    string file=Path.Combine(folder,"invalid.json");File.WriteAllText(file,"{\"OutputMode\":55}");bool rejected=false;try{InputSettings.Load(file);}catch(InvalidDataException){rejected=true;}Check(rejected,"invalid mode accepted");return Task.FromResult<object>("Rejected");
   });
   await Test("Settings selection applies / output lock",async()=>{
    var display=VideoRenderer.Displays()[0];var dialog=new SettingsDialog(new(Rate:"29.97 DF"),display,false);var task=dialog.ShowDialog<SettingsResult?>(window);
    dialog.ModeSelector.SelectedIndex=1;
    var apply=((Control)dialog.Content!).GetLogicalDescendants().OfType<Button>().Single(b=>Equals(b.Content,"適用"));apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    for(int i=0;i<100&&dialog.LastAppliedResult==null;i++)await Task.Delay(10);var result=dialog.LastAppliedResult;Check(dialog.IsVisible,"apply closed settings");dialog.Close(null);await task;Check(result?.Settings.OutputMode==OutputPresentationMode.Compatibility,"selected mode did not apply");
    var locked=new SettingsDialog(result!.Settings,display,true);Check(!locked.ModeSelector.IsEffectivelyEnabled&&locked.ModeSelector.SelectedIndex==1,"active output mode not read-only");
    return new{Selected=result.Settings.OutputMode,Locked=true};
   });
   var cue=new CueRow{FilePath=Path.Combine(AppContext.BaseDirectory,"TestMedia","HAP_1_1080p_2997_10s.mov")};cue.SetStart("00:00:00:00");await ShowProgramService.RefreshAsync(cue,CancellationToken.None);window.Editor.Add(cue);
   foreach(var mode in Enum.GetValues<OutputPresentationMode>()){
    await Test("Selected mode reaches renderer, telemetry and main window / "+mode,async()=>{
     var display=VideoRenderer.Displays()[0];await window.ApplySettingsAsync(new(new(Rate:"29.97 DF",OutputMode:mode),display));
     OutputPresentationMode? actual=null;
     try{
      await window.ArmAsync(async(clock,token)=>{while(!token.IsCancellationRequested){clock.Accept(new TcFrame{Seconds=2,Drop=1},MasterClock.Timestamp());await Task.Delay(33,token);}});
      for(int i=0;i<700&&window.LatestObservation is not {Presented:>2};i++)await Task.Delay(15);
      Check(window.LatestObservation is {Presented:>2},"no frames");actual=window.Surface.Canvas.OutputMode;Check(actual==mode&&window.LatestObservation!.OutputMode==mode,"wrong actual output mode");
      await Task.Delay(150);Check(window.FindControl<TextBlock>("OutputModeLabel")!.Text!.Contains(OutputPresentation.Label(mode)),"main output-mode label");
      bool blocked=false;try{await window.ApplySettingsAsync(new(new(OutputMode:mode),display));}catch(InvalidOperationException){blocked=true;}Check(blocked,"output mode changed while streaming");
      var saved=InputSettings.Load(Program.Option("--settings"));Check(saved.OutputMode==mode,"main settings did not persist mode");
      window.ConfirmStopForTest=_=>Task.FromResult(true);await window.RequestStopAsync(false);return new{Requested=mode,Actual=actual,Persisted=saved.OutputMode};
     }finally{ProgramPlayback.ProbeObserver=null;window.ConfirmStopForTest=null;await window.DisarmAsync();}
    });
   }
  }finally{await window.Monitor.DisposeAsync();File.WriteAllText(Path.Combine(folder,"results.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));MainWindow.Shutdown(failed?1:0);}
 }
}
