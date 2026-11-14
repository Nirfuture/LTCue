using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
namespace LtcPlayer.Avalonia;
public static class CoverHeightVerification
{
 public static async Task RunAsync(MainWindow w,string folder)
 {
  Directory.CreateDirectory(folder);var results=new List<object>();bool failed=false;
  var body=w.FindControl<Grid>("Workspace")!;var preview=w.FindControl<Grid>("PreviewPane")!;var left=w.FindControl<Border>("EditorPane")!;var cover=w.FindControl<Border>("CoverPane")!;var content=w.FindControl<StackPanel>("CoverContent")!;var name=w.FindControl<TextBlock>("CoverName")!;
  void Check(bool ok,string why){if(!ok)throw new IOException(why);}
  double Bottom(Control c)=>c.TranslatePoint(default,body)!.Value.Y+c.Bounds.Height;
  double Natural()=>content.Children.Where(c=>c.IsVisible).Sum(c=>c.DesiredSize.Height)+(content.Children.Count(c=>c.IsVisible)-1)*content.Spacing+cover.Padding.Top+cover.Padding.Bottom+cover.BorderThickness.Top+cover.BorderThickness.Bottom;
  async Task Test(string name,Func<Task> action){try{await action();results.Add(new{test=name,status="PASS",Window=w.ClientSize,LeftBottom=Bottom(left),CoverBottom=Bottom(cover),CoverHeight=cover.Bounds.Height,NaturalHeight=Natural()});}catch(Exception e){failed=true;results.Add(new{test=name,status="FAIL",detail=e.ToString()});}File.WriteAllText(Path.Combine(folder,"results.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));}
  async Task Size(int width,int height){w.Width=width;w.Height=height;await Task.Delay(250);w.UpdateResponsiveLayout();await Task.Delay(100);}
  void NaturalHeight()=>Check(Math.Abs(cover.Bounds.Height-Natural())<1.1,"cover was stretched or clipped");
  void Capture(string name){using var bitmap=new RenderTargetBitmap(new PixelSize((int)w.ClientSize.Width,(int)w.ClientSize.Height),new Vector(96,96));bitmap.Render(w);bitmap.Save(Path.Combine(folder,name+".png"));}
  Size initial=default;
  try{
   await Test("initial window fits right content with aligned bottoms and no added cover space",async()=>{await Task.Delay(150);initial=w.ClientSize;NaturalHeight();Check(!w.CompactLayout&&Math.Abs(Bottom(left)-Bottom(cover))<1.1,"initial bottoms differ");Check(Math.Abs(w.FindControl<ScrollViewer>("WorkspaceScroll")!.Bounds.Height-preview.Bounds.Height)<1.1,"initial window height does not fit content");Capture("initial");});
   await Test("manual height increase grows only list",async()=>{double height=cover.Bounds.Height;await Size((int)initial.Width,(int)initial.Height+120);NaturalHeight();Check(Math.Abs(cover.Bounds.Height-height)<1.1&&Bottom(left)>Bottom(cover)+100,"right pane grew with window");Capture("resized-taller");});
   await Test("initial fitting cannot override later user resizing",async()=>{double height=w.Height;w.FitInitialContentHeight();await Task.Delay(100);Check(w.Height==height,"initial fitting overwrote manual size");});
   await Test("short wide window preserves natural controls and reachable bottom",async()=>{await Size(1640,500);NaturalHeight();Check(Math.Abs(Bottom(left)-Bottom(cover))<1.1,"short window mismatched natural content");var scroll=w.FindControl<ScrollViewer>("WorkspaceScroll")!;scroll.Offset=new Vector(0,scroll.Extent.Height);await Task.Delay(60);Check(scroll.Offset.Y>=scroll.Extent.Height-scroll.Viewport.Height-2,"bottom unreachable");scroll.Offset=default;});
   await Test("many cue rows stay inside the bounded list viewport",async()=>{
    double windowHeight=w.Height;var existing=w.Editor.Rows.ToArray();
    try{for(int n=0;n<1000;n++)w.Editor.Rows.Add(new LtcPlayer.App.CueRow{Enabled=false});await Task.Delay(200);var scroll=w.FindControl<ScrollViewer>("WorkspaceScroll")!;Check(left.Bounds.Height<=Math.Max(Math.Max(360,scroll.Bounds.Height),preview.Bounds.Height)+1.1,"cue rows expanded the outer pane");Check(w.FindControl<DataGrid>("CueGrid")!.Bounds.Height<left.Bounds.Height&&w.Height==windowHeight,"list grew beyond its viewport or resized window");}
    finally{w.Editor.Rows.Clear();foreach(var row in existing)w.Editor.Rows.Add(row);w.Editor.Validate();}
   });
   await Test("long filename keeps cover controls inside the natural card",async()=>{await Size(1640,940);double old=cover.Bounds.Height,windowHeight=w.Height;string? original=name.Text;var timer=(global::Avalonia.Threading.DispatcherTimer)typeof(MainWindow).GetField("coverTimer",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(w)!;timer.Stop();try{name.Text=new string('長',200)+".png";await Task.Delay(200);Check(name.Text.Length>200,"filename replaced before measurement");NaturalHeight();var clear=w.FindControl<Button>("ClearCoverButton")!;Check(Math.Abs(cover.Bounds.Height-old)<1.1&&w.Height==windowHeight,"filename grew the card or window");Check(name.Bounds.Width>0&&clear.TranslatePoint(default,cover)!.Value.X+clear.Bounds.Width<=cover.Bounds.Width-cover.Padding.Right+1,"filename displaced controls");}finally{name.Text=original;timer.Start();}});
   await Test("narrow mode remains stacked and scrollable",async()=>{await Size(700,1100);Check(w.CompactLayout&&Grid.GetRow(preview)==2,"narrow preview not stacked");NaturalHeight();var scroll=w.FindControl<ScrollViewer>("WorkspaceScroll")!;scroll.Offset=new Vector(0,scroll.Extent.Height);await Task.Delay(60);Check(scroll.Offset.Y>=scroll.Extent.Height-scroll.Viewport.Height-2,"stacked cover unreachable");});
   await Test("returning to wide view is stable and keeps right cards at top",async()=>{await Size(1640,940);NaturalHeight();double bottom=Bottom(cover);await Task.Delay(300);Check(Math.Abs(Bottom(cover)-bottom)<.1,"layout oscillates");var audio=(Control)preview.Children[1];double gap=cover.TranslatePoint(default,preview)!.Value.Y-(audio.TranslatePoint(default,preview)!.Value.Y+audio.Bounds.Height);Check(Math.Abs(gap-cover.Margin.Top)<1.1,"cover floated below audio");});
  }catch(Exception e){failed=true;File.WriteAllText(Path.Combine(folder,"fatal.txt"),e.ToString());}
  finally{await w.Monitor.DisposeAsync();MainWindow.Shutdown(failed?1:0);}
 }
}
