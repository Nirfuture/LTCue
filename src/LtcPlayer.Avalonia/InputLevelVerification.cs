using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.LogicalTree;
using LtcPlayer.App;
namespace LtcPlayer.Avalonia;
public static class InputLevelVerification
{
 public static async Task RunAsync(MainWindow w,string folder){
  Directory.CreateDirectory(folder);bool failed=false;var results=new List<object>();float amplitude=.1f;SettingsDialog? dialog=null;Task<SettingsResult?>? dialogTask=null;
  void Check(bool ok,string message){if(!ok)throw new IOException(message);}
  async Task Test(string name,Func<Task> body){try{await body();results.Add(new{test=name,status="PASS"});}catch(Exception e){failed=true;results.Add(new{test=name,status="FAIL",detail=e.ToString()});}File.WriteAllText(Path.Combine(folder,"results.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));}
  T C<T>(string n)where T:Control=>w.FindControl<T>(n)!;
  try{
   await Test("known amplitudes map to dBFS",()=>{foreach(var item in new[]{(1f,0.0),(.5f,-6.0206),(.1f,-20.0),(.001f,-60.0)}){var meter=new LtcInputLevel();meter.Push(item.Item1,10);var value=meter.Read(10);Check(value.HasSamples&&Math.Abs(value.Dbfs-item.Item2)<.001,"dBFS conversion");}return Task.CompletedTask;});
   await Test("zero stale and invalid samples never show a false level",()=>{var meter=new LtcInputLevel();Check(!meter.Read(10).HasSamples,"uninitialized input");meter.Push(0,10);Check(meter.Read(10).HasSamples&&double.IsNegativeInfinity(meter.Read(10).Dbfs),"zero sample");meter.Push(float.NaN,10);meter.Push(float.PositiveInfinity,10);meter.Push(-1,10);Check(meter.Read(10).Peak==0,"invalid sample accepted");meter.Push(.5f,10);Check(!meter.Read(10.301).HasSamples&&meter.Read(10.301).Peak==0,"stale level displayed");return Task.CompletedTask;});
   await Test("short peak hold and upper-limit indication",()=>{var meter=new LtcInputLevel();meter.Push(.995f,0);meter.Push(.1f,.2);Check(meter.Read(.2).NearLimit,"transient peak missed");meter.Push(.1f,.51);Check(!meter.Read(.51).NearLimit&&Math.Abs(meter.Read(.51).Dbfs+20)<.01,"hold stuck");meter.Reset();Check(!meter.Read(.51).HasSamples,"reset failed");return Task.CompletedTask;});
   await Test("live meter reuses input monitor without opening audio output",async()=>{
    var canvas=w.Surface.Canvas.SessionId;
    await w.Monitor.ConfigureAsync(new(Rate:"25"),async(clock,token)=>{int i=0;while(!token.IsCancellationRequested){w.Monitor.ReportInputLevel(Volatile.Read(ref amplitude),"入力中");int f=i++%100;clock.Accept(new TcFrame{Seconds=f/25,Frame=f%25},MasterClock.Timestamp());await Task.Delay(20,token);}});
    int starts=w.Monitor.Starts;dialog=new SettingsDialog(new(Rate:"25"),VideoRenderer.Displays()[0],false,false,w.Monitor);dialogTask=dialog.ShowDialog<SettingsResult?>(w);await Task.Delay(180);Check(w.FindControl<InputPeakBar>("InputLevelBar")==null,"meter remains on main window");Check(dialog.InputLevelText.Text!.Contains("-20.0"),"live dBFS absent");Check(C<TextBlock>("LtcStateLabel").Text=="LTC受信中","decode state replaced");Check(w.Monitor.Starts==starts&&w.Surface.Canvas.SessionId==canvas&&AudioOutputSink.ActiveStreams==0,"meter opened/reset resources");
   });
   await Test("near-limit level stays visible in a narrow window",async()=>{Volatile.Write(ref amplitude,.995f);dialog!.Width=420;dialog.Height=420;await Task.Delay(200);Check(dialog!.InputLevelText.Text!.Contains("上限付近"),"upper-limit warning absent");var text=dialog.InputLevelText;Check(text.Bounds.Width>0&&dialog.InputLevelBar.Bounds.Width>0,"meter compressed away");var scroll=dialog.GetLogicalDescendants().OfType<ScrollViewer>().Single();scroll.Offset=new Vector(0,scroll.Extent.Height);await Task.Delay(50);using var image=new RenderTargetBitmap(new PixelSize((int)dialog.ClientSize.Width,(int)dialog.ClientSize.Height),new Vector(96,96));image.Render(dialog);image.Save(Path.Combine(folder,"settings-narrow.png"));});
   await Test("input error and reconfiguration clear old level",async()=>{w.Monitor.ReportInputLevel(0,"接続エラー");Check(!w.Monitor.InputLevel.HasSamples,"error retained meter");await w.Monitor.ConfigureAsync(new(Rate:"25"),(_,t)=>Task.Delay(Timeout.Infinite,t));await Task.Delay(180);Check(!w.Monitor.InputLevel.HasSamples&&dialog!.InputLevelText.Text=="入力なし","reconfiguration retained meter");dialog!.Close(null);await dialogTask!;int refreshed=dialog.InputLevelRefreshes;await Task.Delay(150);Check(!dialog.InputLevelTimerRunning&&dialog.InputLevelRefreshes==refreshed,"closed dialog kept polling");});
  }finally{if(dialog!=null&&dialog.IsVisible){dialog.Close(null);if(dialogTask!=null)await dialogTask;}await w.Coordinator.DisposeAsync();await w.Preview.DisposeAsync();await w.Monitor.DisposeAsync();MainWindow.Shutdown(failed?1:0);}
 }
}
