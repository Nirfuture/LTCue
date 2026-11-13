using System.Diagnostics;
using System.Text.Json;
using LtcPlayer.App;
namespace LtcPlayer.Avalonia;
public static class MonitorVerification
{
 public static async Task RunAsync(MainWindow window,string folder)
 {
  Directory.CreateDirectory(folder);bool failed=false;var results=new List<object>();
  var host=window.Surface;var target=host.BeginProgram();
  try{
   var display=VideoRenderer.Displays()[0];
   using(var renderer=new VideoRenderer(display)){
    renderer.AttachMonitor(target);
    foreach(var color in new[]{(B:0,G:0,R:255),(B:0,G:255,R:0),(B:255,G:0,R:0),(B:0,G:0,R:0)}){
     await Task.Delay(85);var pixels=new byte[display.Width*display.Height*4];for(int i=0;i<pixels.Length;i+=4){pixels[i]=(byte)color.B;pixels[i+1]=(byte)color.G;pixels[i+2]=(byte)color.R;pixels[i+3]=255;}
     ulong before=renderer.MonitorStatus().Rendered;renderer.Present(pixels);await PreviewRecoveryVerification.Fresh(renderer,before);var actual=renderer.MonitorReadback();var s=renderer.MonitorStats();int p=(s.Height/2*s.Width+s.Width/2)*4;
     if(actual[p]!=color.B||actual[p+1]!=color.G||actual[p+2]!=color.R)throw new IOException("GPU mirror color mismatch");
     results.Add(new{color,s.Width,s.Height,s.Frames,s.Error});
    }
   }
   var metrics=new List<object>();foreach(bool monitor in (Program.Arguments.Contains("--monitor-repeat")?new[]{false,true,true,false,false,true}:new[]{false,true})){
    using var renderer=new VideoRenderer(display);if(monitor)renderer.AttachMonitor(target);var data=new byte[display.Width*display.Height*4];for(int warm=0;warm<10;warm++){renderer.Present(data);await Task.Delay(1);}var process=Process.GetCurrentProcess();double cpuStart=process.TotalProcessorTime.TotalMilliseconds;var intervals=new List<double>();var clock=Stopwatch.StartNew();double last=clock.Elapsed.TotalMilliseconds;
    for(int i=0;i<45;i++){renderer.Present(data);double now=clock.Elapsed.TotalMilliseconds;intervals.Add(now-last);last=now;await Task.Delay(1);}
    metrics.Add(new{monitor,Frames=45,Elapsed=clock.Elapsed.TotalSeconds,MeanIntervalMs=intervals.Average(),MaxIntervalMs=intervals.Max(),Over50ms=intervals.Count(x=>x>50),CpuMilliseconds=process.TotalProcessorTime.TotalMilliseconds-cpuStart,PrivateBytes=process.PrivateMemorySize64,Monitor=renderer.MonitorStats()});
   }
   File.WriteAllText(Path.Combine(folder,"gpu-monitor.json"),JsonSerializer.Serialize(new{Colors=results,Timings=metrics,Note="GPU readback verification only; host timings, not physical display latency"},new JsonSerializerOptions{WriteIndented=true,IncludeFields=true}));
  }catch(Exception e){failed=true;File.WriteAllText(Path.Combine(folder,"fatal.txt"),e.ToString());}
  finally{host.EndProgram();File.WriteAllText(Path.Combine(folder,"exit-code.txt"),failed?"1":"0");MainWindow.Shutdown(failed?1:0);}
 }
}

