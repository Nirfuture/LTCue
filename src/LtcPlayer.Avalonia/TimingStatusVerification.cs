using System.Text.Json;
using Avalonia.Controls;
using LtcPlayer.App;
namespace LtcPlayer.Avalonia;
public static class TimingStatusVerification
{
 public static async Task RunAsync(MainWindow w,string folder){
  Directory.CreateDirectory(folder);bool failed=false;var results=new List<object>();
  void Check(bool ok,string message){if(!ok)throw new IOException(message);}
  async Task Test(string name,Func<Task> body){try{await body();results.Add(new{test=name,status="PASS"});}catch(Exception e){failed=true;results.Add(new{test=name,status="FAIL",detail=e.ToString()});}File.WriteAllText(Path.Combine(folder,"results.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));}
  Task Sync(Action body){body();return Task.CompletedTask;}
  string Text(string name)=>w.FindControl<TextBlock>(name)!.Text??"";
  async Task Until(Func<bool> predicate){double end=MasterClock.Timestamp()+6;while(!predicate()){if(MasterClock.Timestamp()>end)throw new TimeoutException(Text("VideoTimingStatus")+" / "+Text("AudioTimingStatus"));await Task.Delay(20);}}
  void Capture(){using var b=new global::Avalonia.Media.Imaging.RenderTargetBitmap(new global::Avalonia.PixelSize((int)w.ClientSize.Width,(int)w.ClientSize.Height),new global::Avalonia.Vector(96,96));b.Render(w);b.Save(Path.Combine(folder,"timing-status-main.png"));}
  try{
   await Test("within tolerance and transient jitter do not warn",()=>Sync(()=>{var s=new PlaybackTimingStatus();Check(s.Read("a",.04,.05,0).Text=="遅延なし","normal");Check(s.Read("a",.12,.05,1).Text=="確認中","transient called normal");Check(!s.Read("a",.12,.05,1.2).Warning,"transient warned");Check(!s.Read("a",0,.05,1.25).Warning,"recovered transient");}));
   await Test("persistent lag and lead have correct signs",()=>Sync(()=>{var s=new PlaybackTimingStatus();s.Read("a",.12,.05,0);var lag=s.Read("a",.12,.05,.31);Check(lag.Warning&&lag.Text=="120ms遅れ","lag");Check(!s.Read("a",-.08,.05,1).Warning,"sign jitter warned");var lead=s.Read("a",-.08,.05,1.31);Check(lead.Warning&&lead.Text=="80ms先行","lead");}));
   await Test("recovery persistence and source replacement reset warnings",()=>Sync(()=>{var s=new PlaybackTimingStatus();s.Read("a",.12,.05,0);s.Read("a",.12,.05,.31);Check(s.Read("a",0,.05,.4).Text=="確認中","early normal");Check(s.Read("a",0,.05,.71).Text=="遅延なし","recovery");s.Read("a",.12,.05,1);s.Read("a",.12,.05,1.31);Check(!s.Read("b",.12,.05,1.4).Warning,"old generation warning");}));
   await Test("missing and inactive samples never report delay-free",()=>Sync(()=>{var s=new PlaybackTimingStatus();Check(s.Read("a",null,.05,0).Text=="確認中","missing");Check(s.Read("a",double.NaN,.05,1).Text=="確認中","nan");foreach(var state in new[]{"準備中","再同期中","OFF","一時停止","入力待ち"})Check(s.Read("a",0,.05,2,state).Text==state,"inactive hidden");}));
   await Test("frame offsets use seconds for every supported rate",()=>Sync(()=>{
    foreach(var rate in TimecodeRate.Modes)foreach(int frames in new[]{-30,-3,0,1,30}){
     double correction=rate.FramesToSeconds(frames);Check(Math.Abs(correction*rate.Fps-frames)<1e-9,"frame offset unit mismatch");
     var status=new PlaybackTimingStatus();double actual=10+correction;Check(status.Read("offset",10+correction-actual,.05,0).Text=="遅延なし","corrected frame reported lag");
    }
   }));
   var row=new CueRow{FilePath=Path.Combine(Program.Option("--fixtures")!,"h264-aac.mp4")};await ShowProgramService.RefreshAsync(row,CancellationToken.None);w.Editor.Add(row);await w.Preview.LoadAsync(row.FilePath);
   await Test("main display shows live manual video and audio status",async()=>{await w.ChangeAudioAsync(true);await w.Preview.PlayAsync();await Until(()=>Text("VideoTimingStatus")=="映像：遅延なし"&&Text("AudioTimingStatus")=="音声：遅延なし");Check(w.FindControl<TextBlock>("VideoTimingStatus")!.IsEffectivelyVisible&&w.FindControl<TextBlock>("AudioTimingStatus")!.IsEffectivelyVisible,"hidden");Capture();});
   await Test("pause and off replace healthy timing labels",async()=>{await w.Preview.PauseAsync();await Until(()=>Text("VideoTimingStatus")=="映像：一時停止"&&Text("AudioTimingStatus")=="音声：一時停止");await w.ChangeAudioAsync(false);await Until(()=>Text("AudioTimingStatus")=="音声：OFF");});
   await Test("LTC status uses the common clock and rejects faulted input",async()=>{
    await w.ApplySettingsAsync(new(new(Rate:"29.97 DF",Offset:3),VideoRenderer.Displays().First(d=>d.Primary!=0)));
    int receiving=1,frames=15;await w.ChangeAudioAsync(true);await w.Monitor.ConfigureAsync(new(Rate:"29.97 DF"),async(clock,token)=>{while(!token.IsCancellationRequested){if(Volatile.Read(ref receiving)!=0){int f=Interlocked.Increment(ref frames);clock.Accept(new TcFrame{Seconds=f/30,Frame=f%30,Drop=1},MasterClock.Timestamp());}await Task.Delay(33,token);}});
    await Until(()=>Text("VideoTimingStatus")=="映像：遅延なし"&&Text("AudioTimingStatus")=="音声：遅延なし");
    Volatile.Write(ref receiving,0);w.Monitor.Clock.Fault();await Until(()=>Text("VideoTimingStatus")=="映像：入力エラー");Check(Text("AudioTimingStatus")!="音声：遅延なし","fault called healthy");
   });
  }catch(Exception e){failed=true;File.WriteAllText(Path.Combine(folder,"fatal.txt"),e.ToString());}
  finally{await w.DisposeSharedAudioAsync();await w.Preview.DisposeAsync();await w.Monitor.DisposeAsync();MainWindow.Shutdown(failed?1:0);}
 }
}
