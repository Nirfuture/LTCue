using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using System.Diagnostics;
using System.Text.Json;
using LtcPlayer.App;
namespace LtcPlayer.Avalonia;
public static class UiCorrectionsVerification
{
 public static async Task RunAsync(MainWindow w,string folder){
  Directory.CreateDirectory(folder);bool failed=false;var results=new List<object>();
  T C<T>(string n)where T:Control=>w.FindControl<T>(n)!;
  void Check(bool ok,string message){if(!ok)throw new IOException(message);}
  async Task Test(string name,Func<Task> body){try{await body();results.Add(new{test=name,status="PASS"});}catch(Exception e){failed=true;results.Add(new{test=name,status="FAIL",detail=e.ToString()});}File.WriteAllText(Path.Combine(folder,"results.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));}
  void Capture(Window target,string name){using var b=new RenderTargetBitmap(new PixelSize((int)target.ClientSize.Width,(int)target.ClientSize.Height),new Vector(96,96));b.Render(target);b.Save(Path.Combine(folder,name+".png"));}
  var display=VideoRenderer.Displays().First(d=>d.Primary!=0);
  try{
   await Test("spinnerless fade and main panel contains no device setup",()=>{Check(!C<NumericUpDown>("FadeSeconds").ShowButtonSpinner,"spinner remains");Check(w.FindControl<ComboBox>("AudioDevice")==null&&w.FindControl<Button>("RefreshAudioDevicesButton")==null,"device settings remain");Check(C<TextBlock>("LtcLabel").Foreground!.ToString()=="#ff8dcbff","blue LTC removed");Check(C<TextBlock>("OutputGeometry").Text!.StartsWith("出力画面："),"display label missing");return Task.CompletedTask;});
   await Test("DF separator and received status color",async()=>{
    await w.ApplySettingsAsync(new(new(Rate:"29.97 DF"),display));var mode=TimecodeRate.Modes.First(r=>r.Drop);var clock=new MasterClock(mode);clock.Accept(new TcFrame{Drop=1},MasterClock.Timestamp());Check(clock.Read().Label=="00:00:00;00","DF label missing semicolon");
    await w.Monitor.ConfigureAsync(new(Rate:"29.97 DF"),async(c,t)=>{while(!t.IsCancellationRequested){c.Accept(new TcFrame{Drop=1},MasterClock.Timestamp());await Task.Delay(33,t);}});await Task.Delay(150);Check(C<TextBlock>("LtcStateLabel").Text=="LTC受信中"&&C<TextBlock>("LtcStateLabel").Foreground!.ToString()=="#ff80d5b1","received status not green");await w.Monitor.ConfigureAsync(new(Rate:"29.97 DF"));
   });
   await Test("input output setup and refresh are grouped in settings",async()=>{
    var dialog=new SettingsDialog(InputSettings.Load(Program.Option("--settings")),display,false);var task=dialog.ShowDialog<SettingsResult?>(w);try{await Task.Delay(120);Check(dialog.AudioOutputSelector.ItemCount>0&&dialog.InputRefreshButton.IsEffectivelyEnabled&&dialog.AudioRefreshButton.IsEffectivelyEnabled,"settings missing");var labels=dialog.GetLogicalDescendants().OfType<TextBlock>().Select(x=>x.Text??"");Check(labels.All(x=>!x.Contains("ON時に固定")),"fixed-device text remains");Capture(dialog,"settings");var tabs=dialog.GetLogicalDescendants().OfType<TabControl>().Single();Check(((System.Collections.IEnumerable)tabs.ItemsSource!).Cast<object>().Count()==3,"settings tabs missing");tabs.SelectedIndex=2;await Task.Delay(100);Check(dialog.GetVisualDescendants().OfType<TextBlock>().Any(t=>t.IsEffectivelyVisible&&(t.Text??"").Contains("BitBlt")&&(t.Text??"").Contains("不安定")),"mode guidance missing");Capture(dialog,"settings-video");}finally{dialog.Close(null);await task;}
   });
   var row=new CueRow{FilePath=Path.Combine(Program.Option("--fixtures")!,"h264-aac.mp4")};await ShowProgramService.RefreshAsync(row,CancellationToken.None);w.Editor.Add(row);C<DataGrid>("CueGrid").SelectedItem=row;double deadline=MasterClock.Timestamp()+5;while(w.Preview.Snapshot.State!=PreviewState.Ready&&MasterClock.Timestamp()<deadline)await Task.Delay(10);
   await Test("output icon times and output canvas boundary",async()=>{
    await Task.Delay(150);Check(C<Button>("ArmButton").GetVisualDescendants().OfType<LucideIcon>().Any(i=>i.Kind=="monitor-up"),"output icon missing");var left=C<TextBlock>("PreviewPosition");var right=C<TextBlock>("PreviewDuration");Check(right.TranslatePoint(default,w)!.Value.X>left.TranslatePoint(default,w)!.Value.X+left.Bounds.Width,"times not split");Check(C<Border>("OutputFrame").BorderThickness.Left==1,"canvas boundary absent");var viewport=C<Grid>("PreviewViewport");var frame=C<Border>("OutputFrame");Check(Math.Abs(frame.Bounds.Width/ frame.Bounds.Height-16.0/9)<.03&&viewport.Bounds.Width-frame.Bounds.Width<=8,"extra border space");Check(C<DataGrid>("CueGrid").GetVisualDescendants().OfType<TextBlock>().Any(t=>t.Classes.Contains("tc-frame-separator")&&t.Text==";"),"DF editor not updated");Capture(w,"main");
   });
   await Test("borderless panels and odd-size preview edges preserve canvas pixels",async()=>{
    Check(C<Border>("PreviewCard").BorderThickness==new Thickness(0),"panel border remains");
    var timer=(global::Avalonia.Threading.DispatcherTimer)typeof(MainWindow).GetField("timer",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(w)!;timer.Stop();
    var host=w.Surface;var frame=C<Border>("OutputFrame");double oldW=host.Width,oldH=host.Height;
    try{double pixelScale=w.RenderScaling;frame.Width=393/pixelScale+2;frame.Height=222/pixelScale+2;host.Width=393/pixelScale;host.Height=222/pixelScale;await Task.Delay(100);var canvas=host.Canvas;var bytes=new byte[canvas.Width*canvas.Height*4];for(int i=0;i<bytes.Length;i+=4){bytes[i]=70;bytes[i+1]=130;bytes[i+2]=190;bytes[i+3]=255;}ulong before=canvas.PreviewStatus().Presented;canvas.Present(new VideoFrame(bytes,0));double end=MasterClock.Timestamp()+2;while(canvas.PreviewStatus().Presented<before+3&&MasterClock.Timestamp()<end){canvas.UpdatePreview();await Task.Delay(5);}var stats=canvas.PreviewStatus();var pixels=canvas.PreviewReadback();File.WriteAllText(Path.Combine(folder,"edge-sizing.json"),JsonSerializer.Serialize(new{stats.Width,stats.Height,RenderScaling=w.RenderScaling,HostWidth=host.Bounds.Width,HostHeight=host.Bounds.Height,ViewportWidth=C<Grid>("PreviewViewport").Bounds.Width,ViewportHeight=C<Grid>("PreviewViewport").Bounds.Height,FrameWidth=frame.Bounds.Width,FrameHeight=frame.Bounds.Height}));Check(Math.Abs(stats.Width-393)<=1&&Math.Abs(stats.Height-222)<=1&&(stats.Width%2!=0||stats.Height%2!=0),"odd-size fixture not applied within one physical pixel of DPI layout rounding");for(int y=0;y<stats.Height;y++)for(int x=0;x<stats.Width;x++){if(x!=0&&y!=0&&x!=stats.Width-1&&y!=stats.Height-1)continue;int i=(y*stats.Width+x)*4;Check(Math.Abs(pixels[i]-70)<=1&&Math.Abs(pixels[i+1]-130)<=1&&Math.Abs(pixels[i+2]-190)<=1,"black edge at "+x+","+y);}File.WriteAllText(Path.Combine(folder,"edge.json"),JsonSerializer.Serialize(new{stats.Width,stats.Height,AllFourEdgesVerified=true}));}
    finally{host.Width=oldW;host.Height=oldH;w.UpdatePreviewGeometry();timer.Start();}
   });
   await Test("audio-only settings and unchanged apply preserve video and input",async()=>{
    await w.Preview.PlayAsync();await Task.Delay(250);var session=w.Preview.PlaybackSessionId;var canvas=w.Surface.Canvas.SessionId;int starts=w.Monitor.Starts;var clock=w.Monitor.Clock;double at=w.Preview.Snapshot.Position;
    var settings=InputSettings.Load(Program.Option("--settings"));await w.ApplySettingsAsync(new(settings with{AudioDeviceId=AudioOutputSink.Devices()[0].Id},display));await w.ApplySettingsAsync(new(InputSettings.Load(Program.Option("--settings")),display));await Task.Delay(100);
    Check(w.Preview.PlaybackSessionId==session&&w.Preview.Snapshot.Position>at&&w.Surface.Canvas.SessionId==canvas&&w.Monitor.Starts==starts&&ReferenceEquals(clock,w.Monitor.Clock),"settings restarted playback/input");await w.Preview.StopAsync();
   });
   await Test("audio-only settings preserve live LTC session",async()=>{
    int f=10;await w.Monitor.ConfigureAsync(new(Rate:"29.97 DF"),async(c,t)=>{while(!t.IsCancellationRequested){int n=Interlocked.Increment(ref f);c.Accept(new TcFrame{Seconds=n/30,Frame=n%30,Drop=1},MasterClock.Timestamp());await Task.Delay(33,t);}});double end=MasterClock.Timestamp()+5;while(w.PreviewObservation?.FramePts is not >0&&MasterClock.Timestamp()<end)await Task.Delay(10);Check(w.SynchronizationSession!=null,"LTC run did not start");var session=w.SynchronizationSession;int starts=w.Monitor.Starts;var clock=w.Monitor.Clock;var saved=InputSettings.Load(Program.Option("--settings"));await w.ApplySettingsAsync(new(saved with{AudioDeviceId=saved.AudioDeviceId==""?AudioOutputSink.Devices()[0].Id:""},display));await Task.Delay(180);Check(w.SynchronizationSession==session&&w.Monitor.Starts==starts&&ReferenceEquals(clock,w.Monitor.Clock),"audio settings reset LTC");await w.StopSyncPreviewAsync();await w.Monitor.ConfigureAsync(new(Rate:"29.97 DF"));
   });
   await Test("manual preview measured source and presentation FPS",async()=>{
    var measurements=new List<object>();foreach(string media in new[]{Path.Combine(AppContext.BaseDirectory,"TestMedia","HAP_1_1080p_2997_10s.mov"),row.FilePath}){
     await w.Preview.LoadAsync(media);await w.Preview.PlayAsync();await Task.Delay(300);var p=w.Preview.Snapshot;var monitor=w.Surface.Canvas.PreviewStatus();double began=MasterClock.Timestamp();double worstLag=0;
     while(MasterClock.Timestamp()-began<2.5){await Task.Delay(25);var current=w.Preview.Snapshot;if(current.FramePts is {} pts)worstLag=Math.Max(worstLag,current.Position-pts);}
     double elapsed=MasterClock.Timestamp()-began;var end=w.Preview.Snapshot;var after=w.Surface.Canvas.PreviewStatus();var sample=new{File=Path.GetFileName(media),SourceFps=(end.Presented-p.Presented)/elapsed,PreviewFps=(after.Presented-monitor.Presented)/elapsed,WorstLag=worstLag};measurements.Add(sample);await w.Preview.StopAsync();Check(sample.PreviewFps>=27&&sample.PreviewFps<=31.5&&sample.SourceFps>=20&&worstLag<.2,"source FPS/lag failed: "+JsonSerializer.Serialize(sample));
    }File.WriteAllText(Path.Combine(folder,"fps.json"),JsonSerializer.Serialize(measurements,new JsonSerializerOptions{WriteIndented=true}));
   });
  }catch(Exception e){failed=true;File.WriteAllText(Path.Combine(folder,"fatal.txt"),e.ToString());}
  finally{await w.Coordinator.DisposeAsync();await w.Preview.DisposeAsync();await w.Monitor.DisposeAsync();MainWindow.Shutdown(failed?1:0);}
 }
}
