using System.Runtime.InteropServices;
namespace LtcPlayer.App;
internal static class CoverNative
{
 [DllImport("PlayerNative.dll",CallingConvention=CallingConvention.Cdecl)]static extern int output_cover(nint h,byte[]? pixels,int width,int height);
 [DllImport("PlayerNative.dll",CallingConvention=CallingConvention.Cdecl)]static extern int output_cover_mix(nint h,float amount);
 [DllImport("PlayerNative.dll",CallingConvention=CallingConvention.Cdecl)]static extern int output_redraw(nint h);
 static void Check(int hr){if(hr<0)throw new IOException($"蓋画合成に失敗しました: 0x{hr:X8}");}
 public static void Image(nint h,CoverImage? image)=>Check(output_cover(h,image?.Pixels,image?.Width??0,image?.Height??0));
 public static void Mix(nint h,float value)=>Check(output_cover_mix(h,value));
 public static void Redraw(nint h)=>Check(output_redraw(h));
}
