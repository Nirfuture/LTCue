using System.Text.Json;
using LtcPlayer.App;
namespace LtcPlayer.Avalonia;
public static class MediaAnalysisVerification
{
 const string Valid="""{"streams":[{"codec_type":"video","codec_name":"hap","codec_tag_string":"Hap1","width":1920,"height":1080,"duration":"10.01","avg_frame_rate":"30000/1001","start_time":"2.5","sample_aspect_ratio":"1:1"},{"codec_type":"audio","codec_name":"pcm_s16le","start_time":"2.7"}],"format":{"duration":"10.01","start_time":"2.25"}}""";
 static TaskCompletionSource Signal()=>new(TaskCreationOptions.RunContinuationsAsynchronously);
 static void Check(bool value,string message){if(!value)throw new IOException(message);}
 static async Task Reject<T>(Task task)where T:Exception{try{await task;throw new IOException("Expected "+typeof(T).Name);}catch(T){}}
 public static async Task RunAsync(MainWindow w,string folder){
  Directory.CreateDirectory(folder);var results=new List<object>();
  string file=Path.Combine(folder,"source.mov");await File.WriteAllTextAsync(file,"source");
  async Task Test(string name,Func<Task> run){try{await run();results.Add(new{test=name,status="PASS"});}catch(Exception e){results.Add(new{test=name,status="FAIL",error=e.ToString()});}}
  MediaAnalysisService Fake(int capacity=8)=>new(capacity,(_,_)=>Task.FromResult(Valid));
  await Test("four versions retained and normalized paths share",async()=>{
   await using var s=Fake();foreach(int n in Enumerable.Range(1,4)){string p=Path.Combine(folder,n+".mov");await File.WriteAllTextAsync(p,"x");await s.GetAsync(p,default);}
   foreach(int n in Enumerable.Range(1,4))await s.GetAsync(Path.Combine(folder,".",n+".mov"),default);
   Check(s.Count==4&&s.ProbeRuns==4&&s.Hits==4,"retention or identity");
  });
  await Test("concurrent requests coalesce",async()=>{
   var began=Signal();var release=Signal();await using var s=new MediaAnalysisService(probe:async(_,t)=>{began.SetResult();await release.Task.WaitAsync(t);return Valid;});
   var a=s.GetAsync(file,default);await began.Task;var b=s.GetAsync(file,default);release.SetResult();Check(ReferenceEquals(await a,await b)&&s.ProbeRuns==1,"duplicate probes");
  });
  await Test("one caller cancellation preserves other caller",async()=>{
   var began=Signal();var release=Signal();await using var s=new MediaAnalysisService(probe:async(_,t)=>{began.SetResult();await release.Task.WaitAsync(t);return Valid;});using var c=new CancellationTokenSource();
   var a=s.GetAsync(file,c.Token);await began.Task;var b=s.GetAsync(file,default);c.Cancel();await Reject<OperationCanceledException>(a);release.SetResult();await b;Check(s.Count==1&&s.ProbeRuns==1,"other caller cancelled");
  });
  await Test("all callers cancellation cancels worker and permits retry",async()=>{
   var began=Signal();var cancelled=Signal();int calls=0;await using var s=new MediaAnalysisService(probe:async(_,t)=>{if(Interlocked.Increment(ref calls)==1){began.SetResult();try{await Task.Delay(Timeout.Infinite,t);}finally{cancelled.SetResult();}}return Valid;});
   using var c=new CancellationTokenSource();using var d=new CancellationTokenSource();var a=s.GetAsync(file,c.Token);await began.Task;var b=s.GetAsync(file,d.Token);c.Cancel();d.Cancel();await Reject<OperationCanceledException>(a);await Reject<OperationCanceledException>(b);await cancelled.Task;await s.GetAsync(file,default);Check(s.ProbeRuns==2&&s.Count==1,"cancel cached");
  });
  await Test("failure is not retained",async()=>{int n=0;await using var s=new MediaAnalysisService(probe:(_,_)=>++n==1?Task.FromException<string>(new IOException("injected")):Task.FromResult(Valid));await Reject<IOException>(s.GetAsync(file,default));await s.GetAsync(file,default);Check(n==2,"failure cached");});
  await Test("incomplete JSON is not retained",async()=>{int n=0;await using var s=new MediaAnalysisService(probe:(_,_)=>Task.FromResult(++n==1?"{":Valid));await Reject<JsonException>(s.GetAsync(file,default));await s.GetAsync(file,default);Check(n==2,"partial cached");});
  await Test("file modification during probe rejects result",async()=>{
   var began=Signal();var release=Signal();await using var s=new MediaAnalysisService(probe:async(_,t)=>{began.TrySetResult();await release.Task.WaitAsync(t);return Valid;});var a=s.GetAsync(file,default);await began.Task;File.SetLastWriteTimeUtc(file,File.GetLastWriteTimeUtc(file).AddSeconds(2));release.SetResult();await Reject<IOException>(a);Check(s.Count==0,"changed result cached");await s.GetAsync(file,default);Check(s.ProbeRuns==2,"no retry");
  });
  await Test("replacement changes version and metadata",async()=>{await using var s=Fake();var a=await s.GetAsync(file,default);await File.AppendAllTextAsync(file,"replacement");var b=await s.GetAsync(file,default);Check(a.Version!=b.Version&&s.Count==1&&s.ProbeRuns==2,"stale version");});
  await Test("explicit invalidation clears metadata and HAP index",async()=>{
   await using var s=Fake();var a=await s.GetAsync(file,default);await s.IndexCache.GetAsync(file,a.Video(),_=>Task.FromResult<IReadOnlyList<HapMediaDecoder.Packet>>([]),default);s.Invalidate(file);Check(s.Count==0&&s.IndexCache.Count==0,"incomplete invalidation");await s.GetAsync(file,default);Check(s.ProbeRuns==2,"not reparsed");
  });
  await Test("invalidation rejects in-flight old result",async()=>{
   var began=Signal();var release=Signal();await using var s=new MediaAnalysisService(probe:async(_,_)=>{began.SetResult();await release.Task;return Valid;});var a=s.GetAsync(file,default);await began.Task;s.Invalidate(file);release.SetResult();await Reject<OperationCanceledException>(a);Check(s.Count==0,"invalidated result adopted");
  });
  await Test("LRU bound evicts least recent completed version",async()=>{await using var s=Fake(4);for(int n=1;n<=5;n++){string p=Path.Combine(folder,n+".mov");await File.WriteAllTextAsync(p,"x");await s.GetAsync(p,default);}Check(s.Count==4,"capacity exceeded");await s.GetAsync(Path.Combine(folder,"1.mov"),default);Check(s.ProbeRuns==6,"oldest not evicted");});
  await Test("shutdown cancels and waits for active analysis",async()=>{
   var began=Signal();var ended=Signal();var s=new MediaAnalysisService(probe:async(_,t)=>{began.SetResult();try{await Task.Delay(Timeout.Infinite,t);return Valid;}finally{ended.SetResult();}});var a=s.GetAsync(file,default);await began.Task;await s.DisposeAsync();await Reject<OperationCanceledException>(a);Check(ended.Task.IsCompleted&&s.Count==0,"worker survived");await Reject<ObjectDisposedException>(s.GetAsync(file,default));
  });
  await Test("creation timestamp participates in identity",async()=>{await using var s=Fake();var a=await s.GetAsync(file,default);var created=File.GetCreationTimeUtc(file);File.SetCreationTimeUtc(file,created.AddSeconds(10));try{var b=await s.GetAsync(file,default);Check(a.Version!=b.Version&&s.ProbeRuns==2,"creation ignored");}finally{File.SetCreationTimeUtc(file,created);}});
  await Test("video validation retains tag SAR size and rate rejection",()=>{
   MediaAnalysis A(string json)=>new(MediaFileVersion.Capture(file),file,json);
   Check(A(Valid.Replace("Hap1","HapY")).Video().Codec=="hap_q","HAP Q tag");
   foreach(var bad in new[]{Valid.Replace("Hap1","Hap5"),Valid.Replace("1:1","2:1"),Valid.Replace("1920","1919"),Valid.Replace("1080","9000"),Valid.Replace("30000/1001","0/0"),Valid.Replace("\"duration\":\"10.01\",\"avg_frame_rate\"","\"duration\":\"NaN\",\"avg_frame_rate\""),Valid.Replace("\"duration\":\"10.01\",\"avg_frame_rate\"","\"duration\":\"Infinity\",\"avg_frame_rate\"")}){bool rejected=false;try{A(bad).Video();}catch(InvalidDataException){rejected=true;}Check(rejected,"invalid video accepted");}return Task.CompletedTask;
  });
  await Test("video audio and container timestamps preserve distinct semantics",()=>{
   var a=new MediaAnalysis(MediaFileVersion.Capture(file),file,Valid);var v=a.Video();var sound=a.Audio();Check(v.StartTime==2.5&&sound.VideoStart==2.5&&sound.AudioStart==2.7&&sound.ContainerStart==2.25,"timestamps conflated");
   var fallback=new MediaAnalysis(a.Version,file,Valid.Replace(",\"start_time\":\"2.5\"",""));Check(fallback.Video().StartTime==0&&fallback.Audio().VideoStart==2.25,"fallback changed");return Task.CompletedTask;
  });
  await Test("real registration preview and shared audio use one probe",async()=>{
   await using var s=new MediaAnalysisService();string p=Path.Combine(AppContext.BaseDirectory,"TestMedia","HAP_1_1080p_2997_10s.mov");var row=new CueRow{FilePath=p};await ShowProgramService.RefreshAsync(row,default,s);var video=await new MediaDecoder(analysis:s).InspectAsync(p,default);var preview=await PreviewAudio.InspectAsync(p,default,s);var audio=await AudioMediaInfo.InspectAsync(p,default,s);Check(s.ProbeRuns==1&&video.Codec==row.Codec&&audio.Codec==preview.Codec&&audio.Supported==preview.Supported,"adapter mismatch or duplicate probe");
  });
  await Test("list reload reparses once per path and rejects saved metadata",async()=>{
   await using var s=Fake();var a=new CueRow{FilePath=file};await ShowProgramService.RefreshAsync(a,default,s);var b=a.CloneDisabled();string path=Path.Combine(folder,"show.json");await ShowProgramService.SaveAsync(path,new[]{a,b},TimecodeRate.Modes[1]);var load=await ShowProgramService.LoadAsync(path,default,s);Check(s.ProbeRuns==2&&load.Rows.Count==2&&load.Rows.All(r=>r.Duration==10.01),"list reload did not revalidate");
  });
  await Test("probe including synchronous launch executes off UI thread",async()=>{
   bool onUi=true;await using var s=new MediaAnalysisService(probe:(_,_)=>{onUi=global::Avalonia.Threading.Dispatcher.UIThread.CheckAccess();return Task.FromResult(Valid);});await s.GetAsync(file,default);Check(!onUi,"probe blocked UI");
  });
  await Test("unfinished requests have a global bound including cancelled workers",async()=>{
   var began=Signal();var release=Signal();await using var s=new MediaAnalysisService(probe:async(_,_)=>{began.TrySetResult();await release.Task;return Valid;});var pending=new List<Task>();
   try{for(int n=0;n<8;n++){string p=Path.Combine(folder,"pending-"+n+".mov");await File.WriteAllTextAsync(p,"x");pending.Add(s.GetAsync(p,default));}await began.Task;await Reject<IOException>(s.GetAsync(file,default));}finally{release.TrySetResult();await Task.WhenAll(pending);}
   Check(s.Count==8,"pending bound corrupted results");
  });
  await Test("oversized metadata is rejected without retention",async()=>{await using var s=new MediaAnalysisService(probe:(_,_)=>Task.FromResult(new string(' ',1024*1024+1)));await Reject<InvalidDataException>(s.GetAsync(file,default));Check(s.Count==0,"oversized result retained");});
  await Test("HAP index rejects metadata from an older file version",async()=>{
   await using var s=Fake();var a=await s.GetAsync(file,default);await File.AppendAllTextAsync(file,"new version");bool built=false;await Reject<IOException>(s.IndexCache.GetAsync(file,a.Video(),_=>{built=true;return Task.FromResult<IReadOnlyList<HapMediaDecoder.Packet>>([]);},default));Check(!built,"old metadata reached index builder");
  });
  await Test("invalidated in-flight HAP index cannot be adopted",async()=>{
   await using var s=Fake();var a=await s.GetAsync(file,default);var began=Signal();var release=Signal();var index=s.IndexCache.GetAsync(file,a.Video(),async _=>{began.SetResult();await release.Task;return Array.Empty<HapMediaDecoder.Packet>();},default);await began.Task;s.Invalidate(file);release.SetResult();await Reject<OperationCanceledException>(index);Check(s.IndexCache.Count==0,"invalidated index retained");
  });
  File.WriteAllText(Path.Combine(folder,"results.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
  await w.Coordinator.DisposeAsync();await w.Preview.DisposeAsync();await w.Monitor.DisposeAsync();MainWindow.Shutdown(results.Any(r=>JsonSerializer.Serialize(r).Contains("FAIL"))?1:0);
 }
}
