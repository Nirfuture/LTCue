using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using LtcPlayer.App;
namespace LtcPlayer.Avalonia;
public static class ResponsiveVerification
{
 static void Check(bool value,string message){if(!value)throw new IOException(message);}
 public static async Task RunAsync(MainWindow window,string folder){
  Directory.CreateDirectory(folder);var results=new List<object>();bool failed=false;
  async Task Test(string name,Func<Task<object>> body){try{results.Add(new{test=name,status="PASS",detail=await body()});}catch(Exception e){failed=true;results.Add(new{test=name,status="FAIL",detail=e.ToString()});}}
  try{
   await Test("New freewheel default / legacy override / clock loss threshold",()=>{
    Check(new InputSettings().Freewheel==.1,"new settings default");var mode=TimecodeRate.Modes[1];var clock=new MasterClock(mode);clock.Accept(new TcFrame(),0);
    Check(clock.FreewheelSeconds==.1&&clock.Read(.099).State!=SyncState.LOST&&clock.Read(.101).State==SyncState.LOST,"default clock threshold");
    string file=Path.Combine(folder,"legacy.json");File.WriteAllText(file,"{\"Freewheel\":0.5}");Check(InputSettings.Load(file).Freewheel==.5,"legacy value overwritten");
    File.WriteAllText(file,"{}");Check(InputSettings.Load(file).Freewheel==.1,"missing setting default");return Task.FromResult<object>(new{Default=.1,Legacy=.5});
   });
   if(window.Editor.Rows.Count==0)await DefaultTestShow.LoadAsync(window.Editor);
   foreach(var size in new[]{(Width:1640,Height:940),(Width:700,Height:1100),(Width:800,Height:600),(Width:1640,Height:500),(Width:480,Height:420)}){
    await Test("Layout / "+size.Width+"x"+size.Height,async()=>{
     window.Width=size.Width;window.Height=size.Height;await Task.Delay(250);window.UpdateResponsiveLayout();await Task.Delay(100);
     var workspace=window.FindControl<Grid>("Workspace")!;var scroll=window.FindControl<ScrollViewer>("WorkspaceScroll")!;var editor=window.FindControl<Border>("EditorPane")!;var preview=window.FindControl<Grid>("PreviewPane")!;
     bool compact=size.Width<1100||size.Height>=size.Width;Check(window.CompactLayout==compact,"breakpoint");Check(editor.Bounds.Width<=scroll.Viewport.Width+2&&preview.Bounds.Width<=scroll.Viewport.Width+2,"pane horizontally clipped");
     if(compact){Check(Grid.GetRow(preview)==2&&Grid.GetColumn(preview)==0,"preview not stacked");Check(scroll.VerticalScrollBarVisibility==ScrollBarVisibility.Auto,"content not scrollable");scroll.Offset=new Vector(0,scroll.Extent.Height);await Task.Delay(50);Check(scroll.Offset.Y>=Math.Max(0,scroll.Extent.Height-scroll.Viewport.Height)-2,"bottom unreachable");}
     else Check(Grid.GetColumn(preview)==2&&Grid.GetRow(preview)==0,"wide preview placement");
     Check(window.FindControl<Button>("ArmButton")!.IsEffectivelyVisible&&window.FindControl<Button>("SettingsButton")!.IsEffectivelyVisible,"top actions hidden");
     var detail=new{Width=window.Bounds.Width,Height=window.Bounds.Height,Compact=window.CompactLayout,Viewport=scroll.Viewport,Extent=scroll.Extent};scroll.Offset=default;return detail;
    });
   }
   await Test("Settings dialog stays usable at narrow size",async()=>{
    var dialog=new SettingsDialog(new(OutputMode:OutputPresentationMode.Compatibility),VideoRenderer.Displays()[0],false);dialog.Width=420;dialog.Height=420;var task=dialog.ShowDialog<SettingsResult?>(window);
    try{await Task.Delay(200);var controls=((Control)dialog.Content!).GetLogicalDescendants().OfType<Control>().ToArray();var apply=controls.OfType<Button>().Single(b=>Equals(b.Content,"適用"));var location=apply.TranslatePoint(default,dialog)!.Value;
     Check(location.Y>=0&&location.Y+apply.Bounds.Height<=dialog.ClientSize.Height,"apply clipped");Check(location.X>=0&&location.X+apply.Bounds.Width<=dialog.ClientSize.Width,"apply clipped horizontally");
     var scroll=controls.OfType<ScrollViewer>().Single();Check(scroll.Viewport.Height>40,"fields do not scroll");return new{dialog.ClientSize,scroll.Viewport,scroll.Extent,ApplyY=location.Y};
    }finally{dialog.Close(null);await task;}
   });
   await Test("Move to portrait display fits working area",async()=>{
    var screen=window.Screens.All.FirstOrDefault(s=>s.Bounds.Height>s.Bounds.Width);if(screen==null)return "No portrait screen available; covered by narrow size cases";
    window.Width=1640;window.Height=940;await Task.Delay(200);window.Position=new PixelPoint(screen.WorkingArea.X+20,screen.WorkingArea.Y+20);await Task.Delay(650);
    var frame=window.FrameSize??window.ClientSize;double width=frame.Width*screen.Scaling,height=frame.Height*screen.Scaling;
    Check(width<=screen.WorkingArea.Width+3&&height<=screen.WorkingArea.Height+3,"window larger than portrait work area");Check(window.CompactLayout,"portrait did not switch layout");
    Check(window.ClientSize.Width>=Math.Min(1640,screen.WorkingArea.Width/screen.Scaling)-60,"portrait fit shrunk width too far");
    Check(window.ClientSize.Height>=Math.Min(940,screen.WorkingArea.Height/screen.Scaling)-110,"portrait fit shrunk height too far");
    Check(window.Position.X>=screen.WorkingArea.X&&window.Position.Y>=screen.WorkingArea.Y,"window starts outside portrait area");
    Check(window.Position.X+width<=screen.WorkingArea.Right+3&&window.Position.Y+height<=screen.WorkingArea.Bottom+3,"window extends outside portrait area");
    if(Program.Arguments.Contains("--visual-responsive"))await Task.Delay(30000);
    return new{window.Position,window.ClientSize,window.FrameSize,window.RenderScaling,screen.WorkingArea,screen.Scaling};
   });
  }finally{await window.Monitor.DisposeAsync();File.WriteAllText(Path.Combine(folder,"results.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));MainWindow.Shutdown(failed?1:0);}
 }
}
