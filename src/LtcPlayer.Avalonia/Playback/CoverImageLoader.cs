using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System.Runtime.InteropServices;
namespace LtcPlayer.App;
public static class CoverImageLoader
{
 public static Task<CoverImage?> LoadAsync(string path)=>Task.Run(()=>{
  if(string.IsNullOrWhiteSpace(path))return null;
  if(!new[]{".png",".jpg",".jpeg"}.Contains(System.IO.Path.GetExtension(path).ToLowerInvariant()))throw new InvalidDataException("蓋画はPNGまたはJPEGを選択してください。");
  using var stream=File.OpenRead(path);using var source=Bitmap.DecodeToWidth(stream,2048);
  var size=source.PixelSize;if(size.Width<=0||size.Height<=0||size.Width>8192||size.Height>8192||(long)size.Width*size.Height>16777216)throw new InvalidDataException("蓋画の画像サイズが大きすぎます。");
  using var target=new RenderTargetBitmap(size,new Vector(96,96));
  using(var dc=target.CreateDrawingContext()){dc.DrawRectangle(Brushes.Black,null,new Rect(size.ToSize(1)));dc.DrawImage(source,new Rect(size.ToSize(1)));}
  if(target.Format!=PixelFormat.Bgra8888)throw new InvalidDataException("蓋画のピクセル形式を変換できません。");
  var pixels=new byte[checked(size.Width*size.Height*4)];var pin=GCHandle.Alloc(pixels,GCHandleType.Pinned);try{target.CopyPixels(new PixelRect(size),pin.AddrOfPinnedObject(),pixels.Length,size.Width*4);}finally{pin.Free();}
  return new CoverImage(pixels,size.Width,size.Height,System.IO.Path.GetFullPath(path));
 });
}
