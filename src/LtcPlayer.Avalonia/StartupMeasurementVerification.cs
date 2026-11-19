using System.Text.Json;
using LtcPlayer.App;
namespace LtcPlayer.Avalonia;
public partial class MainWindow{internal Task MeasureManualStartAsync(CueRow row)=>PlayManualAsync(row,false);}
public static class StartupMeasurementVerification
{
 static object Resources(){using var p=System.Diagnostics.Process.GetCurrentProcess();p.Refresh();return new{WorkingSet=p.WorkingSet64,PrivateBytes=p.PrivateMemorySize64,PeakWorkingSet=p.PeakWorkingSet64,Handles=p.HandleCount,AudioProcesses=AudioDecodeSession.ActiveProcesses,PeakAudioProcesses=AudioDecodeSession.PeakProcesses,DecodeWindows=AudioDecodeSession.ActiveDecoders,PeakDecodeWindows=AudioDecodeSession.PeakDecoders,AudioStreams=AudioOutputSink.ActiveStreams};}
 public static async Task RunAsync(MainWindow w,string folder){
  Directory.CreateDirectory(folder);bool failed=false;var samples=new List<object>();
  async Task Wait(Func<bool> predicate,int seconds=15){double until=MasterClock.Timestamp()+seconds;while(!predicate()){if(w.SharedAudio.State==SharedAudioState.Error)throw new IOException(w.SharedAudio.Notice);if(MasterClock.Timestamp()>until)throw new TimeoutException("Startup measurement timed out");await Task.Delay(5);}}
  try{
   bool audioEnabled=(Program.Option("--startup-audio")??"on") switch {"on"=>true,"off"=>false,_=>throw new ArgumentException("--startup-audio must be on or off")};
   MediaProbeTrace.Observer=(name,note)=>StartupMeasurementTrace.Mark(name,note:note);
   StartupMeasurementTrace.Begin("registration");double registrationStart=MasterClock.Timestamp();
   string media=Program.Option("--media")??throw new ArgumentException("--media required");
   bool switching=Program.Option("--startup-switch")=="on";
   string? materialList=Program.Option("--startup-materials");
   var materialPaths=materialList!=null?File.ReadAllLines(materialList).Where(x=>!string.IsNullOrWhiteSpace(x)).ToArray():switching?Enumerable.Range(1,4).Select(n=>Path.Combine(Path.GetDirectoryName(media)!,$"HAP_{n}_1080p_2997_10s.mov")).ToArray():new[]{media};
   var rows=new List<CueRow>();
   foreach(string file in materialPaths){
    var added=new CueRow{FilePath=file};await ShowProgramService.RefreshAsync(added,CancellationToken.None,w.Preview.Analysis);w.Editor.Add(added);rows.Add(added);
   }
   double registrationMs=(MasterClock.Timestamp()-registrationStart)*1000;
   var row=rows[0];
   var outputs=AudioOutputSink.Devices();var device=outputs.FirstOrDefault(x=>x.Name.Contains("LG ULTRAFINE",StringComparison.OrdinalIgnoreCase));if(string.IsNullOrEmpty(device.Id))device=outputs.First(x=>x.IsDefault!=0);
   var displays=VideoRenderer.Displays();var display=displays.FirstOrDefault(x=>x.Height>x.Width);if(display.Width==0)display=displays.First(x=>x.Primary!=0);
   await w.ApplySettingsAsync(new(new(Rate:"29.97 DF",AudioDeviceId:device.Id),display));
   // Keep LTC waiting. No hardware input, no external output, no audible tone.
   await w.Monitor.ConfigureAsync(new(Rate:"29.97 DF"),async(_,token)=>await Task.Delay(Timeout.Infinite,token));
   StartupMeasurementTrace.Begin("setup");
   await w.SetSharedAudioAsync(audioEnabled,device.Id,.3f,false,true);
   File.WriteAllText(Path.Combine(folder,"conditions.json"),JsonSerializer.Serialize(new{Mode="manual; UI row-play path",Material=Path.GetFileName(media),row.PixelWidth,row.PixelHeight,row.FrameRate,row.Duration,row.Codec,CanvasWidth=w.Surface.CanvasSize.Width,CanvasHeight=w.Surface.CanvasSize.Height,AudioEnabled=audioEnabled,AudioDevice=device.Name,DeviceInitializedBeforePlay=audioEnabled,HardwareGain=0,ExternalOutput=false,LtcInput="synthetic waiting; no hardware capture",Attempts=5,Switching=switching,RegistrationMs=registrationMs,ResourcesBefore=Resources()},new JsonSerializerOptions{WriteIndented=true}));
   var sequence=switching?Enumerable.Range(0,rows.Count).Append(0).ToArray():new[]{0};
   foreach(int rowIndex in sequence){row=rows[rowIndex];
   for(int i=1;i<=5;i++){
    if(i>1||samples.Count>0){await w.Preview.StopAsync();if(audioEnabled)await Wait(()=>w.SharedAudio.State==SharedAudioState.Waiting);await Task.Delay(100);}
    StartupMeasurementTrace.Begin($"source-{rowIndex}-visit-{samples.Count/5}-trial-{i}");
    long builds=w.Preview.IndexCache.Builds,hits=w.Preview.IndexCache.Hits;
    await w.MeasureManualStartAsync(row);
    await Wait(()=>w.Preview.Snapshot.State==PreviewState.Playing&&!w.Preview.IsBusy);
    if(audioEnabled)await Wait(()=>w.SharedAudio.State==SharedAudioState.Playing&&w.SharedAudio.PlayedPts is >0);
    await Task.Delay(250);
    samples.Add(new{Trial=$"source-{rowIndex}-visit-{samples.Count/5}-trial-{i}",Material=row.Name,IndexBuilds=w.Preview.IndexCache.Builds-builds,IndexHits=w.Preview.IndexCache.Hits-hits,w.Preview.Snapshot.Position,w.Preview.Snapshot.FramePts,w.SharedAudio.PlayedPts,w.SharedAudio.ErrorSeconds,w.SharedAudio.PreparationStarts,w.SharedAudio.Reseeks,Resources=Resources()});
   }
   }
   await w.Preview.StopAsync();if(audioEnabled)await Wait(()=>w.SharedAudio.State==SharedAudioState.Waiting);
   File.WriteAllText(Path.Combine(folder,"resources-stopped.json"),JsonSerializer.Serialize(Resources(),new JsonSerializerOptions{WriteIndented=true}));
  }catch(Exception e){failed=true;File.WriteAllText(Path.Combine(folder,"failure.txt"),e.ToString());}
  finally{
   StartupMeasurementTrace.End();MediaProbeTrace.Observer=null;
   File.WriteAllText(Path.Combine(folder,"transitions.json"),JsonSerializer.Serialize(StartupMeasurementTrace.Snapshot(),new JsonSerializerOptions{WriteIndented=true}));
   File.WriteAllText(Path.Combine(folder,"samples.json"),JsonSerializer.Serialize(samples,new JsonSerializerOptions{WriteIndented=true}));
   await w.Coordinator.DisposeAsync();await w.Preview.DisposeAsync();await w.Monitor.DisposeAsync();File.WriteAllText(Path.Combine(folder,"resources-disposed.json"),JsonSerializer.Serialize(Resources(),new JsonSerializerOptions{WriteIndented=true}));MainWindow.Shutdown(failed?1:0);
  }
 }
}
