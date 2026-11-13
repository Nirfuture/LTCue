using System.Text.Json;
using Avalonia.Controls;
using LtcPlayer.App;
namespace LtcPlayer.Avalonia;
public static class PortraitMonitorVerification
{
 static void Check(bool value,string message){if(!value)throw new IOException(message);}
 public static async Task RunAsync(MainWindow window,string folder){
  Directory.CreateDirectory(folder);var results=new List<object>();bool failed=false;
  try{
   foreach(var display in VideoRenderer.Displays().OrderBy(d=>d.Width/(double)d.Height)){
    try{
     await window.ApplySettingsAsync(new(new(Rate:"29.97 DF"),display));
     if(window.Editor.Rows.Count==0){var row=new CueRow{FilePath=Path.Combine(AppContext.BaseDirectory,"TestMedia","HAP_3_1080p_2997_10s.mov")};row.SetStart("00:00:00:00");await ShowProgramService.RefreshAsync(row,CancellationToken.None);window.Editor.Add(row);}
     await window.ArmAsync(async(clock,token)=>{while(!token.IsCancellationRequested){clock.Accept(new TcFrame{Seconds=2,Drop=1},MasterClock.Timestamp());await Task.Delay(33,token);}});
     for(int i=0;i<500&&window.LatestObservation?.FramePts is not >0;i++)await Task.Delay(15);
     Check(window.LatestObservation?.FramePts is >0,"video did not start");await Task.Delay(250);window.UpdatePreviewGeometry();
     var frame=window.FindControl<Border>("OutputFrame")!;var viewport=window.FindControl<Grid>("PreviewViewport")!;
     double ratio=(frame.Bounds.Width-4)/(frame.Bounds.Height-4);
     Check(Math.Abs(ratio-display.Width/(double)display.Height)<.01,"output frame aspect mismatch");
     Check(frame.Bounds.Width<=viewport.Bounds.Width&&frame.Bounds.Height<=viewport.Bounds.Height,"output frame overflow");
     Check(window.FindControl<TextBlock>("OutputGeometry")!.Text!.Contains(MainWindow.GeometryLabel(display.Width,display.Height)),"output size label");
     Check(window.FindControl<TextBlock>("VideoGeometry")!.Text!.Contains("1920×1080"),"video size label");
     string stats=window.FindControl<TextBlock>("RunInfo")!.Text!;Check(stats.Contains("スキップフレーム数")&&!stats.Contains("提示")&&!stats.Contains("読み飛ばし"),"stats wording");
     Check(Equals(window.FindControl<Button>("ArmButton")!.Content,"出力停止"),"output button wording");
     if(Program.Arguments.Contains("--visual-monitor"))await Task.Delay(15000);
     results.Add(new{test=display.Name,status="PASS",detail=new{display.Width,display.Height,FrameWidth=frame.Bounds.Width,FrameHeight=frame.Bounds.Height,Output=window.FindControl<TextBlock>("OutputGeometry")!.Text,Video=window.FindControl<TextBlock>("VideoGeometry")!.Text}});
    }catch(Exception e){failed=true;results.Add(new{test=display.Name,status="FAIL",detail=e.ToString()});}
    finally{await window.DisarmAsync();}
   }
  }finally{await window.Monitor.DisposeAsync();File.WriteAllText(Path.Combine(folder,"results.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));MainWindow.Shutdown(failed?1:0);}
 }
}
