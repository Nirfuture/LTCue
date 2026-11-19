using System.Text.Json;
using System.Security.Cryptography;
using LtcPlayer.App;
namespace LtcPlayer.Avalonia;
public static class Mp4SingleStartVerification
{
 public static async Task RunAsync(MainWindow w,string folder){
  Directory.CreateDirectory(folder);var results=new List<object>();var observations=new List<object>();
  string file=Path.Combine(Program.Option("--fixtures")!,"h264-aac.mp4");
  var p=w.Preview;var authority=p.ManualAuthority;
  void Check(bool value,string why){if(!value)throw new IOException(why);}
  async Task Test(string name,Func<Task> run){try{await run();results.Add(new{test=name,status="PASS"});}catch(Exception e){results.Add(new{test=name,status="FAIL",error=e.ToString()});}finally{p.ManualAuthority=authority;p.LoadBeforeFrameForTest=null;p.LoadBeforePlayForTest=null;p.PreparedBufferForTest=null;await p.StopAsync();Check(FrameBuffer.ActiveWorkers==0,"worker survived stop");}}
  string Hash(VideoFrame f)=>Convert.ToHexString(SHA256.HashData(f.Pixels));
  try{
   await w.SetSharedAudioAsync(false,silenceForVerification:true);
   await Test("one video process and one initial presentation",async()=>{
    StartupMeasurementTrace.Begin("single");MediaProbeTrace.Observer=(n,s)=>StartupMeasurementTrace.Mark(n,note:s);
    int before=p.Snapshot.Presented;int still=p.StillDecodeStarts;
    p.LoadBeforePlayForTest=_=>{Check(p.Snapshot.Presented==before+1&&p.Snapshot.State==PreviewState.Ready,"first presentation");return Task.CompletedTask;};
    Check(await p.LoadAndPlayAsync(file,()=>true),"not adopted");
    Check(p.Snapshot.Presented==before+1&&p.StillDecodeStarts==still,"duplicate presentation or still decode");
    Check(StartupMeasurementTrace.Snapshot().Count(e=>e.Trial=="single"&&e.Event=="decode_ffmpeg_started")==1,"video process count");
   });
   await Test("first and following decoded hashes and PTS match standalone",async()=>{
    foreach(string source in new[]{file,Path.Combine(Program.Option("--fixtures")!,"nonzero-aac.mp4")}){
    var size=w.Surface.CanvasSize;var expected=new List<VideoFrame>();using var c=new CancellationTokenSource();
    await foreach(var f in new MediaDecoder(gpuScaling:true).DecodeAsync(source,size.Width,size.Height,c.Token)){expected.Add(f);if(expected.Count==4)break;}
    p.PreparedBufferForTest=async(b,t)=>{
     var actual=new List<VideoFrame>{p.LastFrameForTest!};
     while(actual.Count<4){Check(await b.WaitForDataAsync(t),"early EOF");if(b.TryRead(out var f))actual.Add(f!);}
     for(int i=0;i<4;i++){Check(actual[i].Seconds==expected[i].Seconds&&Hash(actual[i])==Hash(expected[i]),"decoded sequence mismatch");observations.Add(new{Source=Path.GetFileName(source),Frame=i,actual[i].Seconds,Hash=Hash(actual[i])});}
     p.CancelPreparation();
    };Check(!await p.LoadAndPlayAsync(source,()=>true),"cancel adopted");}
   });
   await Test("cancel before decode",async()=>{p.LoadBeforeFrameForTest=_=>{p.CancelPreparation();return Task.CompletedTask;};Check(!await p.LoadAndPlayAsync(file,()=>true),"cancel adopted");});
   await Test("cancel after first presentation",async()=>{p.LoadBeforePlayForTest=_=>{p.CancelPreparation();return Task.CompletedTask;};Check(!await p.LoadAndPlayAsync(file,()=>true),"cancel adopted");});
   await Test("UI adoption refusal",async()=>Check(!await p.LoadAndPlayAsync(file,()=>false),"refusal adopted"));
   await Test("authority lost during adoption",async()=>Check(!await p.LoadAndPlayAsync(file,()=>{p.ManualAuthority=()=>false;return true;}),"old authority adopted"));
   await Test("file version changed during adoption",async()=>{
    string copy=Path.Combine(folder,"version.mp4");File.Copy(file,copy);
    Check(!await p.LoadAndPlayAsync(copy,()=>{File.SetLastWriteTimeUtc(copy,File.GetLastWriteTimeUtc(copy).AddSeconds(2));return true;}),"old file adopted");
   });
   await Test("canvas changed during adoption",async()=>{
    var size=w.Surface.CanvasSize;
    try{Check(!await p.LoadAndPlayAsync(file,()=>{using var canvas=w.Surface.PrepareCanvas(640,360);canvas!.Commit();return true;}),"old canvas adopted");}
    finally{using var canvas=w.Surface.PrepareCanvas(size.Width,size.Height);canvas!.Commit();}
   });
   await Test("full bounded queue cancellation completes",async()=>{
    p.PreparedBufferForTest=async(b,t)=>{double until=MasterClock.Timestamp()+10;while(b.Queued<8){if(MasterClock.Timestamp()>until)throw new TimeoutException();await Task.Delay(5,t);}Check(b.Queued==8,"queue bound");p.CancelPreparation();};
    Check(!await p.LoadAndPlayAsync(file,()=>true),"full queue adopted");
   });
   await Test("pause resume stop and replay",async()=>{Check(await p.LoadAndPlayAsync(file,()=>true),"play");await Task.Delay(80);await p.PauseAsync();var id=p.PlaybackSessionId;await p.PlayAsync();Check(p.PlaybackSessionId==id,"resume replaced loop");await p.StopAsync();Check(p.Snapshot.Position==0,"stop position");await p.PlayAsync();Check(p.Snapshot.State==PreviewState.Playing,"replay");});
   await Test("standalone load releases decode worker",async()=>{Check(await p.LoadAsync(file),"load");Check(FrameBuffer.ActiveWorkers==0&&p.Snapshot.State==PreviewState.Ready,"load retained stream");});
   await Test("corrupt input releases preparation",async()=>{string broken=Path.Combine(folder,"broken.mp4");await File.WriteAllTextAsync(broken,"broken");bool rejected=false;try{await p.LoadAndPlayAsync(broken,()=>true);}catch(Exception e)when(e is IOException or InvalidDataException){rejected=true;}Check(rejected&&!p.IsBusy&&FrameBuffer.ActiveWorkers==0,"failure retained resources");});
  }catch(Exception e){File.WriteAllText(Path.Combine(folder,"fatal.txt"),e.ToString());}
  finally{MediaProbeTrace.Observer=null;StartupMeasurementTrace.End();File.WriteAllText(Path.Combine(folder,"frames.json"),JsonSerializer.Serialize(observations));File.WriteAllText(Path.Combine(folder,"results.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));MainWindow.Shutdown(results.Count==12&&results.All(x=>JsonSerializer.Serialize(x).Contains("PASS"))?0:1);}
 }
}
