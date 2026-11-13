using Avalonia.Controls;
namespace LtcPlayer.Avalonia;
public partial class MainWindow
{
 bool handlingOutputDisconnect;
 async Task HandleOutputDisconnectedAsync()
 {
  if(handlingOutputDisconnect||!armed)return;
  handlingOutputDisconnect=true;
  try{await DisarmAsync();StatusMessage("映像出力先の接続・表示構成が変更されたため外部映像を停止しました。設定を確認して再開してください。");}
  finally{handlingOutputDisconnect=false;}
 }
 bool handlingGraphicsFailure;
 Guid? handledGraphicsSession;
 void UpdatePreviewNotice(string normal)
 {
  var canvas=Surface.Ready?Surface.Canvas:null;
  Control<TextBlock>("PreviewInfo").Text=canvas?.SharedDeviceError is <0
   ?$"共通GPU描画エラー（0x{canvas.SharedDeviceError:X8}）。プレビューと映像出力を継続できません。アプリを再起動してください。"
   :canvas?.PreviewError is <0
    ?$"プレビュー描画エラー（0x{canvas.PreviewError:X8}）。映像・音声出力の操作は継続できます。"
    :normal;
 }
 async Task HandleSharedGraphicsFailureAsync()
 {
  if(!Surface.Ready||Surface.Canvas.SharedDeviceError>=0||handlingGraphicsFailure||handledGraphicsSession==Surface.Canvas.SessionId)return;
  handlingGraphicsFailure=true;handledGraphicsSession=Surface.Canvas.SessionId;
  try{
   await DisarmAsync();await StopSyncPreviewAsync();await Preview.StopAsync();
   UpdatePreviewNotice("");StatusMessage("共通GPUの描画に失敗したため映像再生と映像出力を停止しました。アプリを再起動してください。");
  }finally{handlingGraphicsFailure=false;UpdateEnabled();}
 }
}
