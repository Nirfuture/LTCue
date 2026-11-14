using Avalonia.Platform.Storage;
namespace LtcPlayer.Avalonia;

public partial class MainWindow
{
 bool saving;
 CancellationTokenSource? saveCancellation;
 TaskCompletionSource? saveCompletion;

 // Saving owns its lifetime independently of editing and playback. A provider
 // separates the file picker from the same command used by verification.
 async Task SaveProgramAsync(Func<Task<string?>> choosePath,CancellationToken token=default)
 {
  var access=SaveAccess;if(!access.Allowed)throw new InvalidOperationException(access.Reason);
  saving=true;
  using var operation=CancellationTokenSource.CreateLinkedTokenSource(token);
  saveCancellation=operation;
  var completion=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
  saveCompletion=completion;UpdateEnabled();
  try{
   var path=await choosePath();operation.Token.ThrowIfCancellationRequested();
   if(path==null)return;
   var current=SaveContentAccess;if(!current.Allowed)throw new InvalidOperationException(current.Reason);
   await Editor.SaveAsync(path,operation.Token);
   StatusMessage("キューリストを保存しました。");
  }finally{
   saveCancellation=null;saveCompletion=null;saving=false;completion.TrySetResult();UpdateEnabled();
  }
 }
 async Task ChooseAndSaveProgramAsync()=>await SaveProgramAsync(async()=>{
  var file=await StorageProvider.SaveFilePickerAsync(new(){Title="キューリスト保存",DefaultExtension="json",FileTypeChoices=[new("キューリスト"){Patterns=["*.json"]}]});
  return file?.TryGetLocalPath();
 });
 async Task CancelAndWaitForSaveAsync()
 {
  var completion=saveCompletion;saveCancellation?.Cancel();
  if(completion!=null)await completion.Task;
 }
#if VERIFICATION
 internal Task SaveProgramForTestAsync(string path,CancellationToken token=default)=>SaveProgramAsync(()=>Task.FromResult<string?>(path),token);
 internal Task ChooseSaveForTestAsync(Func<Task<string?>> choosePath)=>SaveProgramAsync(choosePath);
 internal Task CancelSaveForTestAsync()=>CancelAndWaitForSaveAsync();
#endif
}
