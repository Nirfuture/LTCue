using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using LtcPlayer.App;
namespace LtcPlayer.Avalonia;
public static class UiUpdateVerification
{
 static void Check(bool value,string message){if(!value)throw new InvalidDataException(message);}
 static async Task Until(Func<bool> action){for(int i=0;i<400;i++){if(action())return;await Task.Delay(15);}throw new TimeoutException();}
 public static async Task RunAsync(MainWindow w,string folder)
 {
  Directory.CreateDirectory(folder);var results=new List<object>();bool failed=false;
  async Task Test(string name,Func<Task<object>> run){try{results.Add(new{test=name,status="PASS",detail=await run()});}catch(Exception e){failed=true;results.Add(new{test=name,status="FAIL",detail=e.ToString()});}await File.WriteAllTextAsync(Path.Combine(folder,"ui-results.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));}
  try{
   foreach(var mode in TimecodeRate.Modes)await Test("idle LTC / "+mode.Name,async()=>{
    await using var monitor=new LtcMonitorService();await monitor.ConfigureAsync(new(Rate:mode.Name),async(clock,token)=>{clock.Accept(new TcFrame{Hours=1,Drop=mode.Drop?1:0},MasterClock.Timestamp());await Task.Delay(Timeout.Infinite,token);});
    await Until(()=>monitor.Clock.Read().Label==(mode.Drop?"01:00:00;00":"01:00:00:00"));await Task.Delay(90);return new{monitor.Starts,State=monitor.Clock.Read().State};
   });
   await Test("single GPU surface prototype",async()=>{
    var target=w.Surface.BeginProgram();try{using var renderer=new VideoRenderer(VideoRenderer.Displays()[0]);renderer.AttachMonitor(target);var d=VideoRenderer.Displays()[0];var data=new byte[d.Width*d.Height*4];for(int i=0;i<data.Length;i+=4){data[i+1]=255;data[i+3]=255;}ulong before=renderer.MonitorStatus().Rendered;renderer.Present(data);await PreviewRecoveryVerification.Fresh(renderer,before);var bytes=renderer.MonitorReadback();var stats=renderer.MonitorStats();int p=(stats.Height/2*stats.Width+stats.Width/2)*4;Check(bytes[p+1]==255&&bytes[p]==0,"monitor frame wrong");await Task.Delay(1);return new{stats.Width,stats.Height,stats.Frames};}finally{w.Surface.EndProgram();}
   });
   await Test("monitor lifetime / confirmation cancel and accept",async()=>{
    string fixtures=Program.Option("--fixtures")??Path.Combine(folder,"fixtures");var cue=new CueRow{FilePath=Path.Combine(fixtures,"h264-aac.mp4")};cue.SetStart("00:00:00:00");await ShowProgramService.RefreshAsync(cue,CancellationToken.None);w.Editor.Add(cue);
    await w.Monitor.ConfigureAsync(new(Rate:"25"),async(clock,token)=>{int i=0;while(!token.IsCancellationRequested){clock.Accept(new TcFrame{Seconds=(i/25)%60,Frame=i%25},MasterClock.Timestamp());i++;await Task.Delay(40,token);}});
    int starts=w.Monitor.Starts;await w.ArmAsync();await Until(()=>w.LatestObservation?.Presented>5);
    var decision=new TaskCompletionSource<bool>();w.ConfirmStopForTest=_=>decision.Task;int before=w.LatestObservation!.Presented;var request=w.RequestStopAsync(false);
    await Task.Delay(250);Check(w.LatestObservation!.Presented>before,"confirmation blocked output");Check(w.Delivery==DeliveryState.ConfirmingStop,"confirmation state");decision.SetResult(false);Check(!await request&&w.IsArmed,"cancel stopped output");
    Check(w.Monitor.Starts==starts,"input reopened on ARM");w.ConfirmStopForTest=_=>Task.FromResult(true);Check(await w.RequestStopAsync(false)&&!w.IsArmed,"accept did not stop");Check(w.Monitor.Starts==starts,"input reopened on stop");var a=w.Monitor.Clock.Read().Label;await Task.Delay(90);Check(w.Monitor.Clock.Read().Label!=a,"idle monitoring stopped");
    return new{InputStarts=starts,ContinuedDuringConfirmation=true,ContinuedAfterStop=true};
   });
   await Test("HAP/HAPQ GPU monitor versus presented source",async()=>{
    var target=w.Surface.BeginProgram();var measurements=new List<object>();
    try{var display=VideoRenderer.Displays()[0];using var renderer=new VideoRenderer(display);renderer.AttachMonitor(target);
     string fixtures=Program.Option("--fixtures")??Path.Combine(folder,"fixtures");
     foreach(string file in new[]{"hap-pcm.mov","hap-q-silent.mov"}){
      await Task.Delay(90);await foreach(var frame in new MediaDecoder().DecodeAsync(Path.Combine(fixtures,file),display.Width,display.Height,CancellationToken.None)){
       ulong before=renderer.MonitorStatus().Rendered;renderer.Present(frame);await PreviewRecoveryVerification.Fresh(renderer,before);var src=renderer.Readback();var dst=renderer.MonitorReadback();var size=renderer.MonitorStats();
       double scale=Math.Min(size.Width/(double)display.Width,size.Height/(double)display.Height),innerWidth=Math.Round(display.Width*scale,MidpointRounding.AwayFromZero),innerHeight=Math.Round(display.Height*scale,MidpointRounding.AwayFromZero),left=Math.Floor((size.Width-innerWidth)/2),top=Math.Floor((size.Height-innerHeight)/2);long sum=0;int max=0;object? worst=null;
       for(int y=0;y<size.Height;y++)for(int x=0;x<size.Width;x++)for(int c=0;c<3;c++){
        double sx=(x+.5-left)/innerWidth*display.Width-.5,sy=(y+.5-top)/innerHeight*display.Height-.5;double expected=0;
        if(x+.5>=left&&x+.5<left+innerWidth&&y+.5>=top&&y+.5<top+innerHeight){int x0=(int)Math.Floor(sx),y0=(int)Math.Floor(sy);double fx=sx-x0,fy=sy-y0;
         int Sample(int px,int py)=>src[(Math.Clamp(py,0,display.Height-1)*display.Width+Math.Clamp(px,0,display.Width-1))*4+c];
         expected=(Sample(x0,y0)*(1-fx)+Sample(x0+1,y0)*fx)*(1-fy)+(Sample(x0,y0+1)*(1-fx)+Sample(x0+1,y0+1)*fx)*fy;
        }
        int error=Math.Abs(dst[(y*size.Width+x)*4+c]-(int)Math.Round(expected));sum+=error;if(error>max){max=error;worst=new{x,y,c,expected,Actual=dst[(y*size.Width+x)*4+c],sx,sy};}
       }
       double mean=sum/(double)(size.Width*size.Height*3);File.WriteAllText(Path.Combine(folder,file+"-scaling.json"),JsonSerializer.Serialize(new{mean,max,worst}));RegressionWindow.SavePng(Path.Combine(folder,file+"-actual.png"),dst,size.Width,size.Height);Check(mean<=1.5&&max<=8,$"monitor scaling error {mean}/{max}");
       RegressionWindow.SavePng(Path.Combine(folder,file+"-monitor.png"),dst,size.Width,size.Height);measurements.Add(new{file,Mean=mean,Max=max,Compressed=frame.TextureFormat});break;
      }
     }return measurements;
    }finally{w.Surface.EndProgram();}
   });
   await Test("native Escape/WM_CLOSE requests preserve output",async()=>{
    using var renderer=new VideoRenderer(VideoRenderer.Displays()[0]);renderer.EnableStopConfirmation();int count=0;
    foreach(bool escape in new[]{false,true}){renderer.TestNativeStopRequest(escape);Check(renderer.PollStopRequest(),"native stop bypassed request");var d=VideoRenderer.Displays()[0];renderer.Present(new byte[d.Width*d.Height*4]);count++;}await Task.Delay(1);return new{Requests=count,OutputStillAlive=true};
   });
   await Test("modal message loop keeps LTC alive",async()=>{
    var settings=new SettingsDialog(new(Rate:"25"),VideoRenderer.Displays()[0],true);string before=w.Monitor.Clock.Read().Label;var dialog=settings.ShowDialog<SettingsResult?>(w);
    await Task.Delay(180);Check(w.Monitor.Clock.Read().Label!=before,"modal blocked receiver");settings.Close(null);Check(await dialog==null,"modal changed settings");return "read-only settings closed without mutation";
   });
   await Test("settings validation and output persistence",async()=>{
    var display=VideoRenderer.Displays().Last();int starts=w.Monitor.Starts;bool rejected=false;
    try{await w.ApplySettingsAsync(new(new(Rate:"25",Freewheel:-1),display));}catch(ArgumentOutOfRangeException){rejected=true;}
    Check(rejected&&w.Monitor.Starts==starts,"invalid setting restarted receiver");
    await w.ApplySettingsAsync(new(new(Rate:"25"),display));string path=Program.Option("--settings")!;
    using var doc=JsonDocument.Parse(File.ReadAllText(path));Check(doc.RootElement.GetProperty("OutputDisplayName").GetString()==display.Name,"output not persisted");Check(InputSettings.Load(path).Rate=="25","legacy settings incompatible");return new{InvalidRejected=rejected,SavedDisplay=display.Name};
   });
  }finally{w.ConfirmStopForTest=null;await w.DisarmAsync();await w.Monitor.DisposeAsync();File.WriteAllText(Path.Combine(folder,"ui-exit-code.txt"),failed?"1":"0");MainWindow.Shutdown(failed?1:0);}
 }

}



