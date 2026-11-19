using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using Atom = LtcPlayer.App.MovAtomReader.Atom;
namespace LtcPlayer.App;

// Conservative demux for unshifted, self-contained MOV packed PCM/48kHz/mono or stereo.
// No resampling, persistent PCM or preparation outside the requested decode window.
internal sealed class MovPcmReader : IAsyncDisposable
{
 const int MaxMetadata=64*1024*1024,MaxEntries=1_000_000,Rate=48000;
 sealed class Unsupported([CallerLineNumber]int line=0):Exception("PCM layout at line "+line);

 readonly record struct Chunk(long First,long Frames,long Offset);
 readonly record struct InputFormat(int Channels,int Bytes,bool Float){public int FrameBytes=>Channels*Bytes;}
 readonly InputFormat format;
 readonly FileStream file;readonly MediaFileVersion version;readonly string path;readonly Chunk[] chunks;readonly long frames;readonly double duration;
 MovPcmReader(FileStream file,MediaFileVersion version,string path,Chunk[] chunks,long frames,double duration,InputFormat format){this.format=format;this.file=file;this.version=version;this.path=path;this.chunks=chunks;this.frames=frames;this.duration=duration;}

 static InvalidDataException Bad()=>new("PCMサンプル表の構造不正");
 static readonly MovAtomReader Atoms=new(Bad,()=>new Unsupported());
 static uint Type(string value)=>MovAtomReader.Type(value);
 static uint U32(ReadOnlyMemory<byte> b,int at)=>Atoms.U32(b,at);
 static ulong U64(ReadOnlyMemory<byte> b,int at)=>Atoms.U64(b,at);
 static ushort U16(ReadOnlyMemory<byte> b,int at)=>Atoms.U16(b,at);
 static List<Atom> Children(ReadOnlyMemory<byte> b)=>Atoms.Children(b);
 static ReadOnlyMemory<byte> Required(List<Atom> a,string t)=>Atoms.Required(a,t);
 static ReadOnlyMemory<byte>? Optional(List<Atom> a,string t)=>Atoms.Optional(a,t);
 static void Full(ReadOnlyMemory<byte> b){if(U32(b,0)!=0)throw new Unsupported();}
 static int Count(ReadOnlyMemory<byte> b,int stride)=>Atoms.Count(b,4,8,stride,MaxEntries);
 static bool Zero(double n)=>double.IsFinite(n)&&Math.Abs(n)<.0000001;
 public static async Task<MovPcmReader?> TryOpenAsync(AudioMediaInfo info,CancellationToken token){
  if(info.Codec is not ("pcm_s16le" or "pcm_s24le" or "pcm_f32le")||!Path.GetExtension(info.Path).Equals(".mov",StringComparison.OrdinalIgnoreCase)||!Zero(info.VideoStart)||info.AudioStart is not {} start||!Zero(start)||!Zero(info.ContainerStart))return null;
  token.ThrowIfCancellationRequested();var version=MediaFileVersion.Capture(info.Path);if(info.FileVersion is {} inspected&&version!=inspected)throw new IOException("音声解析後に素材が変更されました");
  var file=new FileStream(info.Path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete,8192,FileOptions.Asynchronous|FileOptions.RandomAccess);
  try{var (chunks,frames,format)=await ParseAsync(file,info.Duration,token);token.ThrowIfCancellationRequested();if(MediaFileVersion.Capture(info.Path)!=version)throw new IOException("PCM準備中に素材が変更されました");return new(file,version,info.Path,chunks,frames,info.Duration,format);}
  catch(Unsupported e){StartupMeasurementTrace.Mark("audio_direct_unsupported",note:e.Message);await file.DisposeAsync();return null;}catch(OverflowException e){await file.DisposeAsync();throw new InvalidDataException("PCMサンプル表が範囲外",e);}catch{await file.DisposeAsync();throw;}
 }
 static async Task<(Chunk[],long,InputFormat)> ParseAsync(FileStream file,double duration,CancellationToken token){
  var (moov,ranges,_)=await Atoms.ScanAsync(file,MaxMetadata,false,token);
  if(ranges.Count==0)throw Bad();var movie=Children(moov);if(Optional(movie,"cmov")!=null||Optional(movie,"mvex")!=null)throw new Unsupported();
  var audioTracks=new List<List<Atom>>();foreach(var trak in movie.Where(a=>a.Type==Type("trak"))){var t=Children(trak.Data);var mdia=Children(Required(t,"mdia"));if(U32(Required(mdia,"hdlr"),8)==Type("soun"))audioTracks.Add(t);}if(audioTracks.Count!=1)throw new Unsupported();var track=audioTracks[0];var media=Children(Required(track,"mdia"));var minf=Children(Required(media,"minf"));var table=Children(Required(minf,"stbl"));
  var stsd=Required(table,"stsd");Full(stsd);if(U32(stsd,4)!=1)throw new Unsupported();var entries=Children(stsd.Slice(8));if(entries.Count!=1)throw Bad();var format=ParseFormat(entries[0]);int FrameBytes=format.FrameBytes;
  Atoms.SelfContainedReference(Required(Children(Required(minf,"dinf")),"dref"));
  var mdhd=Required(media,"mdhd");int version=(int)(U32(mdhd,0)>>24);if(version is not (0 or 1))throw new Unsupported();if(U32(mdhd,version==0?12:20)!=Rate)throw new Unsupported();
  if(Optional(table,"stz2")!=null||Optional(table,"ctts")!=null||Optional(table,"stss")!=null||Optional(table,"senc")!=null)throw new Unsupported();var sizes=Required(table,"stsz");Full(sizes);if(U32(sizes,4)!=FrameBytes)throw new Unsupported();long count=U32(sizes,8);if(count==0)throw Bad();if(!double.IsFinite(duration)||Math.Abs(duration-count/(double)Rate)>1.0/Rate+.0000001)throw new Unsupported();
  ulong mediaDuration=version==0?U32(mdhd,16):U64(mdhd,24);if(mediaDuration!=(ulong)count)throw new Unsupported();var stts=Required(table,"stts");Full(stts);int timeEntries=Count(stts,8);long timeFrames=0;for(int i=0;i<timeEntries;i++){token.ThrowIfCancellationRequested();uint n=U32(stts,8+i*8);if(n==0)throw Bad();if(U32(stts,12+i*8)!=1)throw new Unsupported();timeFrames=checked(timeFrames+n);}if(timeFrames!=count)throw Bad();
  if(Optional(track,"edts") is {} edts){var elst=Required(Children(edts),"elst");Full(elst);if(U32(elst,4)!=1||U32(elst,12)!=0||U32(elst,16)!=65536)throw new Unsupported();var mvhd=Required(movie,"mvhd");int v=(int)(U32(mvhd,0)>>24);if(v is not (0 or 1))throw new Unsupported();uint scale=U32(mvhd,v==0?12:20);if(scale==0)throw Bad();if(Math.Abs(U32(elst,8)/(double)scale-count/(double)Rate)>1.0/scale+.0000001)throw new Unsupported();}
  var stsc=Required(table,"stsc");Full(stsc);int runs=Count(stsc,12);if(runs==0)throw Bad();var small=Optional(table,"stco");var wide=Optional(table,"co64");if((small==null)==(wide==null))throw Bad();var offsets=small??wide!.Value;Full(offsets);int stride=small!=null?4:8,chunkCount=Count(offsets,stride);if(chunkCount==0)throw Bad();var first=new uint[runs];var perChunk=new uint[runs];for(int i=0;i<runs;i++){first[i]=U32(stsc,8+i*12);perChunk[i]=U32(stsc,12+i*12);if(first[i]==0||first[i]>chunkCount||(i==0?first[i]!=1:first[i]<=first[i-1])||perChunk[i]==0||perChunk[i]>count||U32(stsc,16+i*12)!=1)throw Bad();}
  var chunks=new Chunk[chunkCount];long total=0;int run=0;for(int i=0;i<chunkCount;i++){token.ThrowIfCancellationRequested();while(run+1<runs&&first[run+1]<=i+1)run++;ulong raw=stride==4?U32(offsets,8+i*stride):U64(offsets,8+i*stride);if(raw>long.MaxValue)throw Bad();long at=(long)raw,bytes=checked(perChunk[run]*(long)FrameBytes);if(total+perChunk[run]>count||at>file.Length-bytes||!ranges.Any(r=>at>=r.Start&&at+bytes<=r.End))throw Bad();chunks[i]=new(total,perChunk[run],at);total+=perChunk[run];}if(total!=count)throw Bad();return(chunks,count,format);
 }
 // Legacy v1 sampleSize/bytesPerSample are not the packed storage width.
 static InputFormat ParseFormat(Atom entry){
  var d=entry.Data;if(d.Length<28)throw Bad();
  int version=U16(d,8),channels=U16(d,16);
  if(U16(d,6)!=1||channels is not (1 or 2)||U16(d,18)!=16||U16(d,20)!=0||U16(d,22)!=0||U32(d,24)!=(48000u<<16))throw new Unsupported();
  InputFormat format;int extensionStart;
  if(entry.Type==Type("sowt")&&version==0){format=new(channels,2,false);extensionStart=28;}
  else if((entry.Type==Type("in24")||entry.Type==Type("fl32"))&&version==1){
   if(d.Length<44)throw Bad();int width=entry.Type==Type("in24")?3:4;
   if(U32(d,28)!=1||U32(d,32)!=width||U32(d,36)!=width*channels||U32(d,40)!=2)throw new Unsupported();
   format=new(channels,width,width==4);extensionStart=44;
  }else throw new Unsupported();
  var extensions=Children(d.Slice(extensionStart));
  // Duplicate known extensions are malformed, unknown valid extensions unsupported.
  var channel=Optional(extensions,"chan");var wave=Optional(extensions,"wave");
  if(extensions.Any(a=>a.Type!=Type("chan")&&a.Type!=Type("wave")))throw new Unsupported();
  if(channel is {} c){
   if(c.Length<16)throw Bad();
   if(c.Length!=16||U32(c,0)!=0||U32(c,4)!=(channels==1?0x00640001u:0x00650002u)||U32(c,8)!=0||U32(c,12)!=0)throw new Unsupported();
  }
  if(version==0){if(wave!=null)throw new Unsupported();}
  else{
   if(wave is not {} w)throw new Unsupported();var children=Children(w);
   var frma=Required(children,"frma");var enda=Required(children,"enda");
   if(frma.Length!=4||enda.Length!=2)throw Bad();
   if(U32(frma,0)!=entry.Type||U16(enda,0)!=1||children.Any(a=>a.Type!=Type("frma")&&a.Type!=Type("enda")&&(a.Type!=0||a.Data.Length!=0)))throw new Unsupported();
  }
  return format;
 }
 static float ConvertSample(byte[] data,int at,InputFormat format){
  if(format.Float)return BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(at,4)));
  if(format.Bytes==2)return BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(at,2))/32768f;
  int value=data[at]|data[at+1]<<8|data[at+2]<<16;value=(value<<8)>>8;return value/8388608f;
 }
 void CheckVersion(CancellationToken token){token.ThrowIfCancellationRequested();if(MediaFileVersion.Capture(path)!=version)throw new IOException("PCM読込中に素材が変更されました");}
 public async IAsyncEnumerable<AudioPcmBlock> ReadAsync(Guid cueId,int generation,double seek,[EnumeratorCancellation]CancellationToken token){
  CheckVersion(token);long at=(long)Math.Round(seek*Rate,MidpointRounding.AwayFromZero),output=(long)Math.Round((duration-seek)*Rate,MidpointRounding.AwayFromZero),written=0;int low=0,high=chunks.Length;while(low<high){int mid=low+(high-low)/2;if(chunks[mid].First+chunks[mid].Frames<=at)low=mid+1;else high=mid;}int chunkIndex=low;int FrameBytes=format.FrameBytes;var bytes=new byte[1024*FrameBytes];
  while(written<output){CheckVersion(token);int blockFrames=(int)Math.Min(1024,output-written),readFrames=(int)Math.Min(blockFrames,Math.Max(0,frames-at)),done=0;using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(15));
   while(done<readFrames){if(chunkIndex>=chunks.Length)throw Bad();var chunk=chunks[chunkIndex];int n=(int)Math.Min(readFrames-done,chunk.First+chunk.Frames-at);if(n<=0)throw Bad();file.Position=chunk.Offset+(at-chunk.First)*FrameBytes;await file.ReadExactlyAsync(bytes.AsMemory(done*FrameBytes,n*FrameBytes),timeout.Token);done+=n;at+=n;if(at==chunk.First+chunk.Frames)chunkIndex++;}
   CheckVersion(token);var samples=new float[blockFrames*2];for(int i=0;i<readFrames;i++){
    float left=ConvertSample(bytes,i*FrameBytes,format);
    if(format.Channels==1){left*=0.7071067811865476f;samples[i*2]=left;samples[i*2+1]=left;}
    else{samples[i*2]=left;samples[i*2+1]=ConvertSample(bytes,i*FrameBytes+format.Bytes,format);}
   }
   yield return new(cueId,generation,seek+written/(double)Rate,samples);written+=blockFrames;
  }
 }
 public ValueTask DisposeAsync()=>file.DisposeAsync();
}
