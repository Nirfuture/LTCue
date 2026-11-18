using System.Collections.ObjectModel;
using System.ComponentModel;
using LtcPlayer.App;
namespace LtcPlayer.Avalonia;
public sealed class ShowEditor
{
 public ObservableCollection<CueRow> Rows{get;}=[];
 public TimecodeRate Rate{get;set;}=TimecodeRate.Modes[1];
 public bool Locked=>DefinitionAuthority?.Invoke()==false;
 public Func<bool>? DefinitionAuthority{get;set;}
 public Func<bool>? ReorderAuthority{get;set;}
 bool changingTimecode;
 public event Action? Changed;
 public event Action? PlaybackChanged;
 ScheduledCue[]? playbackDefinition;
 TimecodeRate? validatedRate;
 public string ValidationMessage{get;private set;}="";
 public bool CanArm{get;private set;}
 public ScheduledCue[] Timeline{get;private set;}=[];
 public string Notices{get;private set;}="";
 void Editable(){if(Locked)throw new InvalidOperationException("現在は編集できません");}
 public void Add(CueRow row){Editable();ShowProgramService.RepairIds(Rows.Append(row));Rows.Add(row);row.PropertyChanged+=Edited;Renumber();}
 void Edited(object? sender,PropertyChangedEventArgs e){if(!changingTimecode&&e.PropertyName is nameof(CueRow.Hours) or nameof(CueRow.Minutes) or nameof(CueRow.Seconds) or nameof(CueRow.Frames) or nameof(CueRow.Enabled) or nameof(CueRow.FilePath))Validate();}
 public void Validate()
 {
  ScheduledCue[]? next=null;
  try{
   var result=ShowProgramService.Evaluate(Rows,Rate);
   foreach(var item in result.Rows){item.Row.Error=item.Error;item.Row.Warning=item.Warning;item.Row.OverlapDetails=item.OverlapDetails;}
   Timeline=result.Timeline.ToArray();
   CanArm=true;ValidationMessage="";
   next=Timeline.OrderBy(c=>c.Start).ThenBy(c=>c.CueId).ToArray();
  }catch(Exception e){CanArm=false;Timeline=[];ValidationMessage=e.Message;}
  bool changed=validatedRate!=Rate||(playbackDefinition==null)!=(next==null)||(next!=null&&playbackDefinition!=null&&!next.SequenceEqual(playbackDefinition));
  validatedRate=Rate;playbackDefinition=next;
  if(changed)PlaybackChanged?.Invoke();
  Changed?.Invoke();
 }
 public void Renumber(){for(int i=0;i<Rows.Count;i++)Rows[i].DisplayOrder=i;Validate();}
 public void Delete(CueRow row)=>DeleteMany([row.CueId]);
 public int DeleteMany(IEnumerable<Guid> cueIds)
 {
  Editable();var ids=cueIds.ToHashSet();var removed=Rows.Where(row=>ids.Contains(row.CueId)).ToArray();
  if(removed.Length==0)return 0;
  // Mutate the collection synchronously; publish the final playback definition once.
  foreach(var row in removed){row.PropertyChanged-=Edited;Rows.Remove(row);}
  Renumber();return removed.Length;
 }
 public CueRow Clone(CueRow row){Editable();var clone=row.CloneDisabled();Add(clone);Move(clone,Rows.IndexOf(row)+1);return clone;}
 public void Move(CueRow row,int target){if(!(ReorderAuthority?.Invoke()??!Locked))throw new InvalidOperationException("現在は並べ替えできません");if(Rows.Contains(row)){Rows.Move(Rows.IndexOf(row),Math.Clamp(target,0,Rows.Count-1));Renumber();}}
 public async Task ImportAsync(IEnumerable<string> paths,string start,CuePlacementPreferences? placement=null)
 {
  Editable();var prepared=new List<CueRow>();foreach(var path in paths){var row=new CueRow{FilePath=Path.GetFullPath(path)};row.SetStart(start);try{await ShowProgramService.RefreshAsync(row,CancellationToken.None);}catch(Exception e){row.SourceError=e.Message;}prepared.Add(row);}
#if VERIFICATION
  if(BeforeImportApplyForTest!=null)await BeforeImportApplyForTest();
#endif
  Editable();
  if(Rows.Count+prepared.Count>1000)throw new InvalidDataException("キューは1000件まで");
  if(placement is {AfterExisting:true}&&prepared.Count>0){
   placement.Validate();
   string first=CueTimecodes.AppendStart(Rows.Select(CueTimecodeSource.Capture),placement.GapSeconds,Rate);
   var plan=CueTimecodes.Arrange(prepared.Select(CueTimecodeSource.Capture).ToArray(),first,placement.GapSeconds,Rate);
   foreach(var change in plan.Changes)prepared.Single(row=>row.CueId==change.Source.CueId).SetStart(change.Start);
  }
  ShowProgramService.RepairIds(Rows.Concat(prepared));
  foreach(var row in prepared){Rows.Add(row);row.PropertyChanged+=Edited;}Renumber();
 }
 public async Task LoadAsync(string file)
 {
  Editable();var load=await ShowProgramService.LoadAsync(file,CancellationToken.None);Editable();
  Replace(load);
 }
 public void Replace((TimecodeRate Rate,List<CueRow> Rows,List<string> Notices) load)
 {
  Editable();foreach(var r in Rows)r.PropertyChanged-=Edited;Rows.Clear();Rate=load.Rate;
  foreach(var r in load.Rows){Rows.Add(r);r.PropertyChanged+=Edited;}
  Notices=string.Join(" / ",load.Notices);Renumber();
 }
 public void SetEnabled(CueRow row,bool value){Editable();if(Rows.Contains(row))row.Enabled=value;}
 public void SetTimecode(CueRow row,string[] parts)
 {
  Editable();if(!Rows.Contains(row)||parts.Length!=4)return;
  // Single-row typing may intentionally leave an invalid TC for the status column.
  ApplyTimecodeValues([(row,string.Join(":",parts))]);
 }
 public void SetTimecodes(CueTimecodePlan plan)
 {
  Editable();
  if(Rate!=plan.Rate)throw new InvalidOperationException("LTC形式が変わりました。開き直してください");
  var targets=new List<(CueRow Row,string Start)>();
  foreach(var change in plan.Changes){
   var row=Rows.SingleOrDefault(r=>r.CueId==change.Source.CueId);
   if(row==null||row.FilePath!=change.Source.FilePath||row.SourceRevision!=change.Source.SourceRevision||row.StartTimecode!=change.Source.Start||
      (plan.UsesDuration&&row.Duration!=change.Source.Duration))throw new InvalidOperationException("対象キューが変わりました。開き直してください");
   // Validate every requested value before touching any live row.
   plan.Rate.ParseFrames(change.Start);targets.Add((row,change.Start));
  }
  if(targets.Select(t=>t.Row.CueId).Distinct().Count()!=targets.Count)throw new InvalidDataException("対象キューが重複しています");
  ApplyTimecodeValues(targets);
 }
 void ApplyTimecodeValues(IEnumerable<(CueRow Row,string Start)> values)
 {
  changingTimecode=true;
  try{foreach(var value in values)value.Row.SetStart(value.Start);}
  finally{changingTimecode=false;}
  Validate();
 }
 public Task SaveAsync(string file,CancellationToken token=default)
 {
  var document=ShowProgramService.Capture(Rows,Rate);
  return SaveSnapshotAsync(file,document,token);
 }
#if VERIFICATION
 internal Func<CancellationToken,Task>? BeforeSaveWriteForTest{get;set;}
 internal Func<Task>? BeforeImportApplyForTest{get;set;}
#endif
 async Task SaveSnapshotAsync(string file,ShowProgramDocument document,CancellationToken token)
 {
#if VERIFICATION
  if(BeforeSaveWriteForTest!=null)await BeforeSaveWriteForTest(token);
#endif
  await ShowProgramService.SaveDocumentAsync(file,document,token);
 }
}
