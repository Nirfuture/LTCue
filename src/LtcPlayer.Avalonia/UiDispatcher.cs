using Avalonia.Threading;
namespace LtcPlayer.Avalonia;
// Explicit UI boundary; production clock/decoder/scheduler algorithms are unchanged.
public sealed class UiDispatcher
{
 public async Task InvokeAsync(Action action)=>await Dispatcher.UIThread.InvokeAsync(action);
}
