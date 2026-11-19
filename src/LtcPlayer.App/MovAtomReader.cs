using System.Buffers.Binary;
using System.Text;
namespace LtcPlayer.App;

// Stateless parsing only. Callers retain their stream, limits and error policy.
internal sealed class MovAtomReader(Func<Exception> bad,Func<Exception> unsupported)
{
 internal readonly record struct Atom(uint Type,ReadOnlyMemory<byte> Data);
 internal static uint Type(string value)=>BinaryPrimitives.ReadUInt32BigEndian(Encoding.ASCII.GetBytes(value));
 internal uint U32(ReadOnlyMemory<byte> data,int at){if(at<0||at>data.Length-4)throw bad();return BinaryPrimitives.ReadUInt32BigEndian(data.Span.Slice(at,4));}
 internal ulong U64(ReadOnlyMemory<byte> data,int at){if(at<0||at>data.Length-8)throw bad();return BinaryPrimitives.ReadUInt64BigEndian(data.Span.Slice(at,8));}
 internal ushort U16(ReadOnlyMemory<byte> data,int at){if(at<0||at>data.Length-2)throw bad();return BinaryPrimitives.ReadUInt16BigEndian(data.Span.Slice(at,2));}
 internal List<Atom> Children(ReadOnlyMemory<byte> data){
  var result=new List<Atom>();int at=0;
  while(at<data.Length){
   if(result.Count>=100000||data.Length-at<8)throw bad();
   ulong size=U32(data,at);uint type=U32(data,at+4);int header=8;
   if(size==1){size=U64(data,at+8);header=16;}else if(size==0)size=(ulong)(data.Length-at);
   if(size<(ulong)header||size>(ulong)(data.Length-at))throw bad();
   result.Add(new(type,data.Slice(at+header,(int)size-header)));at+=(int)size;
  }
  return result;
 }
 internal ReadOnlyMemory<byte> Required(List<Atom> atoms,string type){var found=atoms.Where(a=>a.Type==Type(type)).ToArray();if(found.Length!=1)throw bad();return found[0].Data;}
 internal ReadOnlyMemory<byte>? Optional(List<Atom> atoms,string type){var found=atoms.Where(a=>a.Type==Type(type)).ToArray();if(found.Length>1)throw bad();if(found.Length==0)return null;return found[0].Data;}
 internal int Count(ReadOnlyMemory<byte> data,int at,int start,int stride,int maximum){uint n=U32(data,at);if(n>maximum||start>data.Length||n>(data.Length-start)/stride)throw bad();return (int)n;}
 internal void SelfContainedReference(ReadOnlyMemory<byte> dref){
  if(U32(dref,0)!=0||U32(dref,4)!=1)throw unsupported();
  var refs=Children(dref.Slice(8));if(refs.Count!=1)throw bad();
  if(refs[0].Type!=Type("url ")||U32(refs[0].Data,0)!=1)throw unsupported();
 }
 internal async Task<(byte[] Moov,List<(long Start,long End)> Ranges,long BytesRead)> ScanAsync(FileStream file,int maximumMetadata,bool duplicateIsUnsupported,CancellationToken token){
  var header=new byte[16];byte[]? moov=null;var ranges=new List<(long Start,long End)>();long read=0;int boxes=0;
  while(file.Position<file.Length){
   token.ThrowIfCancellationRequested();if(++boxes>100000||file.Length-file.Position<8)throw bad();long start=file.Position;
   await file.ReadExactlyAsync(header.AsMemory(0,8),token);read+=8;ulong size=U32(header,0);uint type=U32(header,4);int length=8;
   if(size==1){if(file.Length-file.Position<8)throw bad();await file.ReadExactlyAsync(header.AsMemory(8,8),token);read+=8;size=U64(header,8);length=16;}else if(size==0)size=(ulong)(file.Length-start);
   if(size<(ulong)length||size>(ulong)(file.Length-start))throw bad();
   if(type==Type("moof"))throw unsupported();
   if(type==Type("moov")){
    if(moov!=null)throw duplicateIsUnsupported?unsupported():bad();
    if(size-(ulong)length>(ulong)maximumMetadata)throw unsupported();
    moov=new byte[(int)size-length];await file.ReadExactlyAsync(moov,token);read+=moov.Length;
   }else if(type==Type("mdat"))ranges.Add((start+length,start+(long)size));
   file.Position=start+(long)size;
  }
  if(moov==null)throw bad();return(moov,ranges,read);
 }
}
