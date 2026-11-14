using LtcPlayer.App;
using LtcPlayer.Avalonia;
using System.Text.Json;

var results=new List<object>();
void Check(bool value,string why){if(!value)throw new Exception(why);}
void Test(string name,Action body){body();results.Add(new{test=name,status="PASS"});Console.WriteLine("PASS "+name);}
ScheduledCue Cue(string path,double start,double duration=10,int revision=0)=>new(Guid.NewGuid(),path,start,duration,25,"h264"){SourceRevision=revision};
var a=Cue("a",10,100);var b=Cue("b",20);var c=Cue("c",30);
Test("failed preload does not interrupt current; missing B is absent",()=>{
 var t=new PlaybackTimeline();t.Update([a,b,c]);Check(t.Select(15,0)==a,"A");t.Fail(b,new IOException("missing"));Check(t.Select(25,0)==a,"A interrupted");Check(t.Next(15)==c,"next was failed B");
});
Test("current failure stays black beyond its duration and never returns to A",()=>{
 var t=new PlaybackTimeline();t.Update([a,b]);Check(t.Select(25,0)==b,"B");t.Fail(b,new IOException("disk"));Check(t.Select(26,0)==null,"failure not black");Check(t.Select(45,0)==null,"old A resurrected");
});
Test("later normal cue starts after failure",()=>{
 var t=new PlaybackTimeline();t.Update([a,b,c]);t.Select(25,0);t.Fail(b,new IOException());Check(t.Select(31,0)==c,"C suppressed");
});
Test("verified replacement recovers at current LTC",()=>{
 var t=new PlaybackTimeline();t.Update([a,b,c]);t.Select(25,0);t.Fail(b,new IOException());var restored=b with{SourceRevision=1};t.Update([a,restored,c]);Check(t.Select(26,0)==restored,"no recovery");Check(t.Select(31,0)==c,"B superseded C");
});
Test("clock jump clears failed execution but not failed material",()=>{
 var t=new PlaybackTimeline();t.Update([a,b,c]);t.Select(25,0);t.Fail(b,new IOException());Check(t.Select(15,1)==a,"jump did not return A");Check(t.Select(25,1)==a,"failed B returned");
});
Test("stale failure cannot damage source replacement",()=>{
 var t=new PlaybackTimeline();var restored=b with{SourceRevision=1};t.Update([restored]);t.Fail(b,new IOException());Check(t.Select(25,0)==restored,"stale fault");
});
Test("unchanged update preserves working opened source whose path is unavailable",()=>{
 var t=new PlaybackTimeline();t.Update([a,b,c]);t.Select(25,0);t.Update([a,b with{CanStart=false},c]);Check(t.Select(26,0)==b,"working B interrupted");Check(t.Select(31,0)==c,"C missing");
});
Test("disabled or removed source is not retained",()=>{
 var t=new PlaybackTimeline();t.Update([a,b]);t.Select(25,0);t.Update([a]);Check(t.Select(25,0)==a,"removed B retained");
});
Test("schedule time edit invalidates old execution interval",()=>{
 var t=new PlaybackTimeline();t.Update([a]);Check(t.Select(15,0)==a,"initial cue");t.Update([a with{Start=40}]);Check(t.Select(15,0)==null,"old interval survived TC edit");
});
var rowA=new CueRow{FilePath="a",Duration=10,FrameRate=25,Codec="h264"};
var rowB=new CueRow{FilePath="b",Duration=10,FrameRate=25,Codec="h264"};
Test("duplicate TC excludes both even when one source is missing",()=>{
 rowB.SourceError="missing";var result=ShowProgramService.Evaluate(new[]{rowA,rowB},TimecodeRate.Modes[1]);Check(result.Timeline.Count==0,"one duplicate survived");Check(result.Rows.All(r=>r.Error.Contains("重複")),"not both errors");Check(rowB.SourceError=="missing","source error erased");
});
Test("disabled duplicate does not compete",()=>{
 rowB.Enabled=false;var result=ShowProgramService.Evaluate(new[]{rowA,rowB},TimecodeRate.Modes[1]);Check(result.Timeline.Count==1,"disabled competes");
});
Test("ID repair preserves valid IDs and repairs empty and duplicate",()=>{
 var first=rowA.CueId;var duplicate=new CueRow{CueId=first};var empty=new CueRow{CueId=Guid.Empty};var rows=new[]{rowA,duplicate,empty};ShowProgramService.RepairIds(rows);Check(rowA.CueId==first,"valid ID replaced");Check(rows.Select(r=>r.CueId).Distinct().Count()==3&&rows.All(r=>r.CueId!=Guid.Empty),"repair failed");
});
Test("empty timeline is a normal value",()=>{
 var t=new PlaybackTimeline();t.Update([]);Check(t.Select(0,0)==null&&t.Next(0)==null,"empty");Check(ShowProgramService.Evaluate(Array.Empty<CueRow>(),TimecodeRate.Modes[1]).Timeline.Count==0,"empty invalid");
});
Test("parallel audio readers and source updates remain consistent",()=>{
 var t=new PlaybackTimeline();t.Update([a,b,c]);Parallel.For(0,10000,i=>{if(i%4==0)t.Update([a,b,c]);else {var selected=t.Select(25,0);Check(selected==b,"inconsistent selection");}});
});
string folder=Path.Combine(Path.GetTempPath(),"ltcue-recovery-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
try{
 string json=Path.Combine(folder,"show.json");
 await ShowProgramService.SaveAsync(json,new[]{rowA,rowB},TimecodeRate.Modes[1]);
 results.Add(new{test="save list with absent disabled source",status="PASS"});
 Test("save captures an independent snapshot without validating or changing row errors",()=>{
  var row=new CueRow{FilePath="missing.mp4",Duration=5,FrameRate=25,Codec="h264",SourceError="missing"};row.Hours="bad";
  var captured=ShowProgramService.Capture(new[]{row},TimecodeRate.Modes[1]);
  Check(row.SourceError=="missing"&&row.Hours=="bad","capture changed source rows");row.FilePath="changed.mp4";
  Check(captured.Cues.Single().FilePath=="missing.mp4","snapshot retained mutable row");
 });
 Test("cancelled save preserves old file and removes temporary file",()=>{
  string before=File.ReadAllText(json);using var cancel=new CancellationTokenSource();cancel.Cancel();bool rejected=false;
  try{ShowProgramService.SaveAsync(json,new[]{rowA,rowB},TimecodeRate.Modes[1],cancel.Token).GetAwaiter().GetResult();}catch(OperationCanceledException){rejected=true;}
  Check(rejected&&File.ReadAllText(json)==before&&!Directory.GetFiles(folder,"*.tmp").Any(),"cancel damaged file");
 });
 Test("failed replacement preserves old file and removes temporary file",()=>{
  string before=File.ReadAllText(json);bool rejected=false;
  using(var locked=new FileStream(json,FileMode.Open,FileAccess.Read,FileShare.Read)){
   try{ShowProgramService.SaveAsync(json,new[]{rowA,rowB},TimecodeRate.Modes[1]).GetAwaiter().GetResult();}catch(Exception e)when(e is IOException or UnauthorizedAccessException){rejected=true;}
  }
  Check(rejected&&File.ReadAllText(json)==before&&!Directory.GetFiles(folder,"*.tmp").Any(),"failed replacement damaged file");
 });
 string media=Path.Combine(folder,"clip.mp4");int revision=0;string error="";bool metadata=false;int recovered=0;var gate=new object();
 await using(var monitor=new MaterialMonitor(
  ()=>{lock(gate)return Task.FromResult(new[]{new MaterialRequest(Guid.Empty,media,revision,metadata,error!="")});},
  values=>{lock(gate){foreach(var result in values){if(result.Info!=null){metadata=true;revision++;error="";recovered++;}else error=result.Error;}}return Task.CompletedTask;},()=>false)){
  async Task Until(Func<bool> condition){for(int i=0;i<100;i++){lock(gate){if(condition())return;}await Task.Delay(100);}throw new TimeoutException();}
  await Until(()=>error!="");await File.WriteAllTextAsync(media,"fake media for stub decoder");await Until(()=>recovered==1&&error=="");
  File.Delete(media);await Until(()=>error!="");await File.WriteAllTextAsync(media,"restored version");await Until(()=>recovered==2&&error=="");
  results.Add(new{test="monitor detects loss and restores without manual action",status="PASS"});
 }
}finally{Directory.Delete(folder,true);}
string output=args.FirstOrDefault()??"results.json";File.WriteAllText(output,JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine($"{results.Count} checks passed");
