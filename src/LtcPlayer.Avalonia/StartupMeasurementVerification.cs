using System.Text.Json;
using LtcPlayer.App;
namespace LtcPlayer.Avalonia;
public partial class MainWindow{internal Task MeasureManualStartAsync(CueRow row)=>PlayManualAsync(row,false);}
public static class StartupMeasurementVerification
{
 public static async Task RunAsync(MainWindow w,string folder){
  Directory.CreateDirectory(folder);bool failed=false;var samples=new List<object>();
  async Task Wait(Func<bool> predicate,int seconds=15){double until=MasterClock.Timestamp()+seconds;while(!predicate()){if(w.SharedAudio.State==SharedAudioState.Error)throw new IOException(w.SharedAudio.Notice);if(MasterClock.Timestamp()>until)throw new TimeoutException("Startup measurement timed out");await Task.Delay(5);}}
  try{
   string media=Program.Option("--media")??throw new ArgumentException("--media required");
   var row=new CueRow{FilePath=media};await ShowProgramService.RefreshAsync(row,CancellationToken.None);w.Editor.Add(row);
   var outputs=AudioOutputSink.Devices();var device=outputs.FirstOrDefault(x=>x.Name.Contains("LG ULTRAFINE",StringComparison.OrdinalIgnoreCase));if(string.IsNullOrEmpty(device.Id))device=outputs.First(x=>x.IsDefault!=0);
   var displays=VideoRenderer.Displays();var display=displays.FirstOrDefault(x=>x.Height>x.Width);if(display.Width==0)display=displays.First(x=>x.Primary!=0);
   await w.ApplySettingsAsync(new(new(Rate:"29.97 DF",AudioDeviceId:device.Id),display));
   // Keep LTC waiting. No hardware input, no external output, no audible tone.
   await w.Monitor.ConfigureAsync(new(Rate:"29.97 DF"),async(_,token)=>await Task.Delay(Timeout.Infinite,token));
   StartupMeasurementTrace.Begin("setup");
   await w.SetSharedAudioAsync(true,device.Id,.3f,false,true);
   File.WriteAllText(Path.Combine(folder,"conditions.json"),JsonSerializer.Serialize(new{Mode="manual; UI row-play path",Material=Path.GetFileName(media),row.PixelWidth,row.PixelHeight,row.FrameRate,row.Duration,row.Codec,CanvasWidth=w.Surface.CanvasSize.Width,CanvasHeight=w.Surface.CanvasSize.Height,AudioDevice=device.Name,DeviceInitializedBeforePlay=true,HardwareGain=0,ExternalOutput=false,LtcInput="synthetic waiting; no hardware capture",Attempts=3},new JsonSerializerOptions{WriteIndented=true}));
   for(int i=1;i<=3;i++){
    if(i>1){await w.Preview.StopAsync();await Wait(()=>w.SharedAudio.State==SharedAudioState.Waiting);await Task.Delay(100);}
    StartupMeasurementTrace.Begin($"trial-{i}");
    await w.MeasureManualStartAsync(row);
    await Wait(()=>w.SharedAudio.State==SharedAudioState.Playing&&w.SharedAudio.PlayedPts is >0);
    await Task.Delay(250);
    samples.Add(new{Trial=$"trial-{i}",w.Preview.Snapshot.Position,w.Preview.Snapshot.FramePts,w.SharedAudio.PlayedPts,w.SharedAudio.ErrorSeconds,w.SharedAudio.PreparationStarts,w.SharedAudio.Reseeks});
   }
   await w.Preview.StopAsync();await Wait(()=>w.SharedAudio.State==SharedAudioState.Waiting);
  }catch(Exception e){failed=true;File.WriteAllText(Path.Combine(folder,"failure.txt"),e.ToString());}
  finally{
   StartupMeasurementTrace.End();
   File.WriteAllText(Path.Combine(folder,"transitions.json"),JsonSerializer.Serialize(StartupMeasurementTrace.Snapshot(),new JsonSerializerOptions{WriteIndented=true}));
   File.WriteAllText(Path.Combine(folder,"samples.json"),JsonSerializer.Serialize(samples,new JsonSerializerOptions{WriteIndented=true}));
   await w.Coordinator.DisposeAsync();await w.Preview.DisposeAsync();await w.Monitor.DisposeAsync();MainWindow.Shutdown(failed?1:0);
  }
 }
}
