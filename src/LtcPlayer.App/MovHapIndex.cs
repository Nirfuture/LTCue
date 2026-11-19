using System.Buffers.Binary;
using Atom = LtcPlayer.App.MovAtomReader.Atom;
namespace LtcPlayer.App;

// Fast path for self-contained, non-fragmented HAP MOVs with an unshifted timeline.
// Read sample tables, never scan mdat. Other valid layouts use the existing demuxer.
public static class MovHapIndex
{
 const int MaxMetadata=64*1024*1024,MaxSamples=1_000_000;
 sealed class UnsupportedLayout:Exception;

 public sealed record Result(IReadOnlyList<HapMediaDecoder.Packet> Packets,long BytesRead);
 static InvalidDataException Bad()=>new("HAP位置情報の構造不正");
 static readonly MovAtomReader Atoms=new(Bad,()=>new UnsupportedLayout());
 static uint Type(string value)=>MovAtomReader.Type(value);
 static uint U32(ReadOnlyMemory<byte> b,int at)=>Atoms.U32(b,at);
 static ulong U64(ReadOnlyMemory<byte> b,int at)=>Atoms.U64(b,at);
 static ushort U16(ReadOnlyMemory<byte> b,int at)=>Atoms.U16(b,at);
 static List<Atom> Children(ReadOnlyMemory<byte> b)=>Atoms.Children(b);
 static ReadOnlyMemory<byte> Required(List<Atom> a,string t)=>Atoms.Required(a,t);
 static ReadOnlyMemory<byte>? Optional(List<Atom> a,string t)=>Atoms.Optional(a,t);
 static void FullBox(ReadOnlyMemory<byte> data){if(U32(data,0)!=0)throw new UnsupportedLayout();}
 static int Count(ReadOnlyMemory<byte> b,int at,int start,int stride)=>Atoms.Count(b,at,start,stride,MaxSamples);
 public static async Task<Result?> TryReadAsync(string path,MediaInfo info,CancellationToken token)
 {
  if(info.Codec is not ("hap" or "hap_q")||Math.Abs(info.StartTime)>.000001)return null;
  try{return await ReadAsync(path,info,token);}catch(UnsupportedLayout){return null;}catch(OverflowException e){throw new InvalidDataException("HAP位置情報が範囲外",e);}
 }
 static async Task<Result> ReadAsync(string path,MediaInfo info,CancellationToken token)
 {
  await using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,8192,FileOptions.Asynchronous|FileOptions.RandomAccess);
  var (moov,mediaRanges,read)=await Atoms.ScanAsync(file,MaxMetadata,true,token);
  var movie=Children(moov);if(Optional(movie,"cmov")!=null||Optional(movie,"mvex")!=null)throw new UnsupportedLayout();
  foreach(var track in movie.Where(x=>x.Type==Type("trak"))){
   var trackAtoms=Children(track.Data);var mdia=Children(Required(trackAtoms,"mdia"));var handler=Required(mdia,"hdlr");
   if(U32(handler,8)!=Type("vide"))continue;
   var minf=Children(Required(mdia,"minf"));var stbl=Children(Required(minf,"stbl"));
   var stsd=Required(stbl,"stsd");FullBox(stsd);if(U32(stsd,4)!=1)throw new UnsupportedLayout();var descriptions=Children(stsd.Slice(8));if(descriptions.Count!=1)throw Bad();
   uint codec=descriptions[0].Type;if(codec!=(info.Codec=="hap"?Type("Hap1"):Type("HapY")))throw new UnsupportedLayout();
   var description=descriptions[0].Data;if(description.Length<8)throw Bad();if(BinaryPrimitives.ReadUInt16BigEndian(description.Span.Slice(6))!=1)throw new UnsupportedLayout();
   Atoms.SelfContainedReference(Required(Children(Required(minf,"dinf")),"dref"));
   var mdhd=Required(mdia,"mdhd");int version=(int)(U32(mdhd,0)>>24);if(version is not (0 or 1))throw new UnsupportedLayout();uint timescale=U32(mdhd,version==0?12:20);if(timescale==0)throw Bad();
   if(mediaRanges.Count==0)throw Bad();if(Optional(stbl,"stz2")!=null)throw new UnsupportedLayout();
   var sizes=Required(stbl,"stsz");FullBox(sizes);uint constant=U32(sizes,4);uint sampleCount=U32(sizes,8);if(sampleCount==0||sampleCount>MaxSamples)throw Bad();int count=(int)sampleCount;
   if(constant==0&&Count(sizes,8,12,4)!=count)throw Bad();
   var stts=Required(stbl,"stts");FullBox(stts);int timeEntries=Count(stts,4,8,8);var times=new double[count];long ticks=0;int sample=0;
   for(int i=0;i<timeEntries;i++){
    token.ThrowIfCancellationRequested();uint n=U32(stts,8+i*8),delta=U32(stts,12+i*8);if(n==0||n>count-sample||delta==0)throw Bad();
    for(int j=0;j<n;j++){if((sample&4095)==0)token.ThrowIfCancellationRequested();times[sample++]=ticks/(double)timescale;ticks=checked(ticks+delta);}
   }
   if(sample!=count)throw Bad();
   if(Optional(stbl,"ctts") is {} ctts){FullBox(ctts);int n=Count(ctts,4,8,8),total=0;for(int i=0;i<n;i++){uint repetitions=U32(ctts,8+8*i);if(repetitions==0||repetitions>count-total)throw Bad();total+=(int)repetitions;if(U32(ctts,12+8*i)!=0)throw new UnsupportedLayout();}if(total!=count)throw Bad();}
   if(Optional(stbl,"stss") is {} stss){FullBox(stss);int n=Count(stss,4,8,4);if(n!=count)throw new UnsupportedLayout();for(int i=0;i<n;i++)if(U32(stss,8+4*i)!=i+1)throw new UnsupportedLayout();}
   if(Optional(trackAtoms,"edts") is {} edts){
    var elst=Required(Children(edts),"elst");FullBox(elst);if(U32(elst,4)!=1)throw new UnsupportedLayout();if(U32(elst,12)!=0||U32(elst,16)!=65536)throw new UnsupportedLayout();
    var mvhd=Required(movie,"mvhd");int v=(int)(U32(mvhd,0)>>24);if(v is not (0 or 1))throw new UnsupportedLayout();uint movieScale=U32(mvhd,v==0?12:20);if(movieScale==0)throw Bad();
    if(Math.Abs(U32(elst,8)/(double)movieScale-ticks/(double)timescale)>1.0/movieScale+.000001)throw new UnsupportedLayout();
   }
   var stsc=Required(stbl,"stsc");FullBox(stsc);int runs=Count(stsc,4,8,12);if(runs==0)throw Bad();
   var offsets=Optional(stbl,"stco");var wide=Optional(stbl,"co64");if((offsets==null)==(wide==null))throw Bad();var chunks=offsets??wide!.Value;FullBox(chunks);int stride=offsets!=null?4:8;int chunkCount=Count(chunks,4,8,stride);if(chunkCount==0)throw Bad();
   var first=new uint[runs];var perChunk=new uint[runs];
   for(int i=0;i<runs;i++){first[i]=U32(stsc,8+12*i);perChunk[i]=U32(stsc,12+12*i);if(first[i]==0||first[i]>chunkCount||(i==0?first[i]!=1:first[i]<=first[i-1])||perChunk[i]==0||perChunk[i]>count||U32(stsc,16+12*i)!=1)throw Bad();}
   var packets=new HapMediaDecoder.Packet[count];sample=0;int run=0;
   for(int chunk=1;chunk<=chunkCount;chunk++){
    token.ThrowIfCancellationRequested();while(run+1<runs&&first[run+1]<=chunk)run++;
    ulong offset=stride==4?U32(chunks,8+(chunk-1)*4):U64(chunks,8+(chunk-1)*8);if(offset>long.MaxValue)throw Bad();long position=(long)offset;
    for(int j=0;j<perChunk[run];j++){
     if(sample>=count)throw Bad();uint size=constant==0?U32(sizes,12+sample*4):constant;if(size==0||size>128*1024*1024||position>file.Length-size||!mediaRanges.Any(r=>position>=r.Start&&position+size<=r.End))throw Bad();
     packets[sample]=new(position,(int)size,times[sample]);sample++;position+=size;
    }
   }
   if(sample!=count)throw Bad();return new(Array.AsReadOnly(packets),read);
  }
  throw new UnsupportedLayout();
 }
}

