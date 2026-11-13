using System.Text.Json;
using LtcPlayer.App;
namespace LtcPlayer.Avalonia;

public static class SyncRecoveryVerification
{
 public static async Task RunAsync(MainWindow window,string folder)
 {
  Directory.CreateDirectory(folder);var stages=new List<object>();Exception? failure=null;
  using var stop=new CancellationTokenSource();Task? input=null,playback=null;bool surface=false;
  ProgramObservation? latest=null;int enabled=1,offsetFrames=0;
  var clock=new MasterClock(TimecodeRate.Modes.First(r=>r.Name=="29.97 DF"),.2);
  async Task Until(Func<bool> condition){for(int i=0;i<500;i++){if(playback?.IsFaulted==true)await playback;if(condition())return;await Task.Delay(20);}throw new TimeoutException(JsonSerializer.Serialize(latest));}
  void Check(bool value,string message){if(!value)throw new IOException(message);}
  async Task Stable(string stage){int generation=clock.Read().Generation;await Until(()=>latest is{State:SyncState.LOCKED,FramePts:not null,LagSeconds:<.15,Recovering:false}&&latest.InputGeneration==generation);stages.Add(new{Stage=stage,Observation=latest});}
  try {
   string media=Program.Option("--media")??throw new IOException("--media missing");
   var info=await new MediaDecoder().InspectAsync(media,stop.Token);
   Check(info.Duration>=25,"Use the 30-second HAP sync fixture");
   double start=MasterClock.Timestamp();
   input=Task.Run(async()=>{int previous=-1;try{while(!stop.IsCancellationRequested){
    int frame=(int)((MasterClock.Timestamp()-start)*30000/1001);if(frame!=previous&&Volatile.Read(ref enabled)!=0){previous=frame;int tc=frame+Volatile.Read(ref offsetFrames);clock.Accept(new TcFrame{Seconds=tc/30,Frame=tc%30,Drop=1},start+(frame+1)*1001/30000.0);}
    await Task.Delay(4,stop.Token);
   }}catch(OperationCanceledException)when(stop.IsCancellationRequested){}});
   var target=window.Surface.BeginProgram();surface=true;
   var display=VideoRenderer.Displays().First(d=>d.Primary!=0);
   playback=new ProgramPlayback(new UiDispatcher()).RunAsync([new(Guid.NewGuid(),media,0,info.Duration,info.FrameRate,info.Codec)],display,clock,0,o=>Volatile.Write(ref latest,o),stop.Token,false,target);
   await Stable("continuous input / actual HAP GPU output");
   for(int resumeCycle=0;resumeCycle<3;resumeCycle++){
   Volatile.Write(ref enabled,0);await Until(()=>latest?.State==SyncState.LOST);var held=latest!;
   await Task.Delay(200);Check(latest!.FramePts==held.FramePts&&latest.Presented==held.Presented,"Input loss changed held output");
   stages.Add(new{Stage="input loss holds output",Observation=latest});
   double resumedAt=MasterClock.Timestamp();int beforeResume=latest!.Presented;int beforeGeneration=clock.Read().Generation;
   Volatile.Write(ref enabled,1);await Until(()=>clock.Read().Generation>beforeGeneration);
   await Until(()=>latest!.Presented>beforeResume&&latest.InputGeneration>beforeGeneration);
   double resumeMilliseconds=(MasterClock.Timestamp()-resumedAt)*1000;
   stages.Add(new{Stage="resume to first new video frame",Milliseconds=resumeMilliseconds,Observation=latest});
   await Stable("input resumed");
   }
   int generation=clock.Read().Generation;Volatile.Write(ref offsetFrames,30);await Until(()=>clock.Read().Generation>generation);await Stable("forward jump");
   generation=clock.Read().Generation;Volatile.Write(ref offsetFrames,0);await Until(()=>clock.Read().Generation>generation);await Stable("backward jump");
  }catch(Exception e){failure=e;}
  finally {
   stop.Cancel();if(input!=null)await input;
   if(playback!=null)try{await playback;}catch(OperationCanceledException){}catch(Exception e){failure??=e;}
   if(surface)window.Surface.EndProgram();await window.Monitor.DisposeAsync();
   File.WriteAllText(Path.Combine(folder,"sync-output.json"),JsonSerializer.Serialize(new{Passed=failure==null,Stages=stages,Error=failure?.ToString()},new JsonSerializerOptions{WriteIndented=true}));
   MainWindow.Shutdown(failure==null?0:1);
  }
 }
}
