using System.Text.Json;
using LtcPlayer.App;
namespace LtcPlayer.Avalonia;
public static class CoverToggleVerification
{
 public static async Task RunAsync(MainWindow w,string folder){
  Directory.CreateDirectory(folder);var results=new List<object>();bool failed=false;
  async Task Until(Func<bool> predicate){double end=MasterClock.Timestamp()+15;while(!predicate()){if(MasterClock.Timestamp()>end)throw new TimeoutException("Playback preparation timeout");await Task.Delay(20);}}
  try{
   string media=Program.Option("--media")!;var row=new CueRow{FilePath=media};await ShowProgramService.RefreshAsync(row,CancellationToken.None);w.Editor.Add(row);
   var display=VideoRenderer.Displays().FirstOrDefault(d=>d.Primary==0);if(display.Width==0)throw new IOException("External display missing");
   await w.ApplySettingsAsync(new(new(Rate:"29.97 DF",AudioVolume:.3),display));
   byte[] image=new byte[64*64*4];for(int i=0;i<image.Length;i+=4){image[i+2]=image[i+3]=255;}w.Cover.Image=new(image,64,64,"diagnostic-solid-red");
   var scenarios=new[]{(Name:"no-toggle",Toggle:false,Fade:0m),(Name:"instant-toggle",Toggle:true,Fade:0m),(Name:"one-second-fade-toggle",Toggle:true,Fade:1m)};
   if(Program.Arguments.Contains("--fade-only"))scenarios=scenarios.Where(s=>s.Fade==1m).ToArray();
   if(Program.Arguments.Contains("--reverse"))Array.Reverse(scenarios);
   foreach(var scenario in scenarios){
    await w.DisarmAsync();await w.Monitor.ConfigureAsync(new(Rate:"29.97 DF"),async(_,t)=>await Task.Delay(Timeout.Infinite,t));await w.StopSyncPreviewAsync();
    w.SetFadeSeconds(scenario.Fade);w.SetCoverVisible(false);await Task.Delay(1100);await w.ChangeAudioAsync(true);await w.ArmAsync();
    await w.Monitor.ConfigureAsync(new(Rate:"29.97 DF"),async(clock,t)=>{double start=MasterClock.Timestamp();int previous=-1;while(!t.IsCancellationRequested){int n=(int)((MasterClock.Timestamp()-start)*30000/1001);if(n==previous){await Task.Delay(1,t);continue;}previous=n;int label=n+(n>=1800?2:0);clock.Accept(new TcFrame{Seconds=label/30%60,Minutes=label/1800,Frame=label%30,Drop=1},MasterClock.Timestamp());await Task.Delay(8,t);}});
    await Until(()=>w.PreviewObservation?.FramePts>1&&w.SharedAudio.State==SharedAudioState.Playing);
    async Task Measure(string phase,Func<Task> body){
     PresentationTimingVerification.Start();var a=w.PreviewObservation!;var external=w.Surface.Canvas.ExternalPresentCountsForTest();long composition=w.Surface.Canvas.CompositionId;
     var process=System.Diagnostics.Process.GetCurrentProcess();var cpu=process.TotalProcessorTime;double began=MasterClock.Timestamp();await body();var b=w.PreviewObservation!;double wall=MasterClock.Timestamp()-began,seconds=b.HostSeconds-a.HostSeconds;
     if(seconds<=0)throw new IOException("No fresh playback observations");var endExternal=w.Surface.Canvas.ExternalPresentCountsForTest();var audio=w.SharedAudio;
     var timing=PresentationTimingVerification.Finish();
     results.Add(new{Timing=timing,scenario.Name,Phase=phase,Media=media,SourceFps=row.FrameRate,FadeSeconds=scenario.Fade,ToggleCount=phase=="during"&&scenario.Toggle?8:0,ToggleIntervalSeconds=.8,FullScreen=!Program.VerificationOutput,Display=display.Name,Width=display.Width,Height=display.Height,OutputMode="Standard",AudioState=audio.State.ToString(),AudioErrorSeconds=audio.ErrorSeconds,Seconds=seconds,UniqueSubmittedFps=((b.Presented-a.Presented)-(b.Repeated-a.Repeated))/seconds,SkipDelta=b.Skipped-a.Skipped,SkipPerSecond=(b.Skipped-a.Skipped)/seconds,ExternalPresentCalls=endExternal.Presents-external.Presents,ExternalRetryCalls=endExternal.Retries-external.Retries,Compositions=w.Surface.Canvas.CompositionId-composition,AppCpuOneCorePercent=(process.TotalProcessorTime-cpu).TotalSeconds/wall*100,LagSeconds=b.LagSeconds,CoverTransitioning=w.Cover.Transitioning});
     File.WriteAllText(Path.Combine(folder,"results.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
    }
    await Measure("during",async()=>{for(int i=0;i<8;i++){if(scenario.Toggle)w.SetCoverVisible(i%2==0);await Task.Delay(800);}});
    w.SetCoverVisible(false);await Task.Delay(1200);
    await Measure("after",()=>Task.Delay(4200));
   }
  }catch(Exception e){failed=true;File.WriteAllText(Path.Combine(folder,"fatal.txt"),e.ToString());}
  finally{w.SetCoverVisible(false);await w.DisarmAsync();await w.DisposeSharedAudioAsync();await w.Monitor.DisposeAsync();MainWindow.Shutdown(failed?1:0);}
 }
}
