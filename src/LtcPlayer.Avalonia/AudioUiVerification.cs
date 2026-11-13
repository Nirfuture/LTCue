using System.Text.Json;
using Avalonia.Controls;
using LtcPlayer.App;
namespace LtcPlayer.Avalonia;
public static class AudioUiVerification
{
 public static async Task RunAsync(MainWindow w,string folder)
 {
  Directory.CreateDirectory(folder);bool failed=false;var results=new List<object>();
  T C<T>(string n)where T:Control=>w.FindControl<T>(n)!;
  void Check(bool ok,string message){if(!ok)throw new IOException(message);}
  async Task Until(Func<bool> condition){double end=MasterClock.Timestamp()+5;while(!condition()){if(MasterClock.Timestamp()>end)throw new TimeoutException(JsonSerializer.Serialize(w.SharedAudio));await Task.Delay(10);}}
  async Task Test(string name,Func<Task> body){try{await body();results.Add(new{test=name,status="PASS"});}catch(Exception e){failed=true;results.Add(new{test=name,status="FAIL",detail=e.ToString()});}File.WriteAllText(Path.Combine(folder,"results.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));}
  try{
   await Test("normal initialization starts off with no audio resources",()=>{Check(w.SharedAudio.State==SharedAudioState.Off&&C<global::Avalonia.Controls.Primitives.ToggleButton>("AudioEnabled").IsChecked!=true&&AudioOutputSink.ActiveStreams==0,"startup audio active");var d=new SettingsDialog(new(),VideoRenderer.Displays()[0],false);Check(d.AudioOutputSelector.ItemCount>1,"no output choices");d.Close();return Task.CompletedTask;});
   await Test("volume and device persist without enable or mute state",async()=>{
    C<Slider>("Volume").Value=.31;var current=InputSettings.Load(Program.Option("--settings"));await w.ApplySettingsAsync(new(current with{AudioDeviceId=AudioOutputSink.Devices()[0].Id},VideoRenderer.Displays().First(d=>d.Primary!=0)));
    string path=Program.Option("--settings")!;var saved=InputSettings.Load(path);Check(Math.Abs(saved.AudioVolume-.31)<.001&&saved.AudioDeviceId!="","settings missing");using var json=JsonDocument.Parse(File.ReadAllText(path));Check(!json.RootElement.TryGetProperty("AudioEnabled",out _),"enable saved");
   });
   var display=VideoRenderer.Displays().First(d=>d.Primary!=0);await w.ApplySettingsAsync(new(new(Rate:"29.97 DF",OutputMode:OutputPresentationMode.Compatibility),display));
   var row=new CueRow{FilePath=Path.Combine(Program.Option("--fixtures")!,"h264-aac.mp4")};await ShowProgramService.RefreshAsync(row,CancellationToken.None);w.Editor.Add(row);C<DataGrid>("CueGrid").SelectedItem=row;await Until(()=>w.Preview.Snapshot.State==PreviewState.Ready);
   await Test("manual playback remains silent until enabled",async()=>{await w.Preview.PlayAsync();await Until(()=>w.Preview.Snapshot.Position>.2);Check(w.Preview.Snapshot.Audio==null&&AudioOutputSink.ActiveStreams==0,"legacy audio opened");await w.ChangeAudioAsync(true);await Until(()=>w.SharedAudio.State==SharedAudioState.Playing);var d=new SettingsDialog(InputSettings.Load(Program.Option("--settings")),VideoRenderer.Displays()[0],false,true);Check(!d.AudioOutputSelector.IsEffectivelyEnabled,"live device selection enabled");d.Close();});
   await Test("output start and canceled stop confirmation preserve audio",async()=>{
    var a=w.SharedAudio;await w.ArmAsync();Check(C<global::Avalonia.Controls.Primitives.ToggleButton>("AudioEnabled").IsEffectivelyVisible&&C<Slider>("Volume").IsEffectivelyVisible,"audio controls hidden during output");w.ConfirmStopForTest=async _=>{await Task.Delay(150);return false;};Check(!await w.RequestStopAsync(false),"canceled stop accepted");Check(w.SharedAudio.SinkSession==a.SinkSession&&w.SharedAudio.PreparationStarts==a.PreparationStarts&&w.SharedAudio.PlayedPts>a.PlayedPts,"audio interrupted");w.ConfirmStopForTest=null;
   });
   await Test("zero volume preserves audio clock during external output",async()=>{C<Slider>("Volume").Value=0;await Until(()=>w.SharedAudio.AppliedGain==0);double at=w.SharedAudio.PlayedPts!.Value;await Task.Delay(150);Check(w.SharedAudio.PlayedPts>at+.1&&w.SharedAudio.State==SharedAudioState.Playing,"zero volume stopped clock");C<Slider>("Volume").Value=.31;await Until(()=>w.SharedAudio.AppliedGain>0);});
   await Test("audio off during external output keeps video alive",async()=>{var video=w.Preview.PlaybackSessionId;double at=w.Preview.Snapshot.Position;await w.ChangeAudioAsync(false);await Task.Delay(150);Check(w.IsArmed&&w.Preview.PlaybackSessionId==video&&w.Preview.Snapshot.Position>at+.1&&AudioOutputSink.ActiveStreams==0,"off disturbed video");var d=new SettingsDialog(InputSettings.Load(Program.Option("--settings")),VideoRenderer.Displays()[0],false);Check(d.AudioOutputSelector.IsEffectivelyEnabled,"device remains locked");d.Close();await w.DisarmAsync();});
   await Test("old settings migrate and malformed volume is rejected",()=>{
    string old=Path.Combine(folder,"old-settings.json");File.WriteAllText(old,"{}");var v=InputSettings.Load(old);Check(v.AudioVolume==.5&&v.AudioDeviceId=="","old settings incompatible");File.WriteAllText(old,"{\"AudioVolume\":2}");bool rejected=false;try{InputSettings.Load(old);}catch(InvalidDataException){rejected=true;}Check(rejected,"invalid volume accepted");return Task.CompletedTask;
   });
  }catch(Exception e){failed=true;File.WriteAllText(Path.Combine(folder,"fatal.txt"),e.ToString());}
  finally{w.ConfirmStopForTest=null;await w.DisarmAsync();await w.StopSyncPreviewAsync();await w.DisposeSharedAudioAsync();await w.Preview.DisposeAsync();await w.Monitor.DisposeAsync();MainWindow.Shutdown(failed?1:0);}
 }
}
