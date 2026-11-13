using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using LtcPlayer.App;
namespace LtcPlayer.Avalonia;
public static partial class DefaultTestShow
{
 public static readonly int[] Starts=[5,20,35,50];
 public static async Task LoadAsync(ShowEditor editor)
 {
  var rows=new List<CueRow>();
  for(int i=0;i<4;i++){
   var row=new CueRow{FilePath=Path.Combine(AppContext.BaseDirectory,"TestMedia",$"HAP_{i+1}_1080p_2997_10s.mov")};row.SetStart($"00:00:{Starts[i]:00}:00");
   await ShowProgramService.RefreshAsync(row,CancellationToken.None);rows.Add(row);
  }
  foreach(var row in rows)editor.Add(row);
 }
}
