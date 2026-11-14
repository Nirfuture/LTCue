using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using LtcPlayer.App;
namespace LtcPlayer.Avalonia;

public partial class MainWindow
{
 bool choosingCover;
 CancellationTokenSource? coverCancellation;
 TaskCompletionSource? coverCompletion;
 OperationAccess CoverResourceAccess => closing||closePending?OperationAccess.Deny("終了中"):
  !presentationReady||!Surface.Ready?OperationAccess.Deny("映像準備中"):
  Surface.Canvas.SharedDeviceError<0?OperationAccess.Deny("GPU描画エラー"):
  busy||editingProgram||applyingSettings?OperationAccess.Deny("変更を反映中"):OperationAccess.Permit;
 internal OperationAccess CoverChangeAccess => !CoverResourceAccess.Allowed?CoverResourceAccess:
  choosingCover?OperationAccess.Deny("蓋絵準備中"):OperationAccess.Permit;
 bool CanChangeCover=>CoverChangeAccess.Allowed;
#if VERIFICATION
 internal Func<CancellationToken,Task>? CoverBeforeLoadForTest{get;set;}
 internal Action? CoverBeforePersistForTest{get;set;}
 internal Task ChangeCoverForTestAsync(string path,CancellationToken token=default)=>ChangeCoverAsync(_=>Task.FromResult<string?>(path),token);
 internal Task CancelCoverForTestAsync()=>CancelAndWaitForCoverAsync();
#endif
 async Task ChangeCoverAsync(Func<CancellationToken,Task<string?>> choosePath,CancellationToken token=default)
 {
  var access=CoverChangeAccess;if(!access.Allowed)throw new InvalidOperationException(access.Reason);
  choosingCover=true;
  using var operation=CancellationTokenSource.CreateLinkedTokenSource(token);
  coverCancellation=operation;
  var completion=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);coverCompletion=completion;UpdateCoverLabel();UpdateDeliveryButton();
  try{
   var path=await choosePath(operation.Token).WaitAsync(operation.Token);
   if(path==null)return;
#if VERIFICATION
   if(CoverBeforeLoadForTest!=null)await CoverBeforeLoadForTest(operation.Token);
#endif
   var image=await CoverImageLoader.LoadAsync(path,operation.Token);
   operation.Token.ThrowIfCancellationRequested();
   PersistCover(path,image);
   StatusMessage(path==""?"蓋絵を解除しました":"蓋絵を更新しました。");
  }finally{
   coverCancellation=null;coverCompletion=null;choosingCover=false;completion.TrySetResult();if(!closing){UpdateCoverLabel();UpdateDeliveryButton();}
  }
 }
 void PersistCover(string path,CoverImage? image)
 {
  if(!CoverResourceAccess.Allowed)throw new InvalidOperationException(CoverResourceAccess.Reason);
  var canvas=Surface.Canvas;
  using var prepared=canvas.PrepareCover(image);
  // Read current settings after image preparation; concurrent volume/fade edits survive.
  var next=settings with{CoverPath=path};
  using var pending=new PendingSettings(Program.Option("--settings")??InputSettings.PathName,next,selectedDisplayName);
#if VERIFICATION
  CoverBeforePersistForTest?.Invoke();
#endif
  if(!CoverResourceAccess.Allowed||!ReferenceEquals(canvas,Surface.Canvas))throw new InvalidOperationException("準備中に映像面の状態が変更されました。");
  prepared.Validate();
  // No await or allocating GPU operation between persistence and adoption.
  pending.Commit();prepared.Commit();settings=next;cover.Image=image;coverLoadWarning="";
  Surface.AcceptPreparedCover(image);UpdateCoverLabel();
 }
 async void ChooseCoverClick(object? sender,RoutedEventArgs e)=>await Safe(async()=>{
  try{await ChangeCoverAsync(async _=>{
   var files=await StorageProvider.OpenFilePickerAsync(new(){Title="蓋絵を選択",AllowMultiple=false,FileTypeFilter=[new("PNG・JPEG"){Patterns=["*.png","*.jpg","*.jpeg"]}]});
   return files.FirstOrDefault()?.TryGetLocalPath();
  });}catch(Exception)when(Surface.Ready&&Surface.Canvas.SharedDeviceError<0){await HandleSharedGraphicsFailureAsync();}
 });
 async void ClearCoverClick(object? sender,RoutedEventArgs e)=>await Safe(async()=>{
  try{await ChangeCoverAsync(_=>Task.FromResult<string?>(""));}
  catch(Exception)when(Surface.Ready&&Surface.Canvas.SharedDeviceError<0){await HandleSharedGraphicsFailureAsync();}
 });
 async Task CancelAndWaitForCoverAsync(){var completion=coverCompletion;coverCancellation?.Cancel();if(completion!=null)await completion.Task;}
}
