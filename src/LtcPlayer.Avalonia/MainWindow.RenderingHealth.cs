using Avalonia.Controls;
namespace LtcPlayer.Avalonia;
public partial class MainWindow
{
 bool handlingOutputDisconnect;
 async Task HandleOutputDisconnectedAsync()
 {
  if(handlingOutputDisconnect||!outputSessionActive)return;
  handlingOutputDisconnect=true;
  try{await DisarmAsync();StatusMessage("出力先の変更・切断により出力停止");}
  finally{handlingOutputDisconnect=false;}
 }
 bool handlingGraphicsFailure;
 Guid? handledGraphicsSession;
 void UpdatePreviewNotice(string normal)
 {
  var canvas=Surface.Ready?Surface.Canvas:null;
  Control<TextBlock>("PreviewInfo").Text=canvas?.SharedDeviceError is <0
   ?$"GPU描画エラー（0x{canvas.SharedDeviceError:X8}）。再起動してください"
   :canvas?.PreviewError is <0
    ?$"プレビュー描画エラー（0x{canvas.PreviewError:X8}、出力は継続）"
    :normal;
 }
 async Task HandleSharedGraphicsFailureAsync()
 {
  if(!Surface.Ready||Surface.Canvas.SharedDeviceError>=0||handlingGraphicsFailure||handledGraphicsSession==Surface.Canvas.SessionId)return;
  handlingGraphicsFailure=true;handledGraphicsSession=Surface.Canvas.SessionId;
  try{
   await DisarmAsync();await StopSyncPreviewAsync();await Preview.StopAsync();
   UpdatePreviewNotice("");StatusMessage("GPU描画エラーで出力停止。再起動してください");
  }finally{handlingGraphicsFailure=false;UpdateEnabled();}
 }
}
