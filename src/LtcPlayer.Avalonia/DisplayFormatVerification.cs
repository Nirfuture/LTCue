using System.Text.Json;
using Avalonia.Controls;
namespace LtcPlayer.Avalonia;
public static class DisplayFormatVerification
{
 public static void Run(MainWindow window,string folder)
 {
  Directory.CreateDirectory(folder);bool failed=false;var results=new List<object>();
  foreach(var c in new[]{(0d,25d,"00:00:00:00"),(1.24,25,"00:00:01:06"),(3661.5,30,"01:01:01:15"),(60.1,25,"00:01:00:02"),(60,30000d/1001,"00:01:00:00")}){
   string actual=MediaTimeDisplay.Format(c.Item1,c.Item2);bool pass=actual==c.Item3;failed|=!pass;results.Add(new{Seconds=c.Item1,Fps=c.Item2,Expected=c.Item3,Actual=actual,Pass=pass});
  }
  bool inputLabelRemoved=window.FindControl<TextBlock>("SyncLabel")==null;failed|=!inputLabelRemoved;
  File.WriteAllText(Path.Combine(folder,"display-format.json"),JsonSerializer.Serialize(new{Cases=results,InputLabelRemoved=inputLabelRemoved},new JsonSerializerOptions{WriteIndented=true}));
  File.WriteAllText(Path.Combine(folder,"exit-code.txt"),failed?"1":"0");MainWindow.Shutdown(failed?1:0);
 }
}
