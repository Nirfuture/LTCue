using LtcPlayer.App;
namespace LtcPlayer.Avalonia;
public sealed class LtcMonitorService : IAsyncDisposable
{
 readonly SemaphoreSlim commands=new(1);
 readonly object inputState=new();
 CancellationTokenSource? cancel;Task? worker;long generation;volatile bool disposed;
 MasterClock clock=new(TimecodeRate.Modes[1]);
 public MasterClock Clock=>Volatile.Read(ref clock);
 readonly LtcInputLevel inputLevel=new();
 public LtcInputLevelSnapshot InputLevel=>inputLevel.Read(MasterClock.Timestamp());
 internal void ReportInputLevel(float peak,string status){Level=peak;Status=status;if(status=="入力中")inputLevel.Push(peak,MasterClock.Timestamp());else inputLevel.Reset();}
 public float Level{get;private set;}public string Status{get;private set;}="未接続";
#if VERIFICATION
 internal Action? BeforeStartForTest{get;set;}
 internal Func<InputSettings,MasterClock,Action<float,string>,Action<Exception?>,CancellationToken,Task>? RunInputForTest{get;set;}
#endif
 public int Starts{get;private set;}
 async Task StopCurrentAsync()
 {
  CancellationTokenSource? oldCancel;Task? oldWorker;
  lock(inputState){oldCancel=cancel;oldWorker=worker;cancel=null;worker=null;}
  if(oldCancel==null)return;
  oldCancel.Cancel();try{if(oldWorker!=null)await oldWorker;}catch(OperationCanceledException){}finally{oldCancel.Dispose();}
 }
 public async Task ConfigureAsync(InputSettings settings,Func<MasterClock,CancellationToken,Task>? source=null,MasterClock? restoredClock=null,bool requireReady=false)
 {
  var mode=TimecodeRate.Modes.First(r=>r.Name==settings.Rate);var next=restoredClock??new MasterClock(mode,settings.Freewheel,settings.Resync);
  await commands.WaitAsync();
  try{
   ObjectDisposedException.ThrowIf(disposed,this);long owner;lock(inputState)owner=++generation;await StopCurrentAsync();
#if VERIFICATION
   BeforeStartForTest?.Invoke();
#endif
   lock(inputState){ObjectDisposedException.ThrowIf(disposed,this);Volatile.Write(ref clock,next);Level=0;inputLevel.Reset();Status="入力待機";cancel=new();Starts++;}
   var token=cancel.Token;var ready=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
   void Ready(Exception? failure){if(!requireReady)return;if(failure==null)ready.TrySetResult();else ready.TrySetException(failure);}
   void StatusUpdate(float peak,string status){lock(inputState){if(generation==owner&&!token.IsCancellationRequested)ReportInputLevel(peak,status);}}
   if(source!=null)worker=Task.Run(async()=>{try{var running=source(next,token);Ready(null);await running;}catch(Exception e){Ready(e);throw;}});
#if VERIFICATION
   else if(RunInputForTest!=null)worker=RunInputForTest(settings,next,StatusUpdate,Ready,token);
#endif
   else if(settings.DeviceId!="")worker=new AudioInputService().RunAsync(settings.DeviceId,settings.Channel,mode,next,StatusUpdate,null,token,requireReady?Ready:null);
   else{Status="入力機器が未選択です";worker=Task.CompletedTask;Ready(null);}
   if(requireReady){
    try{await ready.Task.WaitAsync(TimeSpan.FromSeconds(10),token);}
    catch(TimeoutException e){throw new IOException("LTC入力の開始がタイムアウト",e);}
   }
  }catch{next.Fault();await StopCurrentAsync();throw;}
  finally{commands.Release();}
 }
 public async ValueTask DisposeAsync()
 {
  lock(inputState){disposed=true;generation++;inputLevel.Reset();cancel?.Cancel();}
  await commands.WaitAsync();
  try{await StopCurrentAsync();}finally{commands.Release();}
 }
}
