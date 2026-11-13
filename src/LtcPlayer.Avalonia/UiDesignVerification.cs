using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using System.Text.Json;
using LtcPlayer.App;
namespace LtcPlayer.Avalonia;
public static class UiDesignVerification
{
 public static async Task RunAsync(MainWindow w,string folder){
  Directory.CreateDirectory(folder);bool failed=false;var results=new List<object>();
  T C<T>(string name)where T:Control=>w.FindControl<T>(name)!;
  void Check(bool ok,string text){if(!ok)throw new IOException(text);}
  async Task Test(string name,Func<Task> action){try{await action();results.Add(new{test=name,status="PASS"});}catch(Exception e){failed=true;results.Add(new{test=name,status="FAIL",detail=e.ToString()});}File.WriteAllText(Path.Combine(folder,"results.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));}
  void Capture(string name){using var bitmap=new RenderTargetBitmap(new PixelSize((int)w.ClientSize.Width,(int)w.ClientSize.Height),new Vector(96,96));bitmap.Render(w);bitmap.Save(Path.Combine(folder,name+".png"));}
  try{
   await Test("single speaker control and always visible playback information",async()=>{
    Check(w.FindControl<CheckBox>("Mute")==null,"duplicate mute remains");Check(C<ToggleButton>("AudioEnabled").IsEffectivelyVisible,"speaker hidden");Check(!C<Grid>("PreviewPane").GetLogicalDescendants().OfType<Expander>().Any(),"playback information collapsed");await Task.Delay(150);Check(C<TextBlock>("PreviewInfo").Text==""&&C<TextBlock>("RunInfo").Text=="","internal idle state visible");Check(C<TextBlock>("ModeLabel").Text=="プレビュー"&&C<TextBlock>("LtcStateLabel").Text=="LTC待機","heading mismatch");
   });
   var row=new CueRow{FilePath=Path.Combine(Program.Option("--fixtures")!,"h264-aac.mp4")};await ShowProgramService.RefreshAsync(row,CancellationToken.None);w.Editor.Add(row);C<DataGrid>("CueGrid").SelectedItem=row;double deadline=MasterClock.Timestamp()+5;while(w.Preview.Snapshot.State!=PreviewState.Ready&&MasterClock.Timestamp()<deadline)await Task.Delay(10);Check(w.Preview.Snapshot.State==PreviewState.Ready,"capture media not loaded");await Task.Delay(150);
   foreach(var size in new[]{(1640,940),(1640,500),(700,1100),(480,420)})await Test("layout "+size.Item1+"x"+size.Item2,async()=>{
    w.Width=size.Item1;w.Height=size.Item2;await Task.Delay(200);w.UpdateResponsiveLayout();await Task.Delay(100);
    bool portrait=size.Item1<1100||size.Item2>=size.Item1;Check(w.CompactLayout==portrait,"orientation changed with height alone");var pane=C<Grid>("PreviewPane");Check(Grid.GetColumn(pane)==(portrait?0:2),"third column introduced");
    var viewport=C<Grid>("PreviewViewport");var card=C<Border>("PreviewCard");Check(viewport.Bounds.Height>=160&&viewport.Bounds.Bottom<=card.Bounds.Height,"preview exceeds card");
    var seek=C<Slider>("Seek");double previewBottom=viewport.TranslatePoint(default,w)!.Value.Y+viewport.Bounds.Height;Check(seek.TranslatePoint(default,w)!.Value.Y>=previewBottom,"seek overlaps preview");
    double infoBottom=C<TextBlock>("RunInfo").TranslatePoint(default,w)!.Value.Y+C<TextBlock>("RunInfo").Bounds.Height;Check(infoBottom<C<ToggleButton>("AudioEnabled").TranslatePoint(default,w)!.Value.Y,"playback info below audio");
    var scroll=C<ScrollViewer>("WorkspaceScroll");scroll.Offset=default;Capture(size.Item1+"x"+size.Item2);scroll.Offset=new Vector(0,scroll.Extent.Height);await Task.Delay(60);Check(scroll.Offset.Y>=Math.Max(0,scroll.Extent.Height-scroll.Viewport.Height)-2,"bottom inaccessible");Capture(size.Item1+"x"+size.Item2+"-bottom");scroll.Offset=default;
   });
  }finally{await w.Coordinator.DisposeAsync();await w.Preview.DisposeAsync();await w.Monitor.DisposeAsync();MainWindow.Shutdown(failed?1:0);}
 }
}
