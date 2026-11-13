using System.Text.Json;
using LtcPlayer.App;
namespace LtcPlayer.Avalonia;
public static class AudioStartVerification
{
 public static async Task RunAsync(MainWindow w,string folder){
  Directory.CreateDirectory(folder);bool failed=false;var results=new List<object>();AudioPlayhead head=AudioPlayhead.Empty;
  AudioPlayhead Read(){var p=Volatile.Read(ref head);double now=MasterClock.Timestamp();return p with{Position=p.Estimate(now),HostSeconds=now};}
  try{
   await using var audio=new AudioOutputService(Read,true);await audio.ConfigureAsync(true,"",.5f,false);
   foreach(string file in new[]{"h264-aac.mp4","hap-pcm.mov"})for(int attempt=0;attempt<3;attempt++){
    Volatile.Write(ref head,AudioPlayhead.Empty);double until=MasterClock.Timestamp()+3;while(audio.Snapshot.State!=SharedAudioState.Waiting&&MasterClock.Timestamp()<until)await Task.Delay(2);
    double began=MasterClock.Timestamp();Volatile.Write(ref head,new(Guid.NewGuid(),Guid.NewGuid(),attempt,Path.Combine(Program.Option("--fixtures")!,file),0,file.StartsWith("hap")?4:6,true,began));
    while(audio.Snapshot.State!=SharedAudioState.Playing&&MasterClock.Timestamp()-began<3){if(audio.Snapshot.State==SharedAudioState.Error)throw new IOException(audio.Snapshot.Notice);await Task.Delay(1);}
    var first=audio.Snapshot;double startMs=(MasterClock.Timestamp()-began)*1000;double? firstPts=first.PlayedPts;while(audio.Snapshot.PlayedPts<firstPts+.01&&MasterClock.Timestamp()-began<3)await Task.Delay(1);
    double advancingMs=(MasterClock.Timestamp()-began)*1000;await Task.Delay(120);var sample=new{File=file,Attempt=attempt+1,StartMs=startMs,AdvancingMs=advancingMs,FirstPts=firstPts,ErrorSeconds=audio.Snapshot.ErrorSeconds,State=audio.Snapshot.State.ToString(),audio.Snapshot.PreparationStarts,audio.Snapshot.Reseeks};results.Add(sample);
    if(first.State!=SharedAudioState.Playing||Math.Abs(audio.Snapshot.ErrorSeconds??1)>.1)failed=true;
    File.WriteAllText(Path.Combine(folder,"latency.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
   }
  }catch(Exception e){failed=true;File.WriteAllText(Path.Combine(folder,"fatal.txt"),e.ToString());}
  finally{await w.Coordinator.DisposeAsync();await w.Preview.DisposeAsync();await w.Monitor.DisposeAsync();MainWindow.Shutdown(failed?1:0);}
 }
}
