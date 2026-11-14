using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using LtcPlayer.App;
namespace LtcPlayer.Avalonia;
public static class CoverChangesVerification
{
 public static async Task RunAsync(MainWindow w,string folder)
 {
  Directory.CreateDirectory(folder);var results=new List<object>();bool failed=false;int receiving=0;
  void Check(bool ok,string why){if(!ok)throw new IOException(why);}
  async Task Until(Func<bool> predicate){double end=MasterClock.Timestamp()+12;while(!predicate()){if(MasterClock.Timestamp()>end)throw new TimeoutException(JsonSerializer.Serialize(new{w.Preview.Snapshot,w.LatestObservation,w.Coordinator.Audio}));await Task.Delay(10);}}
  async Task Test(string name,Func<Task> body){try{await body();results.Add(new{test=name,status="PASS"});}catch(Exception e){failed=true;results.Add(new{test=name,status="FAIL",detail=e.ToString()});}File.WriteAllText(Path.Combine(folder,"results.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));}
  string Make(string name,IBrush brush){string file=Path.Combine(folder,name+".png");using var bitmap=new RenderTargetBitmap(new PixelSize(64,64),new Vector(96,96));using(var drawing=bitmap.CreateDrawingContext())drawing.DrawRectangle(brush,null,new Rect(0,0,64,64));bitmap.Save(file);return file;}
  string red=Make("red",Brushes.Red),blue=Make("blue",Brushes.Blue),bad=Path.Combine(folder,"broken.png");File.WriteAllText(bad,"invalid image");
  string settings=Program.Option("--settings")!;var display=VideoRenderer.Displays().First(d=>d.Primary!=0);
  byte[] Center(){var canvas=w.Surface.Canvas;var pixels=canvas.Readback();return pixels.Skip((canvas.Height/2*canvas.Width+canvas.Width/2)*4).Take(3).ToArray();}
  async Task Change(string path){await w.ChangeCoverForTestAsync(path);await Task.Delay(40);}
  void OldPreserved(CoverImage? image,string saved){Check(ReferenceEquals(image,w.Cover.Image)&&File.ReadAllText(settings)==saved&&w.IsOutputActive,"failed change altered image settings or output");}
  var row=new CueRow{FilePath=Path.Combine(Program.Option("--fixtures")!,"hap-sync-30s.mov")};
  try{
   await w.ApplySettingsAsync(new(new(Rate:"25"),display));
   await w.Monitor.ConfigureAsync(new(Rate:"25"),async(clock,t)=>{double start=MasterClock.Timestamp();while(!t.IsCancellationRequested){if(Volatile.Read(ref receiving)!=0){int f=25+(int)((MasterClock.Timestamp()-start)*25);clock.Accept(new TcFrame{Seconds=f/25%60,Minutes=f/1500,Frame=f%25},MasterClock.Timestamp());}await Task.Delay(20,t);}});
   await ShowProgramService.RefreshAsync(row,CancellationToken.None);w.Editor.Add(row);await w.SetSharedAudioAsync(true,silenceForVerification:true);await w.PlayCueForTestAsync(row);await w.ArmAsync();await Until(()=>w.Coordinator.Audio.State==SharedAudioState.Playing);
   await Test("manual output permits cover registration without resetting playback",async()=>{
    var video=w.Preview.PlaybackSessionId;var canvas=w.Surface.Canvas.SessionId;var sink=w.Coordinator.Audio.SinkSession;long builds=w.Preview.IndexCache.Builds;int preps=w.Coordinator.Audio.PreparationStarts;double at=w.Preview.Snapshot.Position;
    Check(w.CoverChangeAccess.Allowed&&w.FindControl<Button>("ChooseCoverButton")!.IsEnabled,"cover locked by output");await Change(red);
    Check(!w.Cover.IsCovered&&w.Cover.Image?.Path==red&&InputSettings.Load(settings).CoverPath==red&&w.Preview.PlaybackSessionId==video&&w.Surface.Canvas.SessionId==canvas&&w.Coordinator.Audio.SinkSession==sink&&w.Coordinator.Audio.PreparationStarts==preps&&w.Preview.IndexCache.Builds==builds&&w.Preview.Snapshot.Position>at,"registration changed playback or visible state");
   });
   await Test("covered output switches image only after preparation",async()=>{w.SetFadeSeconds(0);w.SetCoverVisible(true);await Task.Delay(60);Check(Center()[2]==255,"red not visible");await Change(blue);Check(w.Cover.IsCovered&&w.Cover.Amount()==1&&Center()[0]==255,"blue not visible or cover reset");});
   await Test("image replacement retains the exact fade trajectory",async()=>{
    w.SetFadeSeconds(2);w.SetCoverVisible(false);double stamp=MasterClock.Timestamp()+.5,expected=w.Cover.Amount(stamp);await Change(red);Check(w.Cover.Amount(stamp)==expected&&!w.Cover.IsCovered,"replacement restarted fade");w.SetFadeSeconds(0);w.SetCoverVisible(true);
   });
   await Test("clear retains covered state and output using black",async()=>{await Change("");Check(w.Cover.Image==null&&w.Cover.IsCovered&&InputSettings.Load(settings).CoverPath==""&&Center().All(v=>v==0)&&w.Preview.Snapshot.State==PreviewState.Playing,"clear reset source or cover state");await Change(red);});
   await Test("broken image leaves old visible cover and settings intact",async()=>{var old=w.Cover.Image;string saved=File.ReadAllText(settings);bool rejected=false;try{await Change(bad);}catch{rejected=true;}Check(rejected,"broken image accepted");OldPreserved(old,saved);Check(Center()[2]==255,"broken image replaced GPU cover");});
   await Test("GPU preparation failure leaves old image and output intact",async()=>{
    var old=w.Cover.Image;string saved=File.ReadAllText(settings);bool rejected=false;w.Surface.Canvas.CoverPrepareForTest=_=>throw new IOException("Synthetic cover texture allocation failure");
    try{await Change(blue);}catch(IOException){rejected=true;}finally{w.Surface.Canvas.CoverPrepareForTest=null;}
    Check(rejected,"GPU failure ignored");OldPreserved(old,saved);Check(Center()[2]==255,"GPU failure replaced old cover");
   });
   await Test("settings replacement failure discards prepared GPU texture",async()=>{
    var old=w.Cover.Image;string saved=File.ReadAllText(settings);bool rejected=false;
    using(var locked=new FileStream(settings,FileMode.Open,FileAccess.Read,FileShare.Read)){try{await Change(blue);}catch(Exception e)when(e is IOException or UnauthorizedAccessException){rejected=true;}}
    Check(rejected,"locked file accepted");OldPreserved(old,saved);Check(Center()[2]==255&&!Directory.GetFiles(folder,"*.tmp").Any(),"failed save changed GPU image or left temporary file");
   });
   await Test("discarded GPU candidate never becomes visible",async()=>{var image=await CoverImageLoader.LoadAsync(blue);using(var prepared=w.Surface.Canvas.PrepareCover(image)){Check(Center()[2]==255,"prepare exposed candidate");}Check(Center()[2]==255,"discard changed visible cover");});
   await Test("slow loading preserves video audio and latest volume fade settings",async()=>{
    var gate=new TaskCompletionSource();w.CoverBeforeLoadForTest=t=>gate.Task.WaitAsync(t);var video=w.Preview.PlaybackSessionId;var sink=w.Coordinator.Audio.SinkSession;int preps=w.Coordinator.Audio.PreparationStarts;double at=w.Preview.Snapshot.Position;
    var change=w.ChangeCoverForTestAsync(blue);try{await Task.Delay(180);Check(!w.CoverChangeAccess.Allowed&&w.Preview.Snapshot.Position>at&&w.Preview.PlaybackSessionId==video&&w.Coordinator.Audio.SinkSession==sink&&w.Coordinator.Audio.PreparationStarts==preps,"loading paused playback");
     w.SetFadeSeconds(.7m);w.FindControl<Slider>("Volume")!.Value=.23;
    }finally{gate.SetResult();await change;w.CoverBeforeLoadForTest=null;}
    var saved=InputSettings.Load(settings);Check(saved.CoverPath==blue&&saved.FadeSeconds==.7&&Math.Abs(saved.AudioVolume-.23)<.001,"old settings snapshot overwrote live controls");
   });
   await Test("duplicate cover and concurrent settings commands are rejected",async()=>{
    var gate=new TaskCompletionSource();w.CoverBeforeLoadForTest=t=>gate.Task.WaitAsync(t);var change=w.ChangeCoverForTestAsync(red);bool duplicate=false,config=false;
    try{Check(!w.FindControl<Button>("SettingsButton")!.IsEnabled,"settings entry remained enabled during cover preparation");try{await w.ChangeCoverForTestAsync(red);}catch(InvalidOperationException){duplicate=true;}try{await w.ApplySettingsAsync(new(InputSettings.Load(settings),display));}catch(InvalidOperationException){config=true;}}
    finally{gate.SetResult();await change;w.CoverBeforeLoadForTest=null;}
    Check(duplicate&&config&&w.FindControl<Button>("SettingsButton")!.IsEnabled,"concurrent mutation accepted or settings remained disabled");
   });
   await Test("close cancellation waits for image preparation cleanup",async()=>{
    var old=w.Cover.Image;string saved=File.ReadAllText(settings);w.CoverBeforeLoadForTest=t=>Task.Delay(Timeout.Infinite,t);var change=w.ChangeCoverForTestAsync(blue);await w.CancelCoverForTestAsync();bool cancelled=false;
    try{await change;}catch(OperationCanceledException){cancelled=true;}finally{w.CoverBeforeLoadForTest=null;}
    Check(cancelled&&w.CoverChangeAccess.Allowed,"cancel did not release operation");OldPreserved(old,saved);
   });
   await Test("LTC output replacement retains synchronization audio and HAP analysis",async()=>{
    Volatile.Write(ref receiving,1);await Until(()=>w.LatestObservation?.CueId==row.CueId&&w.Coordinator.Audio.CueId==row.CueId&&w.Coordinator.Audio.State==SharedAudioState.Playing);await Task.Delay(100);
    var sync=w.SynchronizationSession;var canvas=w.Surface.Canvas.SessionId;var sink=w.Coordinator.Audio.SinkSession;long builds=w.Preview.IndexCache.Builds;int preps=w.Coordinator.Audio.PreparationStarts;double? at=w.LatestObservation!.FramePts;
    await Change(blue);await Task.Delay(120);Check(w.SynchronizationSession==sync&&w.Surface.Canvas.SessionId==canvas&&w.Coordinator.Audio.SinkSession==sink&&w.Coordinator.Audio.PreparationStarts==preps&&w.Preview.IndexCache.Builds==builds&&w.LatestObservation?.FramePts>at&&Center()[0]==255,"cover swap interrupted LTC");
   });
   await Test("LOST held video survives cover replacement",async()=>{
    Volatile.Write(ref receiving,0);await Until(()=>w.Monitor.Clock.Read().State==SyncState.LOST);await Task.Delay(100);var sync=w.SynchronizationSession;var pts=w.LatestObservation!.FramePts;await Change(red);Check(w.SynchronizationSession==sync&&w.LatestObservation!.FramePts==pts&&w.Cover.IsCovered&&Center()[2]==255,"cover swap changed held video");
   });
   await Test("settings apply shares prepared-cover path while video restrictions remain",async()=>{
    var sync=w.SynchronizationSession;int starts=w.Monitor.Starts;var clock=w.Monitor.Clock;var current=InputSettings.Load(settings);
    await w.ApplySettingsAsync(new(current with{CoverPath=blue},display));Check(w.Cover.Image?.Path==blue&&Center()[0]==255&&w.SynchronizationSession==sync&&w.Monitor.Starts==starts&&ReferenceEquals(clock,w.Monitor.Clock),"settings image apply restarted resources");bool rejected=false;
    try{await w.ApplySettingsAsync(new(InputSettings.Load(settings) with{OutputMode=OutputPresentationMode.Compatibility},display));}catch(InvalidOperationException){rejected=true;}Check(rejected,"video resources unlocked accidentally");
   });
   await Test("GPU candidate can be safely discarded after owner disposal",()=>{
    using var canvas=new RenderSession(64,64);var image=new CoverImage(new byte[64*64*4],64,64,"fixture");var candidate=canvas.PrepareCover(image);canvas.Dispose();bool rejected=false;try{candidate.Commit();}catch(ObjectDisposedException){rejected=true;}finally{candidate.Dispose();}Check(rejected,"disposed owner accepted commit");return Task.CompletedTask;
   });
  }catch(Exception e){failed=true;File.WriteAllText(Path.Combine(folder,"fatal.txt"),e.ToString());}
  finally{w.CoverBeforeLoadForTest=null;w.Surface.Canvas.CoverPrepareForTest=null;Volatile.Write(ref receiving,0);await w.DisarmAsync();MainWindow.Shutdown(failed?1:0);}
 }
}
