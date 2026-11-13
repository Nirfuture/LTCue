using System.IO.Pipes;
using System.Diagnostics;
using System.Security.Principal;
using Avalonia.Controls;
using Avalonia.Threading;
namespace LtcPlayer.Avalonia;

// A process-lifetime handle avoids abandoned ownership when async work moves threads.
internal sealed class SingleInstance : IDisposable
{
 readonly Mutex gate;
 readonly CancellationTokenSource stop = new();
 readonly string pipeName;
 public bool IsFirst { get; }
 public SingleInstance(string scope="")
 {
  if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows is required.");
  using var identity = WindowsIdentity.GetCurrent();
  string user = identity.User!.Value + "." + Process.GetCurrentProcess().SessionId + scope;
  pipeName = "LtcPlayer.Activate." + user;
  gate = new Mutex(false, @"Local\LtcPlayer.Instance." + user, out bool created);
  IsFirst = created;
  if (IsFirst) _ = ListenAsync();
 }
 async Task ListenAsync()
 {
  while (!stop.IsCancellationRequested)
  {
   try
   {
    using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte,
     PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
    await pipe.WaitForConnectionAsync(stop.Token);
    var data = new byte[1];
    if (await pipe.ReadAsync(data, stop.Token) == 1 && data[0] == 1)
     Dispatcher.UIThread.Post(() => {
      if (global::Avalonia.Application.Current?.ApplicationLifetime is global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime life && life.MainWindow is {} window)
      { if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal; window.Show(); window.Activate(); }
     });
   }
   catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
   catch (IOException) { if (!stop.IsCancellationRequested) await Task.Delay(100); }
  }
 }
 public void ActivateExisting()
 {
  try { using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly); pipe.Connect(1500); pipe.WriteByte(1); }
  catch (TimeoutException) { }
  catch (IOException) { }
 }
 public void Dispose() { stop.Cancel(); gate.Dispose(); }
}
