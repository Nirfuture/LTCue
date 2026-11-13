using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
namespace LtcPlayer.App;
public static class HapMediaDecoder
{
    public record Packet(long Position,int Size,double Seconds);
    public static async Task<Packet[]> IndexAsync(string path,MediaInfo info,CancellationToken token)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(60));
        using var p=MediaDecoder.Start("ffprobe",new[]{"-v","error","-select_streams","v:0","-show_packets","-show_entries","packet=pos,size,pts_time","-of","json",path});
        using var registration=timeout.Token.Register(()=>MediaDecoder.Kill(p));var error=p.StandardError.ReadToEndAsync();
        var builder=new StringBuilder();var buffer=new char[8192];
        try{int n;while((n=await p.StandardOutput.ReadAsync(buffer.AsMemory(),timeout.Token))>0){if(builder.Length+n>32*1024*1024)throw new InvalidDataException("HAPパケット索引が上限を超えています。");builder.Append(buffer,0,n);}await p.WaitForExitAsync(timeout.Token);}
        finally{MediaDecoder.Kill(p);}
        string detail=await error;if(p.ExitCode!=0)throw new InvalidDataException("HAP索引取得失敗: "+detail);
        using var doc=JsonDocument.Parse(builder.ToString());var packets=new List<Packet>();long fileSize=new FileInfo(path).Length;double previous=double.NegativeInfinity;
        foreach(var item in doc.RootElement.GetProperty("packets").EnumerateArray())
        {
            long pos=long.Parse(item.GetProperty("pos").GetString()!,CultureInfo.InvariantCulture);int size=int.Parse(item.GetProperty("size").GetString()!,CultureInfo.InvariantCulture);
            double pts=double.Parse(item.GetProperty("pts_time").GetString()!,CultureInfo.InvariantCulture)-info.StartTime;
            if(pos<0||size<=0||size>128*1024*1024||pos>fileSize-size||!double.IsFinite(pts)||pts<previous||packets.Count>=1000000)throw new InvalidDataException("HAPパケット索引が不正です。");
            packets.Add(new(pos,size,pts));previous=pts;
        }
        if(packets.Count==0)throw new InvalidDataException("HAPフレームがありません。");return packets.ToArray();
    }
    public static async IAsyncEnumerable<VideoFrame> DecodeAsync(string path,MediaInfo info,double seek,[EnumeratorCancellation]CancellationToken token,Packet[]? preparedPackets=null)
    {
        var packets=preparedPackets??await IndexAsync(path,info,token);int kind=info.Codec=="hap"?1:2;
        await using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,65536,true);
        foreach(var packet in packets)
        {
            if(packet.Seconds+0.000001<seek)continue;token.ThrowIfCancellationRequested();file.Position=packet.Position;var data=new byte[packet.Size];await file.ReadExactlyAsync(data,token);
            var timer=System.Diagnostics.Stopwatch.StartNew();var blocks=HapCodec.Unpack(data,info.Width,info.Height,kind);timer.Stop();
            yield return new VideoFrame(blocks,packet.Seconds,kind,info.Width,info.Height){DecodeMilliseconds=timer.Elapsed.TotalMilliseconds};
        }
    }
}
