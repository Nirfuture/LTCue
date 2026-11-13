using LtcPlayer.App;
namespace LtcPlayer.Avalonia;
public sealed class LtcMonitorService : IAsyncDisposable
{
 CancellationTokenSource? cancel;Task? worker;
 public MasterClock Clock{get;private set;}=new(TimecodeRate.Modes[1]);
 readonly LtcInputLevel inputLevel=new();
 public LtcInputLevelSnapshot InputLevel=>inputLevel.Read(MasterClock.Timestamp());
 internal void ReportInputLevel(float peak,string status){Level=peak;Status=status;if(status=="入力中")inputLevel.Push(peak,MasterClock.Timestamp());else inputLevel.Reset();}
 public float Level{get;private set;}public string Status{get;private set;}="未接続";
#if VERIFICATION
 internal Action? BeforeStartForTest{get;set;}
#endif
 public int Starts{get;private set;}
 public async Task ConfigureAsync(InputSettings settings,Func<MasterClock,CancellationToken,Task>? source=null,MasterClock? restoredClock=null)
 {
  var mode=TimecodeRate.Modes.First(r=>r.Name==settings.Rate);var nextClock=restoredClock??new MasterClock(mode,settings.Freewheel,settings.Resync);
  var previousCancel=cancel;var previousWorker=worker;cancel=null;worker=null;
  if(previousCancel!=null){previousCancel.Cancel();try{if(previousWorker!=null)await previousWorker;}catch(OperationCanceledException){}finally{previousCancel.Dispose();}}
#if VERIFICATION
  BeforeStartForTest?.Invoke();
#endif
  Clock=nextClock;Level=0;inputLevel.Reset();Status="入力待機";cancel=new();Starts++;
  if(source!=null){var clock=Clock;var token=cancel.Token;worker=Task.Run(()=>source(clock,token));}
  else if(settings.DeviceId!="")worker=new AudioInputService().RunAsync(settings.DeviceId,settings.Channel,mode,Clock,ReportInputLevel,null,cancel.Token);
  else{Status="設定から入力機器を選択してください。";worker=Task.CompletedTask;}
 }
 public async ValueTask DisposeAsync(){inputLevel.Reset();cancel?.Cancel();if(worker!=null)try{await worker;}catch(OperationCanceledException){}cancel?.Dispose();cancel=null;}
}
