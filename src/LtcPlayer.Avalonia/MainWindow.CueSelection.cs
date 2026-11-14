using Avalonia.Controls;
using LtcPlayer.App;
namespace LtcPlayer.Avalonia;
public partial class MainWindow
{
 // The DataGrid owns selection. Commands capture IDs rather than holding a second selection model.
 Guid[] SelectedCueIds()
 {
  var existing=Editor.Rows.Select(row=>row.CueId).ToHashSet();
  return Grid.SelectedItems.OfType<CueRow>().Select(row=>row.CueId).Where(existing.Contains).Distinct().ToArray();
 }
 void UpdateCueSelectionControls()
 {
  int count=Grid.SelectedItems.Count;bool allowed=CanEditProgram;
  var delete=Control<Button>("DeleteCueButton");delete.IsEnabled=allowed&&count>0;
  ToolTip.SetTip(delete,!allowed?ProgramEditAccess.Reason:count==0?"キューが未選択です":$"選択した{count}件を削除");
  var clone=Control<Button>("CloneCueButton");clone.IsEnabled=allowed&&count==1;
  ToolTip.SetTip(clone,!allowed?ProgramEditAccess.Reason:count==1?"選択したキューを複製":"1件だけ選択してください");
 }
 void RestoreCueSelection(IEnumerable<Guid> cueIds,Guid? active=null)
 {
  var ids=cueIds.ToHashSet();var rows=Editor.Rows.Where(row=>ids.Contains(row.CueId)).ToArray();
  Grid.SelectedItems.Clear();
  if(rows.Length>0){var first=rows.FirstOrDefault(row=>row.CueId==active)??rows[0];Grid.SelectedItem=first;foreach(var row in rows)if(row!=first)Grid.SelectedItems.Add(row);}
  UpdateCueSelectionControls();
 }
 void RebindCueSelection(IEnumerable<Guid> cueIds,Guid? active=null)
 {
  var ids=cueIds.ToArray();Grid.ItemsSource=null;Grid.ItemsSource=Editor.Rows;RestoreCueSelection(ids,active);
 }
 internal async Task DeleteCuesAsync(IEnumerable<Guid> cueIds)
 {
  if(!DefinitionAuthority)return;
  var ids=cueIds.ToHashSet();var targets=Editor.Rows.Where(row=>ids.Contains(row.CueId)).ToArray();if(targets.Length==0)return;
  var selected=SelectedCueIds();var active=(Grid.SelectedItem as CueRow)?.CueId;int fallback=Editor.Rows.IndexOf(targets[0]);
  if(SyncOwnsSurface){
   if(playback.Observation?.CueId is {} held&&ids.Contains(held)){if(!await playback.ClearHeldSourceAsync(held))return;previewCue=null;}
  }else if(previewCue is {} manual&&ids.Contains(manual)){
   try{await Preview.ClearAsync();}catch(OperationCanceledException)when(!DefinitionAuthority){return;}
   if(!DefinitionAuthority)return;previewCue=null;
  }
  // There are no awaits between this authority check and publishing the final list.
  if(!DefinitionAuthority)return;
  Editor.DeleteMany(ids);
  var remaining=selected.Where(id=>!ids.Contains(id)).ToArray();
  if(remaining.Length==0&&Editor.Rows.Count>0)remaining=[Editor.Rows[Math.Min(fallback,Editor.Rows.Count-1)].CueId];
  RestoreCueSelection(remaining,active);
 }
}
