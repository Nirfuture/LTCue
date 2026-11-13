using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System.Runtime.InteropServices;
namespace LtcPlayer.Avalonia;
// Adapters only: ported Phase 1–4 assertions/algorithms remain intact.
public sealed class RegressionWindow
{
 public UiDispatcher Dispatcher{get;}=new();
 public static void SavePng(string path,byte[] pixels,int w,int h)
 {
  using var bitmap=new WriteableBitmap(new PixelSize(w,h),new Vector(96,96),PixelFormat.Bgra8888,AlphaFormat.Opaque);
  using(var frame=bitmap.Lock())for(int y=0;y<h;y++)Marshal.Copy(pixels,y*w*4,frame.Address+y*frame.RowBytes,w*4);
  bitmap.Save(path);
 }
}
public sealed class RegressionApplication
{
 public static RegressionApplication Current{get;}=new();public int ExitCode{get;private set;}
 public void Shutdown(int exitCode)=>ExitCode=exitCode;
}
