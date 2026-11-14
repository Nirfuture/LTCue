using System.Runtime.InteropServices;
namespace LtcPlayer.App;
internal static class CoverNative
{
 [DllImport("PlayerNative.dll",CallingConvention=CallingConvention.Cdecl)]static extern int output_cover(nint h,byte[]? pixels,int width,int height);
 [DllImport("PlayerNative.dll",CallingConvention=CallingConvention.Cdecl)]static extern int output_cover_mix(nint h,float amount);
 [DllImport("PlayerNative.dll",CallingConvention=CallingConvention.Cdecl)]static extern int output_redraw(nint h);
 [DllImport("PlayerNative.dll",CallingConvention=CallingConvention.Cdecl)]static extern int output_cover_prepare(nint h,byte[]? pixels,int width,int height,out nint prepared);
 [DllImport("PlayerNative.dll",CallingConvention=CallingConvention.Cdecl)]static extern int output_cover_commit(nint h,nint prepared);
 [DllImport("PlayerNative.dll",CallingConvention=CallingConvention.Cdecl)]static extern void output_cover_free(nint prepared);
 static void Validate(CoverImage? image){if(image!=null&&(image.Width<=0||image.Height<=0||image.Width>8192||image.Height>8192||image.Pixels.LongLength!=(long)image.Width*image.Height*4))throw new InvalidDataException("蓋絵の画像サイズ不正");}
 internal static nint Prepare(nint h,CoverImage? image){Validate(image);Check(output_cover_prepare(h,image?.Pixels,image?.Width??0,image?.Height??0,out var prepared));return prepared;}
 internal static void Commit(nint h,nint prepared)=>Check(output_cover_commit(h,prepared));
 internal static void Free(nint prepared)=>output_cover_free(prepared);
 static void Check(int hr){if(hr<0)throw new IOException($"蓋絵の合成失敗（0x{hr:X8}）");}
 public static void Image(nint h,CoverImage? image){Validate(image);Check(output_cover(h,image?.Pixels,image?.Width??0,image?.Height??0));}
 public static void Mix(nint h,float value)=>Check(output_cover_mix(h,value));
 public static void Redraw(nint h)=>Check(output_redraw(h));
}
