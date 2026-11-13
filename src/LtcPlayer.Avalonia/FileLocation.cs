using System.Diagnostics;
using System.Runtime.InteropServices;
namespace LtcPlayer.Avalonia;
internal static class FileLocation
{
 // A separate STA keeps slow/offline paths and Shell calls off the playback UI thread.
 public static Task RevealAsync(string path)
 {
  if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException();
  var completion=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
  var thread=new Thread(()=>{
   nint item=0;bool initialized=false;
   try{
    Marshal.ThrowExceptionForHR(CoInitializeEx(0,2));initialized=true;
    string full=Path.GetFullPath(path);
    if(File.Exists(full)){
     Marshal.ThrowExceptionForHR(SHParseDisplayName(full,0,out item,0,out _));
     // cidl=0 selects the fully qualified item and opens its parent.
     // https://learn.microsoft.com/windows/win32/api/shlobj_core/nf-shlobj_core-shopenfolderandselectitems
     Marshal.ThrowExceptionForHR(SHOpenFolderAndSelectItems(item,0,0,0));
    }else{
     string? parent=Path.GetDirectoryName(full);
     if(parent==null||!Directory.Exists(parent))throw new DirectoryNotFoundException("ファイルと保存先フォルダーが見つかりません："+full);
     Process.Start(new ProcessStartInfo(parent){UseShellExecute=true});
    }
    completion.TrySetResult();
   }catch(Exception e){completion.TrySetException(e);}
   finally{if(item!=0)Marshal.FreeCoTaskMem(item);if(initialized)CoUninitialize();}
  }){IsBackground=true,Name="Reveal cue file"};
  thread.SetApartmentState(ApartmentState.STA);thread.Start();return completion.Task;
 }
 [DllImport("ole32.dll")]static extern int CoInitializeEx(nint reserved,uint flags);
 [DllImport("ole32.dll")]static extern void CoUninitialize();
 [DllImport("shell32.dll",CharSet=CharSet.Unicode)]static extern int SHParseDisplayName(string name,nint bindContext,out nint item,uint attributes,out uint actualAttributes);
 [DllImport("shell32.dll")]static extern int SHOpenFolderAndSelectItems(nint item,uint count,nint children,uint flags);
}
