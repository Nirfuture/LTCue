using Avalonia.Threading;
namespace LtcPlayer.Avalonia;
// Explicit UI boundary; production clock/decoder/scheduler algorithms are unchanged.
public sealed class UiDispatcher
{
 public async Task InvokeAsync(Action action){
#if VERIFICATION
  double queued=LtcPlayer.App.MasterClock.Timestamp();
  await Dispatcher.UIThread.InvokeAsync(()=>{
   double delay=(LtcPlayer.App.MasterClock.Timestamp()-queued)*1000;
   PresentationTimingVerification.QueueDelay(delay);
   var previous=PresentationTimingVerification.CurrentQueueDelayMs;
   PresentationTimingVerification.CurrentQueueDelayMs=delay;
   try{action();}finally{PresentationTimingVerification.CurrentQueueDelayMs=previous;}
  });
#else
  await Dispatcher.UIThread.InvokeAsync(action);
#endif
 }
}
