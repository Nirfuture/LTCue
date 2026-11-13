using System.Text.Json;
using LtcPlayer.App;
namespace LtcPlayer.Avalonia;
public static class AudioServiceVerification
{
 public static async Task RunAsync(MainWindow w,string folder)
 {
  Directory.CreateDirectory(folder);bool failed=false;var results=new List<object>();
  string fixtures=Program.Option("--fixtures")!;
  void Check(bool ok,string message){if(!ok)throw new IOException(message);}
  async Task Test(string name,Func<Task> body){try{await body();results.Add(new{test=name,status="PASS"});}catch(Exception e){failed=true;results.Add(new{test=name,status="FAIL",detail=e.ToString()});}File.WriteAllText(Path.Combine(folder,"results.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));}
  async Task Wait(Func<bool> predicate,AudioOutputService service,int ms=5000){double end=MasterClock.Timestamp()+ms/1000.0;while(!predicate()){if(service.Snapshot.State==SharedAudioState.Error)throw new IOException(service.Snapshot.Notice);if(MasterClock.Timestamp()>end)throw new TimeoutException(JsonSerializer.Serialize(service.Snapshot));await Task.Delay(5);}}
  AudioPlayhead head=AudioPlayhead.Empty;
  AudioPlayhead Read(){var p=Volatile.Read(ref head);double now=MasterClock.Timestamp();return p with{Position=p.Estimate(now),HostSeconds=now};}
  void Source(string file="h264-aac.mp4",double at=0,int generation=1,bool advancing=true){Volatile.Write(ref head,new(Guid.NewGuid(),Guid.NewGuid(),generation,Path.Combine(fixtures,file),at,file.StartsWith("hap")?4:6,advancing,MasterClock.Timestamp()));}
  try
  {
   await Test("off owns no decoder or WASAPI stream",async()=>{await using var s=new AudioOutputService(Read,true);await s.ConfigureAsync(false,"",.5f,false);Check(AudioOutputSink.ActiveStreams==0&&AudioDecodeSession.ActiveDecoders==0,"off resources");});
   await Test("enable follows current video time and mute preserves clock",async()=>{
    Source(at:1,advancing:false);Volatile.Write(ref head,Read() with{PreparationRequested=true});
    await using var s=new AudioOutputService(Read,true);await s.ConfigureAsync(true,"",.5f,false);
    await Wait(()=>s.Snapshot.State==SharedAudioState.Preparing&&s.Snapshot.BufferedBlocks>0,s);
    await Task.Delay(100);var prepared=s.Snapshot;
    Check(prepared.PlayedPts==null&&prepared.HardwarePadding==0,"preparation reached hardware before playback permission");
    Volatile.Write(ref head,Read() with{Advancing=true,PreparationRequested=false});await Wait(()=>s.Snapshot.State==SharedAudioState.Playing,s);
    Check(s.Snapshot.PreparationStarts==prepared.PreparationStarts,"playback restarted prepared decoder");
    var a=s.Snapshot;Check(a.PlayedPts>1&&Math.Abs(a.ErrorSeconds??1)<.1,"not current source time");s.SetVolume(.3f,true);await Wait(()=>s.Snapshot.State==SharedAudioState.Muted,s);await Task.Delay(250);var b=s.Snapshot;
    Check(b.SinkSession==a.SinkSession&&b.PreparationStarts==a.PreparationStarts&&b.PlayedPts>a.PlayedPts+.15&&b.AppliedGain==0,"mute restarted or stopped clock");
    s.SetVolume(.3f,false);await Wait(()=>s.Snapshot.State==SharedAudioState.Playing,s);File.WriteAllText(Path.Combine(folder,"tracking.json"),JsonSerializer.Serialize(s.Snapshot));
   });
   await Test("pause flushes old audio and resume uses current time",async()=>{
    Source(at:.5);await using var s=new AudioOutputService(Read,true);await s.ConfigureAsync(true,"",.5f,false);await Wait(()=>s.Snapshot.State==SharedAudioState.Playing,s);var session=s.Snapshot.SinkSession;
    Volatile.Write(ref head,Read() with{Advancing=false});await Wait(()=>s.Snapshot.State==SharedAudioState.Waiting&&AudioDecodeSession.ActiveDecoders==0,s);
    Check(s.Snapshot.HardwarePadding==0,"pause queued audio");
    using(var preparationCancel=new CancellationTokenSource()){
     Volatile.Write(ref head,Read() with{Position=2,PreparationRequested=true,PreparationCancellation=preparationCancel.Token});
     await Wait(()=>s.Snapshot.State==SharedAudioState.Preparing&&s.Snapshot.BufferedBlocks>0,s);
     preparationCancel.Cancel();await Wait(()=>s.Snapshot.State==SharedAudioState.Waiting&&AudioDecodeSession.ActiveDecoders==0,s);
     Check(s.Snapshot.HardwarePadding==0&&s.Snapshot.PlayedPts==null,"cancelled preparation leaked audio");
    }
    Volatile.Write(ref head,Read() with{Position=2,Advancing=true,PreparationRequested=false});await Wait(()=>s.Snapshot.State==SharedAudioState.Playing,s);
    Check(s.Snapshot.PlayedPts>=2&&s.Snapshot.SinkSession==session,"resume incorrect");
   });
   await Test("new generation and cue replace audio without replacing sink",async()=>{
    Source(at:.3);await using var s=new AudioOutputService(Read,true);await s.ConfigureAsync(true,"",.5f,false);await Wait(()=>s.Snapshot.State==SharedAudioState.Playing,s);var a=s.Snapshot;
    var next=Read() with{Generation=2,Position=2};Volatile.Write(ref head,next);await Wait(()=>s.Snapshot.State==SharedAudioState.Playing&&s.Snapshot.Generation==2,s);
    Check(s.Snapshot.PlayedPts>=2&&s.Snapshot.SinkSession==a.SinkSession,"seek stale audio");Source("hap-pcm.mov",.7,3);await Wait(()=>s.Snapshot.State==SharedAudioState.Playing&&s.Snapshot.Generation==3,s);Check(s.Snapshot.CueId==Read().CueId,"old cue");
   });
   await Test("slow preparation catches up without repeated restart",async()=>{
    Source();await using var s=new AudioOutputService(Read,true,450);await s.ConfigureAsync(true,"",.5f,false);await Wait(()=>s.Snapshot.State==SharedAudioState.Playing,s);await Task.Delay(350);
    Check(s.Snapshot.PreparationStarts==1&&s.Snapshot.Reseeks==0&&s.Snapshot.DroppedBlocks>0,"preparation restart loop");Check(Math.Abs(s.Snapshot.ErrorSeconds??1)<.1,"slow prep failed catch up");
   });
   await Test("unsupported and silent cues allocate no decoder",async()=>{
    await using var s=new AudioOutputService(Read,true);Source("hap-q-silent.mov");await s.ConfigureAsync(true,"",.5f,false);await Wait(()=>s.Snapshot.State==SharedAudioState.NoAudio,s);Check(AudioDecodeSession.ActiveDecoders==0,"silent decoder");
    Source("h264-mp3.mp4");await Wait(()=>s.Snapshot.State==SharedAudioState.NoAudio&&s.Snapshot.CueId==Read().CueId,s);Check(AudioDecodeSession.ActiveDecoders==0,"MP3 decoder");
   });
   await Test("stale source silences and explicit off releases resources",async()=>{
    var anchor=new AudioPlayhead(Guid.NewGuid(),Guid.NewGuid(),1,"test",1,10,true,100);
    Check(!anchor.Usable(101),"observation did not expire");
    var owned=anchor with{SourceOwnsClock=true};
    Check(owned.Usable(101)&&owned.Estimate(101)==2&&owned.HostSeconds==100,"owned clock contract");
    Check(!(owned with{Advancing=false}).Usable(101),"stopped owned clock remained usable");
    Source();bool stale=false;AudioPlayhead Supplier()=>stale?Volatile.Read(ref head):Read();await using var s=new AudioOutputService(Supplier,true);await s.ConfigureAsync(true,"",.5f,false);await Wait(()=>s.Snapshot.State==SharedAudioState.Playing,s);
    stale=true;await Wait(()=>s.Snapshot.State==SharedAudioState.Waiting,s);await s.ConfigureAsync(false,"",.5f,false);Check(AudioDecodeSession.ActiveDecoders==0&&AudioOutputSink.ActiveStreams==0,"off leak");
   });
   await Test("source failure latches audio error and explicit retry recovers",async()=>{
    Source("missing.mp4");await using var s=new AudioOutputService(Read,true);await s.ConfigureAsync(true,"",.5f,false);double end=MasterClock.Timestamp()+3;while(s.Snapshot.State!=SharedAudioState.Error&&MasterClock.Timestamp()<end)await Task.Delay(10);
    Check(s.Snapshot.State==SharedAudioState.Error&&AudioOutputSink.ActiveStreams==0,"failure not isolated");Source();await s.ConfigureAsync(true,"",.5f,false);await Wait(()=>s.Snapshot.State==SharedAudioState.Playing,s);
   });
   Check(AudioOutputSink.ActiveStreams==0&&AudioDecodeSession.ActiveDecoders==0,"final resource leak");
  }
  finally{await w.Monitor.DisposeAsync();MainWindow.Shutdown(failed?1:0);}
 }
}
