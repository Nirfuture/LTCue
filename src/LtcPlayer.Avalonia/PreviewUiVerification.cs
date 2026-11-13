using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using LtcPlayer.App;
namespace LtcPlayer.Avalonia;
public static class PreviewUiVerification
{
 public static async Task RunAsync(MainWindow w,string folder)
 {
  Directory.CreateDirectory(folder);bool failed=false;var results=new List<object>();
  T C<T>(string n)where T:Control=>w.FindControl<T>(n)!;
  void Check(bool ok,string message){if(!ok)throw new IOException(message);}
  async Task Test(string name,Func<Task> body){try{await body();results.Add(new{test=name,status="PASS"});}catch(Exception e){failed=true;results.Add(new{test=name,status="FAIL",detail=e.ToString()});}File.WriteAllText(Path.Combine(folder,"results.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));}
  // Hold the actual busy flag so the normal timer continues to exercise the same activity path.
  var busy=typeof(MainWindow).GetField("busy",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
  try{
   w.Width=1640;w.Height=1040;await Task.Delay(200);w.UpdateResponsiveLayout();
   await Test("seek is directly below viewport before title and time",()=>{
    var viewport=C<Grid>("PreviewViewport");var seek=C<Slider>("Seek");double bottom=viewport.TranslatePoint(default,w)!.Value.Y+viewport.Bounds.Height,y=seek.TranslatePoint(default,w)!.Value.Y;
    Check(y>=bottom&&y-bottom<=16,"seek not directly below: "+(y-bottom));Check(y+seek.Bounds.Height<=C<TextBlock>("PreviewName").TranslatePoint(default,w)!.Value.Y,"title before seek");return Task.CompletedTask;
   });
   await Test("short activity never displays progress",async()=>{
    busy.SetValue(w,true);w.SetActivity(true,"短い処理");await Task.Delay(40);Check(C<ProgressBar>("ActivityBar").Opacity==0,"short flash");busy.SetValue(w,false);w.SetActivity(false,"");await Task.Delay(200);Check(C<ProgressBar>("ActivityBar").Opacity==0&&!C<ProgressBar>("ActivityBar").IsIndeterminate,"late flash");
   });
   await Test("long activity preserves viewport and native host geometry",async()=>{
    await Task.Delay(150);var before=C<Grid>("PreviewViewport").Bounds;var frame=C<Border>("OutputFrame").Bounds;var session=w.Surface.Canvas.SessionId;
    busy.SetValue(w,true);w.SetActivity(true,"長い処理");await Task.Delay(260);Check(C<ProgressBar>("ActivityBar").Opacity==1,"no delayed progress");Check(C<Grid>("PreviewViewport").Bounds==before&&C<Border>("OutputFrame").Bounds==frame,"activity resized preview");
    busy.SetValue(w,false);w.SetActivity(false,"");await Task.Delay(150);Check(C<Grid>("PreviewViewport").Bounds==before&&C<Border>("OutputFrame").Bounds==frame&&w.Surface.Canvas.SessionId==session,"completion resized/recreated preview");File.WriteAllText(Path.Combine(folder,"geometry.json"),JsonSerializer.Serialize(new{Viewport=before,Frame=frame}));
   });
   await Test("fade input persists immediate and timed durations",()=>{foreach(decimal seconds in new[]{0m,.5m,1m,2m}){w.SetFadeSeconds(seconds);Check(C<NumericUpDown>("FadeSeconds").Value==seconds&&InputSettings.Load(Program.Option("--settings")).FadeSeconds==(double)seconds,"duration not saved");}return Task.CompletedTask;});
   await Test("custom fade and empty input restoration",()=>{
    w.SetFadeSeconds(.7m);Check(InputSettings.Load(Program.Option("--settings")).FadeSeconds==.7,"custom lost");var input=C<NumericUpDown>("FadeSeconds");input.Value=null;input.RaiseEvent(new RoutedEventArgs(InputElement.LostFocusEvent));Check(input.Value==.7m,"blank not restored");return Task.CompletedTask;
   });
   await Test("immediate and timed fade apply in both directions",async()=>{
    w.SetFadeSeconds(0);w.SetCoverVisible(true);Check(w.Cover.Amount()==1&&!w.Cover.Transitioning,"instant not immediate");w.SetCoverVisible(false);Check(w.Cover.Amount()==0,"instant return");
    w.SetFadeSeconds(.5m);w.SetCoverVisible(true);await Task.Delay(100);Check(w.Cover.Amount()>0&&w.Cover.Amount()<1,"fade missing");await Task.Delay(450);Check(w.Cover.Amount()==1,"fade endpoint");w.SetCoverVisible(false);await Task.Delay(100);Check(w.Cover.Amount()>0&&w.Cover.Amount()<1,"return fade missing");await Task.Delay(450);Check(w.Cover.Amount()==0,"return endpoint");
   });
   await Test("manual playback preparation keeps preview bounds stable",async()=>{
    await w.Preview.LoadAsync(Path.Combine(Program.Option("--fixtures")!,"h264-aac.mp4"));await Task.Delay(150);var before=C<Grid>("PreviewViewport").Bounds;var frame=C<Border>("OutputFrame").Bounds;await w.Preview.PlayAsync();await Task.Delay(100);Check(C<Grid>("PreviewViewport").Bounds==before&&C<Border>("OutputFrame").Bounds==frame,"initial play resized preview");await w.Preview.PauseAsync();
    for(int i=0;i<3;i++){await w.Preview.PlayAsync();await Task.Delay(50);await w.Preview.PauseAsync();Check(C<Grid>("PreviewViewport").Bounds==before&&C<Border>("OutputFrame").Bounds==frame,"play/pause resized preview");}
   });
   using var bitmap=new RenderTargetBitmap(new PixelSize((int)w.ClientSize.Width,(int)w.ClientSize.Height),new Vector(96,96));bitmap.Render(w);bitmap.Save(Path.Combine(folder,"layout.png"));
  }catch(Exception e){failed=true;File.WriteAllText(Path.Combine(folder,"fatal.txt"),e.ToString());}
  finally{busy.SetValue(w,false);w.SetActivity(false,"");await w.StopSyncPreviewAsync();await w.DisposeSharedAudioAsync();await w.Preview.DisposeAsync();await w.Monitor.DisposeAsync();MainWindow.Shutdown(failed?1:0);}
 }
}
