using System.Buffers.Binary;
namespace LtcPlayer.App;
public static class HapCodec
{
    private static (int Header,int Size,int Type) Section(ReadOnlySpan<byte> data)
    {
        if(data.Length<4)throw new InvalidDataException("HAPヘッダー不足");
        int size=data[0]|data[1]<<8|data[2]<<16,header=4;
        if(size==0){if(data.Length<8)throw new InvalidDataException("HAP拡張ヘッダー不足");size=checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(4,4)));header=8;}
        if(size<0||size>data.Length-header)throw new InvalidDataException("HAPセクション範囲外");return(header,size,data[3]);
    }
    public static byte[] Unpack(ReadOnlySpan<byte> packet,int width,int height,int kind)
    {
        int expected=checked((width/4)*(height/4)*(kind==1?8:16));
        if(width<=0||height<=0||width>8192||height>8192||width%4!=0||height%4!=0||kind is not (1 or 2))throw new InvalidDataException("HAP寸法・形式が不正です。");
        var top=Section(packet);if(top.Header+top.Size!=packet.Length || (top.Type&15)!=(kind==1?11:15))throw new InvalidDataException("HAP形式またはフレーム長が不一致です。");
        var data=packet.Slice(top.Header,top.Size);int compressor=top.Type>>4;
        if(compressor==10){if(data.Length!=expected)throw new InvalidDataException("HAP BCサイズ不一致");return data.ToArray();}
        if(compressor==11){var raw=Snappy(data,expected);if(raw.Length!=expected)throw new InvalidDataException("HAP展開サイズ不一致");return raw;}
        if(compressor!=12)throw new InvalidDataException("未対応HAP圧縮です。");
        var instruction=Section(data);if(instruction.Type!=1)throw new InvalidDataException("HAP命令コンテナがありません。");
        var tables=data.Slice(instruction.Header,instruction.Size);byte[]? compressors=null;int[]? sizes=null,offsets=null;
        while(tables.Length>0)
        {
            var part=Section(tables);var bytes=tables.Slice(part.Header,part.Size);
            if(part.Type==2){if(compressors!=null)throw new InvalidDataException("HAPテーブル重複");compressors=bytes.ToArray();}
            if(part.Type is 3 or 4)
            {
                if(bytes.Length%4!=0 || bytes.Length/4>4096)throw new InvalidDataException("HAPチャンクテーブル不正");
                var values=new int[bytes.Length/4];for(int i=0;i<values.Length;++i)values[i]=checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(i*4,4)));
                if(part.Type==3){if(sizes!=null)throw new InvalidDataException("HAPテーブル重複");sizes=values;}else{if(offsets!=null)throw new InvalidDataException("HAPテーブル重複");offsets=values;}
            }
            tables=tables.Slice(part.Header+part.Size);
        }
        if(compressors==null||sizes==null||sizes.Length==0||sizes.Length>4096||compressors.Length!=sizes.Length||(offsets!=null && offsets.Length!=sizes.Length))throw new InvalidDataException("HAPチャンク数不一致");
        var payload=data.Slice(instruction.Header+instruction.Size);var output=new byte[expected];int at=0,source=0;
        for(int i=0;i<sizes.Length;++i)
        {
            int offset=offsets==null?source:offsets[i];int size=sizes[i];
            if(offset<0||size<=0||offset>payload.Length-size)throw new InvalidDataException("HAPチャンク範囲外");
            if(compressors[i]==10 && size>expected-at)throw new InvalidDataException("HAP非圧縮チャンクが大きすぎます。");
            var chunk=payload.Slice(offset,size);byte[] raw=compressors[i] switch{10=>chunk.ToArray(),11=>Snappy(chunk,expected-at),_=>throw new InvalidDataException("未対応HAPチャンク圧縮")};
            if(raw.Length>expected-at)throw new InvalidDataException("HAPチャンク展開過多");raw.CopyTo(output,at);at+=raw.Length;source=checked(offset+size);
        }
        if(at!=expected)throw new InvalidDataException("HAP最終BCサイズ不一致");return output;
    }
    public static byte[] Snappy(ReadOnlySpan<byte> data,int limit)
    {
        int p=0;uint size=0;bool finished=false;
        for(int shift=0;shift<=28;shift+=7){if(p>=data.Length)throw new InvalidDataException("Snappy長さ不足");byte b=data[p++];if(shift==28 && (b&240)!=0)throw new InvalidDataException("Snappy長さオーバーフロー");size|=(uint)(b&127)<<shift;if((b&128)==0){finished=true;break;}}
        if(!finished||size>limit)throw new InvalidDataException("Snappy展開上限超過");var output=new byte[(int)size];int at=0;
        while(p<data.Length && at<output.Length)
        {
            byte tag=data[p++];int type=tag&3,length,offset=0;
            if(type==0)
            {
                int code=tag>>2;length=code+1;
                if(code>=60){int n=code-59;if(p>data.Length-n)throw new InvalidDataException("Snappyリテラル長不足");uint value=0;for(int i=0;i<n;++i)value|=(uint)data[p++]<<(8*i);length=checked((int)value+1);}
                if(length>output.Length-at||length>data.Length-p)throw new InvalidDataException("Snappyリテラル範囲外");data.Slice(p,length).CopyTo(output.AsSpan(at));p+=length;at+=length;
            }
            else
            {
                int n=type==1?1:type==2?2:4;if(p>data.Length-n)throw new InvalidDataException("Snappy参照長不足");
                if(type==1){length=4+((tag>>2)&7);offset=((tag&224)<<3)|data[p++];}
                else{length=1+(tag>>2);uint value=0;for(int i=0;i<n;++i)value|=(uint)data[p++]<<(8*i);offset=checked((int)value);}
                if(offset<=0||offset>at||length>output.Length-at)throw new InvalidDataException("Snappy参照範囲外");for(int i=0;i<length;++i){output[at]=output[at-offset];++at;}
            }
        }
        if(at!=output.Length||p!=data.Length)throw new InvalidDataException("Snappy展開長不一致");return output;
    }
}
