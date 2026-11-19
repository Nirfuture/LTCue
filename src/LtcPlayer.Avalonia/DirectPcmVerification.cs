using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using LtcPlayer.App;
namespace LtcPlayer.Avalonia;
public static class DirectPcmVerification
{
 static void Check(bool value,string message){if(!value)throw new IOException(message);}
 static async Task Reject<T>(Task task)where T:Exception{try{await task;throw new IOException("Expected "+typeof(T).Name);}catch(T){}}
 static async Task<float[]> Decode(AudioMediaInfo info,double seek,bool direct){
  var cue=Guid.NewGuid();await using var s=new AudioDecodeSession(info,cue,17,seek,default,0,direct);await s.Ready.WaitAsync(TimeSpan.FromSeconds(8));if(direct)Check(s.UsingDirectPcm,"expected direct PCM");var samples=new List<float>();double next=seek;double end=MasterClock.Timestamp()+12;
  while(!s.Completed){if(s.TryRead(out var block)){Check(block!.CueId==cue&&block.Generation==17&&Math.Abs(block.Pts-next)<1.0/48000,"stale identity or discontinuity");Check(block.Frames<=1024,"oversized block");samples.AddRange(block.Samples);next+=block.Frames/48000.0;}else{if(MasterClock.Timestamp()>end)throw new TimeoutException();await Task.Delay(1);}Check(s.BufferedBlocks<=8,"unbounded PCM");}
  Check(Math.Abs(samples.Count/2-(info.Duration-seek)*48000)<=2,"duration mismatch");return samples.ToArray();
 }
 static void Equal(float[] a,float[] b){Check(a.All(float.IsFinite)&&b.All(float.IsFinite),"nonfinite value in finite comparison");Check(Math.Abs(a.Length-b.Length)<=4,"reference length differs");Check(a.Take(Math.Min(a.Length,b.Length)).Zip(b,(x,y)=>Math.Abs(x-y)).DefaultIfEmpty().Max()<.000001,"PCM differs from FFmpeg");}
 static List<(string Type,int Payload,int End)> Boxes(byte[] data,int start,int end){var list=new List<(string,int,int)>();for(int at=start;at<end;){int n=checked((int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(at)));Check(n>=8&&n<=end-at,"bad test box");list.Add((System.Text.Encoding.ASCII.GetString(data,at+4,4),at+8,at+n));at+=n;}return list;}
 static int AudioOffsetTable(byte[] data){var moov=Boxes(data,0,data.Length).Single(b=>b.Type=="moov");foreach(var t in Boxes(data,moov.Payload,moov.End).Where(b=>b.Type=="trak")){var mdia=Boxes(data,t.Payload,t.End).Single(b=>b.Type=="mdia");var m=Boxes(data,mdia.Payload,mdia.End);var h=m.Single(b=>b.Type=="hdlr");if(System.Text.Encoding.ASCII.GetString(data,h.Payload+8,4)!="soun")continue;var minf=m.Single(b=>b.Type=="minf");var stbl=Boxes(data,minf.Payload,minf.End).Single(b=>b.Type=="stbl");return Boxes(data,stbl.Payload,stbl.End).Single(b=>b.Type=="stco").Payload+8;}throw new IOException("test audio missing");}
 // Fixture-only structural rewrites: moov is trailing, so media byte positions stay unchanged.
 static byte[] Rewrite(byte[] data,Func<string,byte[],byte[]> change){
  using var stream=new MemoryStream();
  foreach(var box in Boxes(data,0,data.Length)){
   byte[] payload=data[box.Payload..box.End];
   if(box.Type is "moov" or "trak" or "mdia" or "minf" or "stbl")payload=Rewrite(payload,change);
   byte[] result=change(box.Type,payload);stream.Write(result);
  }
  return stream.ToArray();
 }
 static byte[] Box(string type,byte[] payload){var b=new byte[payload.Length+8];BinaryPrimitives.WriteUInt32BigEndian(b,(uint)b.Length);System.Text.Encoding.ASCII.GetBytes(type).CopyTo(b,4);payload.CopyTo(b,8);return b;}
 public static async Task RunAsync(MainWindow w,string folder){
  StartupMeasurementTrace.Begin("direct-suite");Directory.CreateDirectory(folder);var results=new List<object>();string fixtures=Program.Option("--fixtures")!,offsets=Program.Option("--audio-fixtures")!;
  async Task Test(string name,Func<Task> body){try{await body();results.Add(new{test=name,status="PASS"});}catch(Exception e){results.Add(new{test=name,status="FAIL",detail=e.ToString()});}File.WriteAllText(Path.Combine(folder,"results.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));}
  async Task Wait(Func<bool> condition,AudioOutputService? service=null){double end=MasterClock.Timestamp()+6;while(!condition()){if(service?.Snapshot.State==SharedAudioState.Error)throw new IOException(service.Snapshot.Notice);if(MasterClock.Timestamp()>end)throw new TimeoutException(service==null?"PCM wait":JsonSerializer.Serialize(service.Snapshot));await Task.Delay(3);}}
  async Task Generate(string path,int rate,int channels,string codec="pcm_s16le",int seconds=1){await Task.Run(async()=>{using var p=MediaDecoder.Start("ffmpeg",["-hide_banner","-nostdin","-v","error","-y","-f","lavfi","-i","color=c=red:s=32x32:r=25:d="+seconds+"","-f","lavfi","-i","sine=frequency=440:sample_rate="+rate+":duration="+seconds+"","-c:v","hap","-c:a",codec,"-ar",rate.ToString(),"-ac",channels.ToString(),"-shortest",path]);var output=p.StandardOutput.ReadToEndAsync();var error=p.StandardError.ReadToEndAsync();await p.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));await output;Check(p.ExitCode==0,await error);});}
  string small=Path.Combine(folder,"small.mov"),rateFile=Path.Combine(folder,"44100.mov"),mono=Path.Combine(folder,"mono.mov");
  try{
   await Generate(small,48000,2);await Generate(rateFile,44100,2);await Generate(mono,48000,1);
   File.WriteAllText(Path.Combine(folder,"fixture-hashes.json"),JsonSerializer.Serialize(new[]{small,rateFile,mono}.Select(p=>new{Path=p,SHA256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)))})));
   var info=await AudioMediaInfo.InspectAsync(small,default);
   await Test("four default PCM materials match FFmpeg at start middle and tail",async()=>{for(int n=1;n<=4;n++){var i=await AudioMediaInfo.InspectAsync(Path.Combine(AppContext.BaseDirectory,"TestMedia",$"HAP_{n}_1080p_2997_10s.mov"),default);foreach(double at in new[]{0,1.25,i.Duration-.05})Equal(await Decode(i,at,true),await Decode(i,at,false));}});
   await Test("fractional seek and final partial block preserve reference PCM",async()=>{foreach(double at in new[]{.0000752,.12345,info.Duration-.0000752})Equal(await Decode(info,at,true),await Decode(info,at,false));});
   await Test("direct replay and seek launch no audio process",async()=>{long starts=AudioDecodeSession.ProcessStarts;foreach(double at in new[]{0,.25,.75,0})await Decode(info,at,true);Check(AudioDecodeSession.ProcessStarts==starts&&AudioDecodeSession.ActiveProcesses==0,"direct path launched FFmpeg");});
   await Test("44100 Hz uses unchanged FFmpeg resampling",async()=>{var i=await AudioMediaInfo.InspectAsync(rateFile,default);await using var reader=await MovPcmReader.TryOpenAsync(i,default);Check(reader==null,"unsupported rate entered direct path");await using var s=new AudioDecodeSession(i,Guid.NewGuid(),1,0,default);await s.Ready;Check(!s.UsingDirectPcm&&AudioDecodeSession.ActiveProcesses==1,"rate fallback missing");});
   await Test("mono direct conversion matches FFmpeg stereo gain",async()=>{var i=await AudioMediaInfo.InspectAsync(mono,default);Equal(await Decode(i,0,true),await Decode(i,0,false));});
   await Test("late and early PCM retain FFmpeg timestamp handling",async()=>{foreach(string name in new[]{"late-pcm.mov","early-pcm.mov"}){var i=await AudioMediaInfo.InspectAsync(Path.Combine(offsets,name),default);await using var reader=await MovPcmReader.TryOpenAsync(i,default);Check(reader==null,"offset PCM entered restricted path");await using var s=new AudioDecodeSession(i,Guid.NewGuid(),1,0,default);await s.Ready;Check(!s.UsingDirectPcm,"offset fallback missing");}});
   await Test("AAC and nonzero container keep FFmpeg decode",async()=>{foreach(string path in new[]{Path.Combine(fixtures,"h264-aac.mp4"),Path.Combine(offsets,"nonzero-aac.mp4")}){var i=await AudioMediaInfo.InspectAsync(path,default);await using var s=new AudioDecodeSession(i,Guid.NewGuid(),1,0,default);await s.Ready;Check(!s.UsingDirectPcm,"AAC direct path");}});
   await Test("out of mdat audio offsets reject corruption without fallback",async()=>{var bytes=await File.ReadAllBytesAsync(small);BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(AudioOffsetTable(bytes)),uint.MaxValue);string p=Path.Combine(folder,"bad-offset.mov");await File.WriteAllBytesAsync(p,bytes);long before=AudioDecodeSession.ProcessStarts;await using var s=new AudioDecodeSession(info with{Path=p,FileVersion=null},Guid.NewGuid(),1,0,default);await Reject<InvalidDataException>(s.Ready);Check(AudioDecodeSession.ProcessStarts==before,"corrupt PCM fell through");});
   await Test("truncated atoms are rejected",async()=>{string p=Path.Combine(folder,"truncated.mov");await File.WriteAllBytesAsync(p,(await File.ReadAllBytesAsync(small)).Take(20).ToArray());await Reject<InvalidDataException>(MovPcmReader.TryOpenAsync(info with{Path=p,FileVersion=null},default));});
   await Test("changed file versions reject preparation and subsequent reads",async()=>{string p=Path.Combine(folder,"changing.mov");File.Copy(small,p);var i=await AudioMediaInfo.InspectAsync(p,default);File.SetLastWriteTimeUtc(p,File.GetLastWriteTimeUtc(p).AddSeconds(2));await Reject<IOException>(MovPcmReader.TryOpenAsync(i,default));i=await AudioMediaInfo.InspectAsync(p,default);await using var reader=await MovPcmReader.TryOpenAsync(i,default);Check(reader!=null,"direct reader absent");await using var frames=reader!.ReadAsync(Guid.NewGuid(),7,0,default).GetAsyncEnumerator();Check(await frames.MoveNextAsync(),"no PCM");File.SetLastWriteTimeUtc(p,File.GetLastWriteTimeUtc(p).AddSeconds(2));await Reject<IOException>(frames.MoveNextAsync().AsTask());});
   await Test("cancellation during preparation and full queue releases source",async()=>{await using(var cancelled=new AudioDecodeSession(info,Guid.NewGuid(),1,0,default,300)){await cancelled.DisposeAsync();Check(cancelled.Ready.IsCanceled,"preparation survived cancel");}await using(var full=new AudioDecodeSession(info,Guid.NewGuid(),2,0,default)){await full.Ready;await Wait(()=>full.BufferedBlocks==8);await full.DisposeAsync();}Check(AudioDecodeSession.ActiveDecoders==0&&AudioDecodeSession.ActiveProcesses==0,"decoder survived stop");using var exclusive=new FileStream(small,FileMode.Open,FileAccess.ReadWrite,FileShare.None);});
   AudioPlayhead head=AudioPlayhead.Empty;AudioPlayhead Read(){var p=Volatile.Read(ref head);double now=MasterClock.Timestamp();return p with{Position=p.Estimate(now),HostSeconds=now};}
   void Source(int generation,bool advancing=true,bool ltc=false,string? path=null,int revision=0){Volatile.Write(ref head,new(Guid.NewGuid(),Guid.NewGuid(),generation,path??small,0,1,advancing,MasterClock.Timestamp(),ltc,SourceOwnsClock:true){SourceRevision=revision});}
   await Test("cancelled direct preparation never reaches WASAPI",async()=>{Source(1,false);using var c=new CancellationTokenSource();Volatile.Write(ref head,Read() with{PreparationRequested=true,PreparationCancellation=c.Token});await using var s=new AudioOutputService(Read,true);await s.ConfigureAsync(true,"",.3f,false);await Wait(()=>s.Snapshot.BufferedBlocks>0,s);Check(s.Snapshot.HardwarePadding==0&&s.Snapshot.PlayedPts==null,"unpermitted PCM output");c.Cancel();await Wait(()=>s.Snapshot.State==SharedAudioState.Waiting&&AudioDecodeSession.ActiveDecoders==0,s);Check(s.Snapshot.HardwarePadding==0,"cancel retained hardware PCM");});
   await Test("LTC generation jump stop resume and material revision reject old PCM",async()=>{Source(1,true,true);await using var s=new AudioOutputService(Read,true);await s.ConfigureAsync(true,"",.3f,false);await Wait(()=>s.Snapshot.State==SharedAudioState.Playing,s);var sink=s.Snapshot.SinkSession;for(int generation=2;generation<=6;generation++){var p=Read() with{Generation=generation,Position=generation%2==0?.6:.1};Volatile.Write(ref head,p);await Wait(()=>s.Snapshot.State==SharedAudioState.Playing&&s.Snapshot.Generation==generation,s);Check(s.Snapshot.PlayedPts>=p.Position&&s.Snapshot.SinkSession==sink,"stale seek PCM or device replacement");Volatile.Write(ref head,Read() with{Advancing=false});await Wait(()=>s.Snapshot.State==SharedAudioState.Waiting&&AudioDecodeSession.ActiveDecoders==0,s);Volatile.Write(ref head,Read() with{Advancing=true,Position=0});await Wait(()=>s.Snapshot.State==SharedAudioState.Playing,s);}string p2=Path.Combine(folder,"replacement.mov");File.Copy(small,p2);Source(9,true,true,p2,1);await Wait(()=>s.Snapshot.State==SharedAudioState.Playing&&s.Snapshot.Generation==9,s);Volatile.Write(ref head,Read() with{Advancing=false});await Wait(()=>s.Snapshot.State==SharedAudioState.Waiting,s);File.SetLastWriteTimeUtc(p2,File.GetLastWriteTimeUtc(p2).AddSeconds(2));Source(10,true,true,p2,2);await Wait(()=>s.Snapshot.State==SharedAudioState.Playing&&s.Snapshot.Generation==10,s);Check(s.Snapshot.SinkSession==sink&&AudioDecodeSession.ActiveProcesses==0&&s.Snapshot.BufferedBlocks<=8,"replacement resources or stale source");});
   await Test("audio off device failure and explicit recovery release resources",async()=>{Source(1);await using var s=new AudioOutputService(Read,true);await s.ConfigureAsync(true,"",.3f,false);await Wait(()=>s.Snapshot.State==SharedAudioState.Playing,s);var old=s.Snapshot.SinkSession;await s.ConfigureAsync(false,"",.3f,false);Check(AudioDecodeSession.ActiveDecoders==0&&AudioOutputSink.ActiveStreams==0,"off leak");await Reject<IOException>(s.ConfigureAsync(true,"nonexistent-output-for-verification",.3f,false));Check(AudioDecodeSession.ActiveProcesses==0&&AudioOutputSink.ActiveStreams==0,"failed device leaked");await s.ConfigureAsync(false,"",.3f,false);Source(2);await s.ConfigureAsync(true,"",.3f,false);await Wait(()=>s.Snapshot.State==SharedAudioState.Playing,s);Check(s.Snapshot.SinkSession!=old&&s.Snapshot.Generation==2,"device recovery reused old epoch");});
   var comparisons=new List<object>();
   foreach(string codec in new[]{"pcm_s16le","pcm_s24le","pcm_f32le"})foreach(int channels in new[]{1,2}){
    await Test($"{codec} {channels}ch start middle tail fractional seek direct reference",async()=>{
     string path=Path.Combine(folder,$"{codec}-{channels}.mov");await Generate(path,48000,channels,codec,2);var i=await AudioMediaInfo.InspectAsync(path,default);
     long starts=AudioDecodeSession.ProcessStarts;
     foreach(double at in new[]{0,.75125,.0000752,1.12345,i.Duration-.0000752}){
      var a=await Decode(i,at,true);Check(AudioDecodeSession.ProcessStarts==starts,"direct started process");
      var b=await Decode(i,at,false);starts=AudioDecodeSession.ProcessStarts;
      Check(a.All(float.IsFinite)&&b.All(float.IsFinite),"unexpected special value");
      double error=a.Zip(b,(x,y)=>(double)Math.Abs(x-y)).DefaultIfEmpty().Max();
      Check(Math.Abs(a.Length-b.Length)<=4,"sample count differs");Check(error<=(codec=="pcm_s16le"&&channels==2?1e-6:2e-7),"conversion error "+error);
      comparisons.Add(new{codec,channels,seek=at,DirectSamples=a.Length,ReferenceSamples=b.Length,MaxError=error,LeftPeak=a.Where((_,n)=>n%2==0).Select(Math.Abs).DefaultIfEmpty().Max(),RightPeak=a.Where((_,n)=>n%2==1).Select(Math.Abs).DefaultIfEmpty().Max()});
     }
    });
   }
   await Test("22 second PCM uses bounded chunks beyond one million frames",async()=>{
    string path=Path.Combine(folder,"long.mov");await Generate(path,48000,1,"pcm_s24le",22);var i=await AudioMediaInfo.InspectAsync(path,default);
    foreach(double at in new[]{20.8,21.12345,21.999925})Equal(await Decode(i,at,true),await Decode(i,at,false));
   });
   await Test("signed 24 bit boundaries and float specials preserve decode values",async()=>{
    foreach(string codec in new[]{"pcm_s24le","pcm_f32le"})foreach(int channels in new[]{1,2}){
     string raw=Path.Combine(folder,$"edges-{codec}-{channels}.raw"),path=Path.ChangeExtension(raw,"mov");
     using(var writer=new BinaryWriter(File.Create(raw)))for(int n=0;n<48000*channels;n++){
      if(codec=="pcm_s24le"){int[] v=[-8388608,-8388607,-1,0,1,8388606,8388607];int x=v[n%v.Length];writer.Write((byte)x);writer.Write((byte)(x>>8));writer.Write((byte)(x>>16));}
      else{float[] v=[-2,2,-1,1,-0f,0f,float.NaN,float.PositiveInfinity,float.NegativeInfinity,.12345678f];writer.Write(v[n%v.Length]);}
     }
     await Task.Run(async()=>{using var proc=MediaDecoder.Start("ffmpeg",["-hide_banner","-nostdin","-v","error","-y","-f","lavfi","-i","color=s=32x32:r=25:d=1","-f",codec=="pcm_s24le"?"s24le":"f32le","-ar","48000","-ac",channels.ToString(),"-i",raw,"-c:v","hap","-c:a",codec,"-shortest",path]);var error=proc.StandardError.ReadToEndAsync();await proc.WaitForExitAsync();Check(proc.ExitCode==0,await error);});
     var i=await AudioMediaInfo.InspectAsync(path,default);var a=await Decode(i,0,true);var b=await Decode(i,0,false);Check(a.Length==b.Length,"edges length");
     for(int n=0;n<a.Length;n++){
      if(float.IsNaN(b[n]))Check(float.IsNaN(a[n]),"NaN classification");
      else if(float.IsInfinity(b[n]))Check(a[n]==b[n],"Inf sign");
      else Check(float.IsFinite(a[n])&&Math.Abs(a[n]-b[n])<=2e-7,"finite edge conversion");
     }
     if(codec=="pcm_s24le")Check(a.Min()<-.7&&a.Max()>.7,"signed boundary lost");
     else Check(a.Any(float.IsNaN)&&a.Any(float.IsInfinity)&&a.Any(x=>float.IsFinite(x)&&Math.Abs(x)>1),"special coverage absent");
    }
   });
   await Test("co64 and wide or zero sized trailing moov preserve real PCM",async()=>{
    var source=await File.ReadAllBytesAsync(Path.Combine(folder,"pcm_s24le-2.mov"));
    foreach(string variant in new[]{"co64","wide-moov","zero-moov"}){
     var bytes=Rewrite(source,(tag,payload)=>{
      if(variant=="co64"&&tag=="stco"){
       uint count=BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(4));var wide=new byte[8+count*8];payload.AsSpan(0,8).CopyTo(wide);
       for(int n=0;n<count;n++)BinaryPrimitives.WriteUInt64BigEndian(wide.AsSpan(8+n*8),BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(8+n*4)));
       return Box("co64",wide);
      }
      if(tag=="moov"&&variant=="wide-moov"){var wide=new byte[payload.Length+16];BinaryPrimitives.WriteUInt32BigEndian(wide,1);System.Text.Encoding.ASCII.GetBytes(tag).CopyTo(wide,4);BinaryPrimitives.WriteUInt64BigEndian(wide.AsSpan(8),(ulong)wide.Length);payload.CopyTo(wide,16);return wide;}
      var b=Box(tag,payload);if(tag=="moov"&&variant=="zero-moov")BinaryPrimitives.WriteUInt32BigEndian(b,0);return b;
     });
     string path=Path.Combine(folder,variant+".mov");await File.WriteAllBytesAsync(path,bytes);var i=await AudioMediaInfo.InspectAsync(path,default);
     foreach(double at in new[]{0,.02345,1.9})Equal(await Decode(i,at,true),await Decode(i,at,false));
    }
   });
   await Test("normal big endian PCM and signed32 remain FFmpeg fallbacks",async()=>{
    foreach(string codec in new[]{"pcm_s24be","pcm_f32be","pcm_s32le"}){
     string path=Path.Combine(folder,codec+".mov");await Generate(path,48000,2,codec);var i=await AudioMediaInfo.InspectAsync(path,default);
     await using var reader=await MovPcmReader.TryOpenAsync(i,default);Check(reader==null,"unsupported representation guessed");
     await using var session=new AudioDecodeSession(i,Guid.NewGuid(),1,0,default);await session.Ready;Check(!session.UsingDirectPcm,"missing fallback");
    }
   });
   await Test("table count mismatch rejected without process fallback",async()=>{
    var source=await File.ReadAllBytesAsync(small);var bytes=Rewrite(source,(tag,payload)=>{if(tag=="stts")BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(8),0);return Box(tag,payload);});
    string path=Path.Combine(folder,"bad-timing.mov");await File.WriteAllBytesAsync(path,bytes);long starts=AudioDecodeSession.ProcessStarts;
    await Reject<InvalidDataException>(MovPcmReader.TryOpenAsync(info with{Path=path,FileVersion=null},default));Check(AudioDecodeSession.ProcessStarts==starts,"corrupt table fallback");
   });
   await Test("new formats full queue cancellation closes files and releases windows",async()=>{
    foreach(string codec in new[]{"pcm_s16le","pcm_s24le","pcm_f32le"})foreach(int channels in new[]{1,2}){
     string path=Path.Combine(folder,$"{codec}-{channels}.mov");var i=await AudioMediaInfo.InspectAsync(path,default);long starts=AudioDecodeSession.ProcessStarts;
     await using(var session=new AudioDecodeSession(i,Guid.NewGuid(),1,0,default)){await session.Ready;await Wait(()=>session.BufferedBlocks==8);await session.DisposeAsync();}
     Check(AudioDecodeSession.ActiveDecoders==0&&AudioDecodeSession.ProcessStarts==starts,"window/process leak");using var exclusive=new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.None);
    }
   });
   File.WriteAllText(Path.Combine(folder,"format-comparisons.json"),JsonSerializer.Serialize(comparisons,new JsonSerializerOptions{WriteIndented=true}));
   Check(AudioDecodeSession.ActiveDecoders==0&&AudioDecodeSession.ActiveProcesses==0&&AudioOutputSink.ActiveStreams==0,"final resource leak");
  }catch(Exception e){results.Add(new{test="suite setup or final resource assertion",status="FAIL",detail=e.ToString()});}
  StartupMeasurementTrace.End();File.WriteAllText(Path.Combine(folder,"diagnostics.json"),JsonSerializer.Serialize(StartupMeasurementTrace.Snapshot(),new JsonSerializerOptions{WriteIndented=true}));
  File.WriteAllText(Path.Combine(folder,"results.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));await w.Coordinator.DisposeAsync();await w.Preview.DisposeAsync();await w.Monitor.DisposeAsync();MainWindow.Shutdown(results.Any(x=>JsonSerializer.Serialize(x).Contains("FAIL"))?1:0);
 }
}
