using System.Collections.ObjectModel;
using System.ComponentModel;
using LtcPlayer.App;
namespace LtcPlayer.Avalonia;
public sealed class ShowEditor
{
 public ObservableCollection<CueRow> Rows{get;}=[];
 public TimecodeRate Rate{get;set;}=TimecodeRate.Modes[1];
 public bool Locked{get;set;}
 public event Action? Changed;
 public event Action? PlaybackChanged;
 ScheduledCue[]? playbackDefinition;
 TimecodeRate? validatedRate;
 public string ValidationMessage{get;private set;}="";
 public bool CanArm{get;private set;}
 void Editable(){if(Locked)throw new InvalidOperationException("本番ARM中は編集できません。");}
 public void Add(CueRow row){Editable();Rows.Add(row);row.PropertyChanged+=Edited;Renumber();}
 void Edited(object? sender,PropertyChangedEventArgs e){if(e.PropertyName is nameof(CueRow.Hours) or nameof(CueRow.Minutes) or nameof(CueRow.Seconds) or nameof(CueRow.Frames) or nameof(CueRow.Enabled))Validate();}
 public void Validate()
 {
  ScheduledCue[]? next=null;
  try{var valid=ShowProgramService.Validate(Rows,Rate);CanArm=valid.Count>0;ValidationMessage=$"キュー {Rows.Count}件 / 有効 {valid.Count}件";next=valid.OrderBy(c=>c.Start).ThenBy(c=>c.CueId).ToArray();}
  catch(Exception e){CanArm=false;ValidationMessage=e.Message;}
  bool changed=validatedRate!=Rate||(playbackDefinition==null)!=(next==null)||(next!=null&&playbackDefinition!=null&&!next.SequenceEqual(playbackDefinition));
  validatedRate=Rate;playbackDefinition=next;
  if(changed)PlaybackChanged?.Invoke();
  Changed?.Invoke();
 }
 public void Renumber(){for(int i=0;i<Rows.Count;i++)Rows[i].DisplayOrder=i;Validate();}
 public void Delete(CueRow row){Editable();row.PropertyChanged-=Edited;Rows.Remove(row);Renumber();}
 public CueRow Clone(CueRow row){Editable();var clone=row.CloneDisabled();Add(clone);Move(clone,Rows.IndexOf(row)+1);return clone;}
 public void Move(CueRow row,int target){Editable();if(Rows.Contains(row)){Rows.Move(Rows.IndexOf(row),Math.Clamp(target,0,Rows.Count-1));Renumber();}}
 public async Task ImportAsync(IEnumerable<string> paths,string start)
 {
  Editable();foreach(var path in paths){var row=new CueRow{FilePath=Path.GetFullPath(path)};row.SetStart(start);await ShowProgramService.RefreshAsync(row,CancellationToken.None);Editable();Add(row);}
 }
 public async Task LoadAsync(string file)
 {
  Editable();var load=await ShowProgramService.LoadAsync(file,CancellationToken.None);Editable();
  foreach(var r in Rows)r.PropertyChanged-=Edited;Rows.Clear();Rate=load.Rate;
  foreach(var r in load.Rows){Rows.Add(r);r.PropertyChanged+=Edited;}
  Renumber();
 }
 public Task SaveAsync(string file){Editable();return ShowProgramService.SaveAsync(file,Rows,Rate);}
}
