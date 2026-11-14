using Avalonia.Controls;
namespace LtcPlayer.Avalonia;

internal readonly record struct OperationAccess(bool Allowed,string Reason)
{
 public static OperationAccess Permit => new(true,"");
 public static OperationAccess Deny(string reason) => new(false,reason);
}

public partial class MainWindow
{
 // These are derived decisions, not separately maintained ownership flags.
 OperationAccess ManualControlAccess
 {
  get{
   if(closing||closePending)return OperationAccess.Deny("終了中");
   if(busy)return OperationAccess.Deny("編集・設定処理中");
   if(delivery is DeliveryState.Starting or DeliveryState.Stopping or DeliveryState.ConfirmingStop)
    return OperationAccess.Deny("出力切替中");
   if(!Surface.Ready)return OperationAccess.Deny("映像準備中");
   if(Surface.Canvas.SharedDeviceError<0)return OperationAccess.Deny("GPU描画エラー");
   if(!playback.ManualAvailable)return OperationAccess.Deny(LtcHasPriority?"LTC同期中は操作できません":"LTC入力エラー中は操作できません");
   return OperationAccess.Permit;
  }
 }
 OperationAccess ManualPlayAccess => !ManualControlAccess.Allowed?ManualControlAccess:
  Preview.IsBusy?OperationAccess.Deny("素材準備中"):OperationAccess.Permit;
 OperationAccess ManualTransportAccess
 {
  get{
   var access=ManualControlAccess;if(!access.Allowed)return access;
   if(SyncOwnsSurface||Preview.IsSyncOwned)return OperationAccess.Deny("映像保持中。▶で手動再生");
   if(Preview.LoadedPath=="")return OperationAccess.Deny("動画が未再生です");
   return OperationAccess.Permit;
  }
 }
 internal OperationAccess SeekAccess
 {
  get{
   var access=ManualTransportAccess;if(!access.Allowed)return access;
   if(Preview.IsBusy)return OperationAccess.Deny("再生位置を準備中");
   if(Preview.Snapshot.State is PreviewState.Error or PreviewState.Empty)return OperationAccess.Deny("動画を再生し直してください");
   return OperationAccess.Permit;
  }
 }
 bool editingProgram;
 internal OperationAccess ReorderAccess => closing||closePending?OperationAccess.Deny("終了中"):
  busy||editingProgram?OperationAccess.Deny("反映中"):
  delivery is DeliveryState.Starting or DeliveryState.Stopping or DeliveryState.ConfirmingStop?OperationAccess.Deny("出力切替中"):OperationAccess.Permit;
 bool DefinitionAuthority => !closing&&!closePending&&delivery is not (DeliveryState.Starting or DeliveryState.Stopping or DeliveryState.ConfirmingStop)&&playback.ManualAvailable;
 internal OperationAccess ProgramEditAccess => !ReorderAccess.Allowed?ReorderAccess:
  !playback.ManualAvailable?OperationAccess.Deny("LTC同期・入力エラー中は編集できません"):OperationAccess.Permit;
 bool CanEditProgram => ProgramEditAccess.Allowed;
 // Rechecked after the file picker; the owning save must not reject itself as busy.
 OperationAccess SaveContentAccess => closing||closePending?OperationAccess.Deny("終了中"):
  busy||editingProgram?OperationAccess.Deny("一覧・設定を反映中"):
  Editor.Rows.Count==0?OperationAccess.Deny("キューがありません"):
  LtcHasPriority?OperationAccess.Deny("LTC同期中は保存できません"):OperationAccess.Permit;
 internal OperationAccess SaveAccess => saving?OperationAccess.Deny("保存中"):SaveContentAccess;

 void UpdateOperationControls()
 {
  var seek=SeekAccess;
  Control<Slider>("Seek").IsEnabled=seek.Allowed;
  ToolTip.SetTip(Control<Slider>("Seek"),seek.Allowed?
   "":seek.Reason);
  if(!seek.Allowed&&(scrubbing||keyboardSeeking))CancelScrub();
  foreach(string name in new[]{"AddCueButton","LoadProgramButton"}){Control<Button>(name).IsEnabled=CanEditProgram;ToolTip.SetTip(Control<Button>(name),CanEditProgram?null:ProgramEditAccess.Reason);}
  UpdateCueSelectionControls();UpdateCueEditingControls();
  var save=SaveAccess;Control<Button>("SaveProgramButton").IsEnabled=save.Allowed;
  ToolTip.SetTip(Control<Button>("SaveProgramButton"),save.Allowed?"":save.Reason);
 }
}
