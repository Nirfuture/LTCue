using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
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
  var waits=new List<object>();
  async Task Until(Func<bool> predicate,double timeoutSeconds=6,[System.Runtime.CompilerServices.CallerLineNumber]int line=0){
   double start=MasterClock.Timestamp();
   try{while(!predicate()){if(MasterClock.Timestamp()-start>timeoutSeconds)throw new TimeoutException(Text("VideoTimingStatus")+" / "+Text("AudioTimingStatus")+" / "+JsonSerializer.Serialize(new{Clock=w.Monitor.Clock.Read(),Observation=w.PreviewObservation,Cues=w.Editor.Rows.Select(r=>new{r.StartTimecode,r.Duration})}));await Task.Delay(20);}}
   finally{waits.Add(new{Line=line,TimeoutSeconds=timeoutSeconds,ElapsedSeconds=MasterClock.Timestamp()-start,Video=Text("VideoTimingStatus"),Audio=Text("AudioTimingStatus"),Clock=w.Monitor.Clock.Read(),Observation=w.PreviewObservation});}
  }
  void Capture(string name="timing-status-main.png"){using var b=new global::Avalonia.Media.Imaging.RenderTargetBitmap(new global::Avalonia.PixelSize((int)w.ClientSize.Width,(int)w.ClientSize.Height),new global::Avalonia.Vector(96,96));b.Render(w);b.Save(Path.Combine(folder,name));}
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
   await Test("frame rate uses measured distinct-frame counters and clears stale sessions",()=>Sync(()=>{
    var meter=new FrameRateMeter();Check(meter.Read("a",0,0)==null,"startup fabricated fps");
    Check(meter.Read("a",1,60)==60,"60fps counter rate");Check(meter.Read("a",1,60)==60,"same observation changed rate");
    Check(meter.Read("a",2,120)==60,"stable fps changed");Check(meter.Read("a",3,150)==45,"window did not follow slowdown");
    Check(meter.Read("a",4,180)==30,"old fast history retained");
    Check(meter.Read("b",5,240)==null,"cue/session reused old samples");
    Check(meter.Read("b",6,300)==60,"new session missing rate");Check(meter.Read("b",7,0)==null,"counter reset produced negative fps");
    meter.Reset();Check(meter.Read("b",8,60)==null,"inactive reset retained rate");Check(meter.Read("b",double.NaN,60)==null,"invalid timestamp accepted");
   }));
   await Test("fps sampling is throttled and red requires persistent below-90-percent rate",()=>Sync(()=>{
    var meter=new FrameRateMeter();meter.Read("a",0,0,60);
    Check(meter.Read("a",.5,30,60)==null,"sampled before one second");
    Check(meter.Read("a",1,54,60)==54&&!meter.IsLow,"exactly 90 percent warned");
    Check(meter.Read("a",1.5,60,60)==54&&!meter.IsLow,"recomputed within one second");
    meter.Reset();meter.Read("a",0,0,60);meter.Read("a",1,50,60);Check(!meter.IsLow,"first low sample warned");
    meter.Read("a",2,100,60);Check(!meter.IsLow,"one-second dip warned");meter.Read("a",3,150,60);Check(meter.IsLow,"persistent low rate did not warn");
    meter.Read("a",4,210,60);Check(!meter.IsLow,"recovered rate stayed red");
    meter.Read("a",5,260,60);Check(!meter.IsLow,"transient low rate warned");
    Check(meter.Read("b",6,310,60)==null&&!meter.IsLow,"new session retained warning");
    meter.Read("b",7,330,60);meter.Read("b",8,350,60);meter.Read("b",9,370,60);Check(meter.IsLow,"new session sustained low rate missing");
    meter.Reset();Check(!meter.IsLow,"inactive reset retained red");
    w.SetFrameRateDisplay(53.9,true);var number=w.FindControl<TextBlock>("VideoFrameRateNumber")!;
    Check(number.Text=="53.9"&&ReferenceEquals(number.Foreground,ThemePalette.ErrorText),"numeric red/half-width style missing");
    Check(!ReferenceEquals(w.FindControl<TextBlock>("AudioTimingStatus")!.Foreground,ThemePalette.ErrorText),"audio colored red by video rate");
    w.SetFrameRateDisplay(60,false);Check(number.Text=="60.0"&&ReferenceEquals(number.Foreground,ThemePalette.SecondaryText),"normal color not restored");
    w.FindControl<StackPanel>("VideoFrameRateGroup")!.IsVisible=false;
   }));
   var row=new CueRow{FilePath=Path.Combine(Program.Option("--fixtures")!,"h264-aac-sync-30s.mp4")};await ShowProgramService.RefreshAsync(row,CancellationToken.None);w.Editor.Add(row);await w.Preview.LoadAsync(row.FilePath);
   await Test("main display shows live manual video and audio status",async()=>{await w.ChangeAudioAsync(true);await w.Preview.PlayAsync();await Until(()=>Text("VideoTimingStatus")=="映像：再生中"&&Text("AudioTimingStatus")=="音声：再生中");Check(w.FindControl<TextBlock>("VideoTimingStatus")!.IsEffectivelyVisible&&w.FindControl<TextBlock>("AudioTimingStatus")!.IsEffectivelyVisible,"hidden");Capture();});
   await Test("pause and off replace healthy timing labels",async()=>{await w.Preview.PauseAsync();await Until(()=>Text("VideoTimingStatus")=="映像：一時停止"&&Text("AudioTimingStatus")=="音声：一時停止");await w.ChangeAudioAsync(false);await Until(()=>Text("AudioTimingStatus")=="音声：OFF");});
   await Test("manual stop aligns video and audio with output off and on while audio OFF stays explicit",async()=>{
    foreach(bool output in new[]{false,true}){
     if(output)await w.ArmAsync();
     await w.ChangeAudioAsync(true);await w.PlayCueForTestAsync(row);
     await Until(()=>Text("VideoTimingStatus")=="映像：再生中"&&Text("AudioTimingStatus")=="音声：再生中");
     var stop=w.FindControl<DataGrid>("CueGrid")!.GetVisualDescendants().OfType<Button>().Single(b=>b.DataContext==row&&b.Classes.Contains("cue-stop"));
     Check(stop.IsEnabled,"stop button disabled");stop.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
     await Until(()=>Text("VideoTimingStatus")=="映像：停止中"&&Text("AudioTimingStatus")=="音声：停止中");
     Check(w.Preview.Snapshot.Position==0&&!w.Coordinator.ReadAudioPlayhead().Advancing,"stop transport changed");
     using(var image=new global::Avalonia.Media.Imaging.RenderTargetBitmap(new global::Avalonia.PixelSize((int)w.ClientSize.Width,(int)w.ClientSize.Height),new global::Avalonia.Vector(96,96))){image.Render(w);image.Save(Path.Combine(folder,output?"stopped-output-on.png":"stopped-output-off.png"));}
     await w.ChangeAudioAsync(false);await Until(()=>Text("AudioTimingStatus")=="音声：OFF");
     if(output)await w.DisarmAsync();
    }
   });
   await Test("LTC reception interpolation loss and input fault remain distinct",async()=>{
    // Recovery includes a loss interval and decoder/audio preparation. Keep the
    // cue active throughout rather than testing the end of the six-second clip.
    Check(row.Duration>=29.9,"synchronization fixture is too short");
    await w.ApplySettingsAsync(new(new(Rate:"29.97 DF",Offset:3),VideoRenderer.Displays().First(d=>d.Primary!=0)));
    int receiving=1,frames=15;await w.ChangeAudioAsync(true);await w.Monitor.ConfigureAsync(new(Rate:"29.97 DF",Freewheel:1),async(clock,token)=>{while(!token.IsCancellationRequested){if(Volatile.Read(ref receiving)!=0){int f=Interlocked.Increment(ref frames);clock.Accept(new TcFrame{Seconds=f/30,Frame=f%30,Drop=1},MasterClock.Timestamp());}await Task.Delay(33,token);}});
    await Until(()=>Text("VideoTimingStatus").StartsWith("映像：遅延なし")&&Text("AudioTimingStatus")=="音声：遅延なし",15);
    await Until(()=>Text("VideoTimingStatus")=="映像：遅延なし"&&w.FindControl<StackPanel>("VideoFrameRateGroup")!.IsVisible&&System.Text.RegularExpressions.Regex.IsMatch(Text("VideoFrameRateNumber"),@"^[0-9]+\.[0-9]$"));
    Check(ToolTip.GetTip(w.FindControl<TextBlock>("RunInfo")!)==null,"cumulative skip tooltip remains");Capture("timing-status-sync-fps.png");
    var seek=w.FindControl<Slider>("Seek")!;
    await Until(()=>seek.Value>.5);
    Check(!seek.IsEnabled&&Math.Abs(seek.Maximum-row.Duration)<.001,"synchronized seek range/access wrong");
    Check(w.PreviewObservation?.FramePts is {} pts&&Math.Abs(seek.Value-pts)<.2,"slider does not follow presented sync frame");
    Volatile.Write(ref receiving,0);
    await Until(()=>Text("VideoTimingStatus").StartsWith("映像：補間中")&&Text("AudioTimingStatus")=="音声：補間中");
    await Until(()=>Text("VideoTimingStatus")=="映像：入力待ち"&&Text("AudioTimingStatus")=="音声：入力待ち");
    Check(!w.FindControl<StackPanel>("VideoFrameRateGroup")!.IsVisible,"input loss retained fps");
    double held=seek.Value;await Task.Delay(150);Check(!seek.IsEnabled&&Math.Abs(seek.Value-held)<.001,"held sync seek moved or became editable");
    Volatile.Write(ref receiving,1);await Until(()=>Text("VideoTimingStatus").StartsWith("映像：遅延なし")&&Text("AudioTimingStatus")=="音声：遅延なし",15);
    await Until(()=>seek.Value>held+.1);Check(!seek.IsEnabled,"resumed synchronization permits seeking");
    Check(w.Monitor.Clock.Read().Seconds+TimecodeRate.Modes.First(r=>r.Name=="29.97 DF").FramesToSeconds(3)<row.Duration,"recovery ran beyond the cue");
    Volatile.Write(ref receiving,0);w.Monitor.Clock.Fault();await Until(()=>Text("VideoTimingStatus")=="映像：入力エラー"&&Text("AudioTimingStatus")=="音声：入力エラー");
   });
  }catch(Exception e){failed=true;File.WriteAllText(Path.Combine(folder,"fatal.txt"),e.ToString());}
  finally{File.WriteAllText(Path.Combine(folder,"waits.json"),JsonSerializer.Serialize(waits,new JsonSerializerOptions{WriteIndented=true}));await w.DisarmAsync();await w.DisposeSharedAudioAsync();await w.Preview.DisposeAsync();await w.Monitor.DisposeAsync();MainWindow.Shutdown(failed?1:0);}
 }
}
