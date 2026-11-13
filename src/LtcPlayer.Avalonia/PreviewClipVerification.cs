using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.VisualTree;
using System.Text.Json;
using LtcPlayer.App;
namespace LtcPlayer.Avalonia;
public static class PreviewClipVerification
{
 public static async Task RunAsync(MainWindow w,string folder){
  Directory.CreateDirectory(folder);bool failed=false;var results=new List<object>();
  void Check(bool ok,string message){if(!ok)throw new IOException(message);}
  async Task Test(string name,Func<Task> body){try{await body();results.Add(new{test=name,status="PASS"});}catch(Exception e){failed=true;results.Add(new{test=name,status="FAIL",detail=e.ToString()});}File.WriteAllText(Path.Combine(folder,"results.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));}
  var scroll=w.FindControl<ScrollViewer>("WorkspaceScroll")!;var host=w.Surface;
  async Task Settle(){await Task.Delay(250);}
  void CheckClip(){
   var presenter=scroll;var point=host.TranslatePoint(new Point(),presenter)!.Value;double scale=w.RenderScaling;
   var expected=new Rect(host.Bounds.Size).Intersect(new Rect(-point.X,-point.Y,presenter.Bounds.Width,presenter.Bounds.Height));var actual=host.NativeClipForTest();
   if(expected.Width<=0||expected.Height<=0)Check(actual.Right==actual.Left&&actual.Bottom==actual.Top,"hidden native preview still visible");
   else Check(Math.Abs(actual.Left-expected.Left*scale)<=2&&Math.Abs(actual.Top-expected.Top*scale)<=2&&Math.Abs(actual.Right-expected.Right*scale)<=2&&Math.Abs(actual.Bottom-expected.Bottom*scale)<=2,"clip differs from scroll viewport: "+actual+" expected "+expected);
  }
  try{
   await Test("audio heading and duplicate status removed",()=>{Check(w.FindControl<TextBlock>("AudioStatus")==null,"duplicate label");Check(w.FindControl<StackPanel>("PreviewControls")!.GetVisualDescendants().OfType<TextBlock>().Any(x=>x.Text=="音声"),"heading");return Task.CompletedTask;});
   await w.Preview.UseSharedAudioAsync();await w.Preview.LoadAsync(Path.Combine(Program.Option("--fixtures")!,"hap-sync-30s.mov"));await w.Preview.PlayAsync();var session=host.Canvas.SessionId;
   w.Width=700;w.Height=600;await Settle();
   await Test("scroll partially clips native preview",async()=>{var p=host.TranslatePoint(new Point(),scroll)!.Value;scroll.Offset=new Vector(0,scroll.Offset.Y+p.Y+host.Bounds.Height*.35);await Settle();CheckClip();Check(host.NativeClipForTest().Top>0,"partial clipping not exercised");});
   await Test("short wide window clips the bottom edge",async()=>{w.Width=1640;w.Height=420;scroll.Offset=default;await Settle();CheckClip();var region=host.NativeClipForTest();Check(region.Bottom>region.Top&&region.Bottom<host.Bounds.Height*w.RenderScaling-2,"bottom clipping not exercised");});
   await Test("scroll restores native preview without replacing canvas",async()=>{w.Height=1100;scroll.Offset=new Vector(0,0);await Settle();CheckClip();Check(host.Canvas.SessionId==session,"scroll recreated canvas");});
   await Test("short window clips fully hidden preview",async()=>{w.Width=480;w.Height=420;scroll.Offset=new Vector(0,0);await Settle();CheckClip();Check(host.NativeClipForTest().Bottom==0,"fully hidden fixture not exercised");});
   await Test("resizing and scrolling retain playback and output geometry",async()=>{double before=w.Preview.Snapshot.Position;foreach(var size in new[]{(1640,500),(800,600),(1640,940)}){w.Width=size.Item1;w.Height=size.Item2;await Settle();CheckClip();}Check(host.Canvas.SessionId==session&&w.Preview.Snapshot.State==PreviewState.Playing&&w.Preview.Snapshot.Position>before,"clipping interrupted playback");});
   await Test("portrait selection sizes the viewport before window resize",async()=>{
    w.Width=1640;w.Height=940;await Settle();
    var displays=VideoRenderer.Displays();var landscape=displays.First(d=>d.Primary!=0);var portrait=displays.FirstOrDefault(d=>d.Height>d.Width);
    bool synthetic=portrait.Width==0;
    if(synthetic){portrait=new OutputDisplay{Name="SYNTHETIC-LAYOUT-PORTRAIT",Width=1080,Height=1920};w.SettingsDisplaysForTest=()=>[..displays,portrait];}
    // This case checks canvas/layout only; no physical external output is attached.
    try{
     var settings=InputSettings.Load(Program.Option("--settings"));
     await w.ApplySettingsAsync(new(settings,landscape));await Settle();
     await w.ApplySettingsAsync(new(settings,portrait));await Settle();
     var viewport=w.FindControl<Grid>("PreviewViewport")!;var pane=w.FindControl<Grid>("PreviewPane")!;
     double expected=Math.Clamp(Math.Max(200,pane.Bounds.Width-26)*portrait.Height/portrait.Width,160,420);
     Check(Math.Abs(viewport.Bounds.Height-expected)<1,"portrait height remained stale: "+viewport.Bounds.Height+" expected "+expected);
     File.WriteAllText(Path.Combine(folder,"portrait-condition.json"),JsonSerializer.Serialize(new{SyntheticLayoutDisplay=synthetic,portrait.Width,portrait.Height,PhysicalPortraitOutputTested=false}));
    }finally{w.SettingsDisplaysForTest=null;}
   });
   await Test("portrait top-edge resizing preserves preview frame and canvas",async()=>{
    var frame=w.FindControl<Border>("OutputFrame")!;var before=frame.Bounds.Size;var canvas=host.Canvas.SessionId;var size=host.CanvasSize;
    int bottom=w.Position.Y+(int)(w.Height*w.RenderScaling);
    foreach(int height in new[]{900,760,600,500,700,940}){w.Height=height;w.Position=new PixelPoint(w.Position.X,bottom-(int)(height*w.RenderScaling));await Settle();CheckClip();Check(Math.Abs(frame.Bounds.Width-before.Width)<1&&Math.Abs(frame.Bounds.Height-before.Height)<1,"portrait preview changed during height-only resize");Check(host.Canvas.SessionId==canvas&&host.CanvasSize==size,"resizing changed output canvas");}
   });
  }catch(Exception e){failed=true;File.WriteAllText(Path.Combine(folder,"fatal.txt"),e.ToString());}
  finally{await w.Preview.DisposeAsync();await w.Monitor.DisposeAsync();MainWindow.Shutdown(failed?1:0);}
 }
}
