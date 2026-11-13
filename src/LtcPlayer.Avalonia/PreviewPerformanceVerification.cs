using System.Diagnostics;
using System.Text.Json;
using LtcPlayer.App;
namespace LtcPlayer.Avalonia;

public static class PreviewPerformanceVerification
{
 static void Check(bool value,string message){if(!value)throw new IOException(message);}
 static async Task<VideoFrame> First(MediaDecoder decoder,string path,double at=0)
 {
  await foreach(var frame in decoder.DecodeAsync(path,640,360,CancellationToken.None,at))return frame;
  throw new IOException("No frame decoded");
 }
 public static async Task RunAsync(MainWindow window,string folder)
 {
  Directory.CreateDirectory(folder);var timings=new List<object>();Exception? failure=null;
  try {
   var media=Program.Option("--media")??throw new IOException("--media missing");
   var cached=new MediaDecoder(reuseAnalysis:true);var watch=Stopwatch.StartNew();
   var original=await First(cached,media);double cold=watch.Elapsed.TotalMilliseconds;
   watch.Restart();var repeated=await First(cached,media);double warm=watch.Elapsed.TotalMilliseconds;
   Check(original.Pixels.SequenceEqual(repeated.Pixels),"Cached HAP frame changed");
   var sought=await First(cached,media,2);Check(sought.Seconds>=2,"Cached seek returned earlier frame");
   var uncached=await First(new MediaDecoder(),media,2);
   Check(sought.Seconds==uncached.Seconds&&sought.Pixels.SequenceEqual(uncached.Pixels),"Cached seek differs from fresh decode");
   timings.Add(new{Test="HAP first frame",ColdMs=cold,WarmMs=warm,SeekVerified=true});

   var copy=Path.Combine(folder,"changing.mov");File.Copy(media,copy,true);
   var invalidation=new MediaDecoder(reuseAnalysis:true);var before=await invalidation.InspectAsync(copy,CancellationToken.None);
   Check(ReferenceEquals(before,await invalidation.InspectAsync(copy,CancellationToken.None)),"Metadata not reused");
   await First(invalidation,copy);
   File.SetLastWriteTimeUtc(copy,File.GetLastWriteTimeUtc(copy).AddSeconds(2));
   var after=await invalidation.InspectAsync(copy,CancellationToken.None);
   Check(!ReferenceEquals(before,after),"Changed file reused stale metadata");
   var refreshed=await First(invalidation,copy);Check(refreshed.Pixels.SequenceEqual(original.Pixels),"Changed file failed to refresh index");
   using(var canceled=new CancellationTokenSource()) {
    canceled.Cancel();bool rejected=false;
    try{await invalidation.InspectAsync(copy,canceled.Token);}catch(OperationCanceledException){rejected=true;}
    Check(rejected,"Cache ignored cancellation");
   }
   timings.Add(new{Test="File modification invalidation and cancellation",Passed=true});

   window.Preview.SetVolume(0,true);
   foreach(var path in new[]{media,media,Path.Combine(Program.Option("--fixtures")!,"h264-aac.mp4"),media}) {
    watch.Restart();await window.Preview.LoadAsync(path);double load=watch.Elapsed.TotalMilliseconds;
    watch.Restart();await window.Preview.PlayAsync();double play=watch.Elapsed.TotalMilliseconds;
    await Task.Delay(300);Check(window.Preview.Snapshot.Position>0,"Preview did not advance");
    Check(window.Preview.Snapshot.State==PreviewState.Playing,"Preview playback failed: "+window.Preview.Snapshot.Notice);
    for(int attempt=0;attempt<80&&window.Preview.Snapshot.FramePts is not >.1;attempt++)await Task.Delay(25);
    Check(window.Preview.Snapshot.FramePts is >.1,"Video frame did not advance with playback clock");
    await window.Preview.PauseAsync();double paused=window.Preview.Snapshot.Position;
    await Task.Delay(100);Check(window.Preview.Snapshot.Position==paused,"Paused position changed");
    await window.Preview.SeekAsync(1);Check(window.Preview.Snapshot.State==PreviewState.Paused,"Seek state wrong");
    await window.Preview.PlayAsync();await Task.Delay(200);
    Check(window.Preview.Snapshot.Position>=1,"Seek/resume did not advance");
    await window.Preview.StopAsync();Check(window.Preview.Snapshot.State==PreviewState.Stopped&&window.Preview.Snapshot.Position>=1,"Stop did not hold");
    await window.Preview.PlayAsync();await Task.Delay(100);
    Check(window.Preview.Snapshot.Position>0&&window.Preview.Snapshot.Position<1,"Replay did not start at zero");
    await window.Preview.StopAsync();
    timings.Add(new{Test="Preview load/play/pause/seek/replay",File=Path.GetFileName(path),LoadMs=load,PlayMs=play});
   }
  }catch(Exception error){failure=error;}
  finally {
   await window.Preview.StopAsync();await window.Monitor.DisposeAsync();
   File.WriteAllText(Path.Combine(folder,"performance.json"),JsonSerializer.Serialize(new{Passed=failure==null,Measurements=timings,Error=failure?.ToString()},new JsonSerializerOptions{WriteIndented=true}));
   MainWindow.Shutdown(failure==null?0:1);
  }
 }
}
