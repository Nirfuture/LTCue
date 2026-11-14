using LtcPlayer.App;
using System.Diagnostics;
using System.Text.Json;
if(args[0]=="--test"){await IndexChecks.RunAsync(args[1]);return;}
if(args[0]=="--scan"){
 var scan=new List<object>();
 foreach(var path in Directory.GetFiles(args[2],"*.mov").OrderBy(x=>x)){
  var info=await new MediaDecoder().InspectAsync(path,CancellationToken.None);var clock=Stopwatch.StartNew();var index=await MovHapIndex.TryReadAsync(path,info,CancellationToken.None);if(index==null)throw new Exception("Unsupported real material: "+path);
  double indexMs=clock.Elapsed.TotalMilliseconds;foreach(double at in new[]{0,info.Duration*.5,Math.Max(0,info.Duration-1)}){bool read=false;await foreach(var frame in HapMediaDecoder.DecodeAsync(path,info,at,CancellationToken.None,index.Packets)){if(frame.Seconds<at-.000001)throw new Exception("Wrong sample");read=true;break;}if(!read)throw new Exception("No sample");}
  scan.Add(new{File=Path.GetFileName(path),IndexMs=indexMs,index.BytesRead,Packets=index.Packets.Count,FirstMiddleEndFrames=true});
  File.WriteAllText(args[1],JsonSerializer.Serialize(scan,new JsonSerializerOptions{WriteIndented=true}));
 }
 Console.WriteLine($"{scan.Count} real MOVs indexed and sampled");return;
}
var results=new List<object>();
foreach(var path in args.Skip(1)){
 var watch=Stopwatch.StartNew();var info=await new MediaDecoder().InspectAsync(path,CancellationToken.None);double probe=watch.Elapsed.TotalMilliseconds;
 watch.Restart();var fast=await MovHapIndex.TryReadAsync(path,info,CancellationToken.None);double fastMs=watch.Elapsed.TotalMilliseconds;
 if(fast==null)throw new Exception("Fast path unsupported: "+path);
 watch.Restart();var legacy=await HapMediaDecoder.ProbeIndexAsync(path,info,CancellationToken.None);double legacyMs=watch.Elapsed.TotalMilliseconds;
 if(fast.Packets.Count!=legacy.Length)throw new Exception("Count mismatch");
 for(int i=0;i<legacy.Length;i++)if(fast.Packets[i].Position!=legacy[i].Position||fast.Packets[i].Size!=legacy[i].Size||Math.Abs(fast.Packets[i].Seconds-legacy[i].Seconds)>.000001)throw new Exception("Packet mismatch "+i);
 watch.Restart();await foreach(var frame in HapMediaDecoder.DecodeAsync(path,info,0,CancellationToken.None,legacy)){break;}double read=watch.Elapsed.TotalMilliseconds;
 results.Add(new{File=Path.GetFileName(path),ProbeMs=probe,FastIndexMs=fastMs,LegacyIndexMs=legacyMs,FirstReadAndUnpackMs=read,MetadataBytesRead=fast.BytesRead,FileBytes=new FileInfo(path).Length,Packets=legacy.Length,Equal=true});
 File.WriteAllText(args[0],JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(results[^1]));
}
