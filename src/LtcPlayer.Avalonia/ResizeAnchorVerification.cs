using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using LtcPlayer.App;
using System.Text.Json;
namespace LtcPlayer.Avalonia;
public static class ResizeAnchorVerification
{
 public static async Task RunAsync(MainWindow w,string folder){
  Directory.CreateDirectory(folder);var samples=new List<object>();var warningSamples=new List<object>();bool failed=false;
  try{
   await DefaultTestShow.LoadAsync(w.Editor);w.Width=1640;w.Height=940;await Task.Delay(200);var grid=w.FindControl<DataGrid>("CueGrid")!;var scroll=w.FindControl<ScrollViewer>("WorkspaceScroll")!;var pane=w.FindControl<Border>("EditorPane")!;var list=w.FindControl<Grid>("CueListViewport")!;
   var starts=w.Editor.Rows.Select(r=>r.StartTimecode).ToArray();
   void Restore(){for(int i=0;i<starts.Length;i++){w.Editor.Rows[i].SetStart(starts[i]);w.Editor.Rows[i].Enabled=true;}w.Editor.Validate();}
   foreach(var size in new[]{(Width:1640,Height:940),(Width:1640,Height:500),(Width:700,Height:1100),(Width:480,Height:420)}){
    Restore();w.Width=size.Width;w.Height=size.Height;scroll.Offset=default;await Task.Delay(180);scroll.Offset=default;await Task.Delay(60);
    var preview=w.FindControl<Grid>("PreviewPane")!;var hint=w.FindControl<TextBlock>("DeliveryHint")!;
    double listTop=list.TranslatePoint(default,w)!.Value.Y,previewTop=preview.TranslatePoint(default,w)!.Value.Y,viewportHeight=scroll.Viewport.Height;
    foreach(string state in new[]{"all-disabled","conflict","invalid-timecode","restored"}){
     Restore();
     if(state=="all-disabled")foreach(var row in w.Editor.Rows)row.Enabled=false;
     if(state=="conflict")w.Editor.Rows[1].SetStart(starts[0]);
     if(state=="invalid-timecode")w.Editor.Rows[0].Hours="99";
     await Task.Delay(140);
     double listDelta=list.TranslatePoint(default,w)!.Value.Y-listTop,previewDelta=preview.TranslatePoint(default,w)!.Value.Y-previewTop,heightDelta=scroll.Viewport.Height-viewportHeight;
     bool expectedVisible=state!="restored";
     bool passed=Math.Abs(listDelta)<.5&&Math.Abs(previewDelta)<.5&&Math.Abs(heightDelta)<.5&&hint.IsVisible==expectedVisible;
     warningSamples.Add(new{size.Width,size.Height,State=state,ListDelta=listDelta,PreviewDelta=previewDelta,ViewportHeightDelta=heightDelta,Warning=hint.Text,Visible=hint.IsVisible,Passed=passed});
     if(!passed)failed=true;
    }
   }
   Restore();w.Width=1640;w.Height=940;await Task.Delay(100);
   foreach(string focus in new[]{"list","volume"}){
    w.Height=940;scroll.Offset=default;await Task.Delay(120);if(focus=="volume")w.FindControl<Slider>("Volume")!.Focus();else{grid.SelectedItem=w.Editor.Rows.Last();grid.Focus();}scroll.Offset=default;await Task.Delay(80);double initial=list.TranslatePoint(default,w)!.Value.Y;
    foreach(int height in new[]{940,900,840,760,680,600,520,440,500,620,780,940}){w.Height=height;await Task.Delay(60);double top=list.TranslatePoint(default,w)!.Value.Y;var item=new{Focus=focus,Height=height,Top=top,Delta=top-initial,Offset=scroll.Offset.Y,PaneHeight=pane.Bounds.Height};samples.Add(item);if(Math.Abs(top-initial)>.5)failed=true;}
   }
  }catch(Exception e){failed=true;File.WriteAllText(Path.Combine(folder,"fatal.txt"),e.ToString());}
  finally{File.WriteAllText(Path.Combine(folder,"resize.json"),JsonSerializer.Serialize(new{Passed=!failed,Samples=samples,WarningSamples=warningSamples},new JsonSerializerOptions{WriteIndented=true}));await w.Coordinator.DisposeAsync();await w.Preview.DisposeAsync();await w.Monitor.DisposeAsync();MainWindow.Shutdown(failed?1:0);}
 }
}
