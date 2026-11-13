using System.Diagnostics;
using System.Text.Json;
using LtcPlayer.App;
namespace LtcPlayer.Avalonia;
public static class ResumeLatencyVerification
{
 static void Check(bool value,string message){if(!value)throw new IOException(message);}
 static async Task<double> First(MediaDecoder decoder,string file,double at){var sw=Stopwatch.StartNew();await foreach(var frame in decoder.DecodeAsync(file,640,360,CancellationToken.None,at)){Check(frame.Seconds>=at-.001,"wrong resumed position");return sw.Elapsed.TotalMilliseconds;}throw new IOException("No frame");}
 public static async Task RunAsync(MainWindow window,string folder){
  Directory.CreateDirectory(folder);var results=new List<object>();bool failed=false;
  async Task Test(string name,Func<Task<object>> body){try{results.Add(new{test=name,status="PASS",detail=await body()});}catch(Exception e){failed=true;results.Add(new{test=name,status="FAIL",detail=e.ToString()});}}
  string file=Path.Combine(AppContext.BaseDirectory,"TestMedia","HAP_3_1080p_2997_10s.mov");
  try{
   await Test("Repeated HAP seek reuses analysis and packet index",async()=>{
    var cold=new List<double>();for(int i=0;i<3;i++)cold.Add(await First(new MediaDecoder(),file,1+i));
    var cached=new MediaDecoder(reuseAnalysis:true);double initial=await First(cached,file,1);var warm=new List<double>();for(int i=0;i<3;i++)warm.Add(await First(cached,file,2+i));
    Check(cached.AnalysisRuns==1&&cached.HapIndexBuilds==1,"Repeated analysis/index build");Check(warm.Average()<cold.Average(),"Warm seek did not improve");
    return new{ColdMilliseconds=cold,InitialMilliseconds=initial,WarmMilliseconds=warm,Speedup=cold.Average()/warm.Average(),cached.AnalysisRuns,cached.HapIndexBuilds};
   });
   await Test("Concurrent same-file preparation shares one analysis",async()=>{
    var decoder=new MediaDecoder(reuseAnalysis:true);await Task.WhenAll(Task.Run(()=>First(decoder,file,1)),Task.Run(()=>First(decoder,file,4)));
    Check(decoder.AnalysisRuns==1&&decoder.HapIndexBuilds==1,"Duplicate concurrent preparation");return new{decoder.AnalysisRuns,decoder.HapIndexBuilds};
   });
   await Test("File version change invalidates cached analysis",async()=>{
    string copy=Path.Combine(folder,"changed.mov");File.Copy(file,copy,true);var decoder=new MediaDecoder(reuseAnalysis:true);await First(decoder,copy,1);
    File.SetLastWriteTimeUtc(copy,File.GetLastWriteTimeUtc(copy).AddSeconds(5));await First(decoder,copy,2);
    Check(decoder.AnalysisRuns==2&&decoder.HapIndexBuilds==2,"Stale analysis reused");return new{decoder.AnalysisRuns,decoder.HapIndexBuilds};
   });
  }finally{await window.Monitor.DisposeAsync();File.WriteAllText(Path.Combine(folder,"results.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));MainWindow.Shutdown(failed?1:0);}
 }
}
