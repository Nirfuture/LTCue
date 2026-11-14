using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using LtcPlayer.App;
static class IndexChecks
{
 static byte[] Join(params byte[][] items)=>items.SelectMany(x=>x).ToArray();
 static byte[] N(params uint[] values){var data=new byte[values.Length*4];for(int i=0;i<values.Length;i++)BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(i*4),values[i]);return data;}
 static byte[] Wide(params ulong[] values){var data=new byte[values.Length*8];for(int i=0;i<values.Length;i++)BinaryPrimitives.WriteUInt64BigEndian(data.AsSpan(i*8),values[i]);return data;}
 static byte[] Box(string type,params byte[][] items){var payload=Join(items);return Join(N((uint)payload.Length+8),Encoding.ASCII.GetBytes(type),payload);}
 static byte[] Make(bool wide=false,bool front=false,bool constant=false,bool composition=false,bool edit=false,bool external=false,bool badTime=false,bool badOffset=false,bool oversize=false)
 {
  uint[] sizes=constant?[3,3,3,3,3]:[1,2,3,4,5];
  byte[] Movie(uint start){
   byte[] visual=new byte[8];visual[7]=1;
   byte[] table=Box("stbl",Box("stsd",N(0,1),Box("Hap1",visual)),
    Box("stts",N(0,2,2,40,badTime?2u:3u,20)),Box("stsc",N(0,2,1,2,1,2,3,1)),
    Box("stsz",constant?N(0,3,5):Join(N(0,0,oversize?uint.MaxValue:5),N(sizes))),
    wide?Box("co64",N(0,2),Wide(start,start+sizes[0]+sizes[1])):Box("stco",N(0,2,badOffset?uint.MaxValue:start,start+sizes[0]+sizes[1])),
    composition?Box("ctts",N(0,1,5,1)):[]);
   return Box("moov",Box("trak",edit?Box("edts",Box("elst",N(0,1,140,1,65536))):[],Box("mdia",Box("mdhd",N(0,0,0,1000,140)),Box("hdlr",N(0,0),Encoding.ASCII.GetBytes("vide")),Box("minf",Box("dinf",Box("dref",N(0,1),Box("url ",N(external?0u:1u)))),table))));
  }
  var media=Box("mdat",new byte[sizes.Sum(v=>(int)v)]);if(!front)return Join(media,Movie(8));var movie=Movie(8);return Join(Movie((uint)movie.Length+8),media);
 }
 public static async Task RunAsync(string output)
 {
  var results=new List<object>();string folder=Path.Combine(Path.GetTempPath(),"ltcue-index-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);var info=new MediaInfo(4,4,"hap",.14,25);bool failed=false;
  void Check(bool ok,string why){if(!ok)throw new Exception(why);}
  async Task Test(string name,Func<Task> action){try{await action();results.Add(new{test=name,status="PASS"});}catch(Exception e){failed=true;results.Add(new{test=name,status="FAIL",detail=e.ToString()});}Console.WriteLine((failed?"CHECK ":"PASS ")+name);}
  async Task<string> FileFor(byte[] data){string path=Path.Combine(folder,Guid.NewGuid()+".mov");await File.WriteAllBytesAsync(path,data);return path;}
  async Task Reject(byte[] data){bool rejected=false;try{await MovHapIndex.TryReadAsync(await FileFor(data),info,CancellationToken.None);}catch(InvalidDataException){rejected=true;}Check(rejected,"invalid MOV accepted");}
  IReadOnlyList<HapMediaDecoder.Packet> Packets(int n=2)=>Array.AsReadOnly(Enumerable.Range(0,n).Select(i=>new HapMediaDecoder.Packet(i,1,i/25.0)).ToArray());
  try{
   foreach(bool wide in new[]{false,true})foreach(bool front in new[]{false,true}){
    await Test($"sample-table positions and variable times: wide={wide},front={front}",async()=>{
     var result=await MovHapIndex.TryReadAsync(await FileFor(Make(wide,front)),info,CancellationToken.None);Check(result!=null&&result.Packets.Count==5,"index absent");var p=result!.Packets;
     Check(p.Select(x=>x.Size).SequenceEqual(new[]{1,2,3,4,5})&&p.Select(x=>x.Seconds).Zip(new[]{0d,.04,.08,.10,.12}).All(x=>Math.Abs(x.First-x.Second)<1e-10),"sizes or times");
     Check(p[1].Position==p[0].Position+1&&p[2].Position==p[1].Position+2&&p[4].Position==p[3].Position+4,"chunk offsets");
    });
   }
   await Test("constant sample sizes",async()=>{var r=await MovHapIndex.TryReadAsync(await FileFor(Make(constant:true)),info,CancellationToken.None);Check(r!.Packets.All(x=>x.Size==3),"constant size");});
   await Test("composition offsets use compatibility path",async()=>Check(await MovHapIndex.TryReadAsync(await FileFor(Make(composition:true)),info,CancellationToken.None)==null,"ctts guessed"));
   await Test("edited timeline uses compatibility path",async()=>Check(await MovHapIndex.TryReadAsync(await FileFor(Make(edit:true)),info,CancellationToken.None)==null,"edit guessed"));
   await Test("external media uses compatibility path",async()=>Check(await MovHapIndex.TryReadAsync(await FileFor(Make(external:true)),info,CancellationToken.None)==null,"external data followed"));
   await Test("fragmented MOV uses compatibility path",async()=>Check(await MovHapIndex.TryReadAsync(await FileFor(Box("moof",N(0))),info,CancellationToken.None)==null,"fragment guessed"));
   await Test("bad atom length rejected",()=>Reject(Join(N(1000),Encoding.ASCII.GetBytes("moov"))));
   await Test("packet outside media rejected",()=>Reject(Make(badOffset:true)));
   await Test("sample timing count mismatch rejected",()=>Reject(Make(badTime:true)));
   await Test("oversized sample count rejected before allocation",()=>Reject(Make(oversize:true)));
   await Test("truncated MOV rejected",()=>Reject(Make()[..^1]));
   await Test("cancelled index read rejected",async()=>{using var cancel=new CancellationTokenSource();cancel.Cancel();bool rejected=false;try{await MovHapIndex.TryReadAsync(await FileFor(Make()),info,cancel.Token);}catch(OperationCanceledException){rejected=true;}Check(rejected,"cancel ignored");});
   await Test("concurrent cache consumers share one build; one cancellation is isolated",async()=>{
    var cache=new HapIndexCache();string path=await FileFor(Make());var started=new TaskCompletionSource();var release=new TaskCompletionSource();using var cancel=new CancellationTokenSource();int calls=0;
    async Task<IReadOnlyList<HapMediaDecoder.Packet>> Build(CancellationToken t){Interlocked.Increment(ref calls);started.SetResult();await release.Task.WaitAsync(t);return Packets();}
    var one=cache.GetAsync(path,info,Build,cancel.Token);await started.Task;var two=cache.GetAsync(path,info,Build,CancellationToken.None);cancel.Cancel();try{await one;}catch(OperationCanceledException){}release.SetResult();await two;
    Check(calls==1&&cache.Count==1,"shared work cancelled or duplicated");
   });
   await Test("all cancelled consumers stop build and allow retry",async()=>{
    var cache=new HapIndexCache();string path=await FileFor(Make());using var cancel=new CancellationTokenSource();var started=new TaskCompletionSource();var ended=new TaskCompletionSource();
    async Task<IReadOnlyList<HapMediaDecoder.Packet>> Build(CancellationToken t){started.SetResult();try{await Task.Delay(Timeout.Infinite,t);}finally{ended.SetResult();}return Packets();}
    var first=cache.GetAsync(path,info,Build,cancel.Token);await started.Task;cancel.Cancel();try{await first;}catch(OperationCanceledException){}await ended.Task.WaitAsync(TimeSpan.FromSeconds(2));
    await cache.GetAsync(path,info,_=>Task.FromResult(Packets()),CancellationToken.None);Check(cache.Count==1&&cache.Builds==2,"cancel poisoned retry");
   });
   await Test("failed build is not cached",async()=>{
    var cache=new HapIndexCache();string path=await FileFor(Make());try{await cache.GetAsync(path,info,_=>Task.FromException<IReadOnlyList<HapMediaDecoder.Packet>>(new IOException("read failure")),CancellationToken.None);}catch(IOException){}
    await cache.GetAsync(path,info,_=>Task.FromResult(Packets()),CancellationToken.None);Check(cache.Builds==2&&cache.Count==1,"failure retained");
   });
   await Test("entry count evicts oldest analysis",async()=>{
    var cache=new HapIndexCache(2);var paths=new[]{await FileFor(Make()),await FileFor(Make()),await FileFor(Make())};foreach(var path in paths)await cache.GetAsync(path,info,_=>Task.FromResult(Packets()),CancellationToken.None);
    Check(cache.Count==2,"unbounded entries");await cache.GetAsync(paths[0],info,_=>Task.FromResult(Packets()),CancellationToken.None);Check(cache.Builds==4,"old analysis not evicted");
   });
   await Test("byte budget excludes oversized completed analysis",async()=>{
    var cache=new HapIndexCache(4,300);string path=await FileFor(Make());await cache.GetAsync(path,info,_=>Task.FromResult(Packets()),CancellationToken.None);Check(cache.RetainedBytes==0&&cache.Count==0,"byte limit exceeded");
   });
   await Test("file version change invalidates cached result",async()=>{
    var cache=new HapIndexCache();string path=await FileFor(Make());await cache.GetAsync(path,info,_=>Task.FromResult(Packets()),CancellationToken.None);File.SetLastWriteTimeUtc(path,File.GetLastWriteTimeUtc(path).AddSeconds(3));
    await cache.GetAsync(path,info,_=>Task.FromResult(Packets(3)),CancellationToken.None);Check(cache.Builds==2&&cache.Count==1,"stale file version");
   });
   await Test("file mutation during build rejects result",async()=>{
    var cache=new HapIndexCache();string path=await FileFor(Make());bool rejected=false;
    try{await cache.GetAsync(path,info,_=>{File.SetLastWriteTimeUtc(path,File.GetLastWriteTimeUtc(path).AddSeconds(2));return Task.FromResult(Packets());},CancellationToken.None);}catch(IOException){rejected=true;}
    Check(rejected&&cache.Count==0,"changed file accepted");
   });
   await Test("explicit invalidation forces fresh analysis",async()=>{
    var cache=new HapIndexCache();string path=await FileFor(Make());await cache.GetAsync(path,info,_=>Task.FromResult(Packets()),CancellationToken.None);cache.Invalidate(path);
    await cache.GetAsync(path,info,_=>Task.FromResult(Packets()),CancellationToken.None);Check(cache.Builds==2,"invalidation ignored");
   });
  }finally{Directory.Delete(folder,true);File.WriteAllText(output,JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));}
  if(failed)throw new Exception("HAP preparation checks failed");Console.WriteLine($"{results.Count} checks passed");
 }
}
