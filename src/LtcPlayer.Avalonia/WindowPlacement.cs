using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
namespace LtcPlayer.Avalonia;
internal static class WindowPlacement
{
 public static void Track(Window window){
  var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(180)};bool opened=false;string? screenKey=null;
  void Queue(){if(!opened)return;timer.Stop();timer.Start();}
  void FitChanged(){
   if(!opened)return;var screen=window.Screens.ScreenFromWindow(window)??window.Screens.Primary;if(screen==null)return;
   string key=screen.WorkingArea+"/"+screen.Scaling;if(key==screenKey)return;screenKey=key;Fit(window);
  }
  timer.Tick+=(_,_)=>{timer.Stop();FitChanged();};
  window.Opened+=(_,_)=>{opened=true;FitChanged();};
  window.PositionChanged+=(_,_)=>Queue();
  window.ScalingChanged+=(_,_)=>{screenKey=null;Queue();};
  window.PropertyChanged+=(_,e)=>{if(e.Property==Window.WindowStateProperty&&window.WindowState==WindowState.Normal){screenKey=null;Queue();}};
  window.Closed+=(_,_)=>{opened=false;timer.Stop();};
 }
 public static void Fit(Window window){
  if(window.WindowState!=WindowState.Normal)return;
  var screen=window.Screens.ScreenFromWindow(window)??window.Screens.Primary;if(screen==null)return;
  var area=screen.WorkingArea;double scale=screen.Scaling;
  // Native frame and client resize notifications can arrive in different layout passes.
  // Bound decoration estimates so a stale pre-move size never becomes a huge title bar.
  double chromeWidth=Math.Clamp((window.FrameSize?.Width??window.ClientSize.Width)-window.ClientSize.Width,0,32);
  double chromeHeight=Math.Clamp((window.FrameSize?.Height??window.ClientSize.Height+32)-window.ClientSize.Height,0,80);
  double maxWidth=Math.Max(240,area.Width/scale-chromeWidth-16),maxHeight=Math.Max(240,area.Height/scale-chromeHeight-16);
  double width=Math.Min(double.IsFinite(window.Width)?window.Width:window.ClientSize.Width,maxWidth);
  double height=Math.Min(double.IsFinite(window.Height)?window.Height:window.ClientSize.Height,maxHeight);
  window.Width=width;window.Height=height;
  int outerWidth=(int)Math.Ceiling((width+chromeWidth)*scale),outerHeight=(int)Math.Ceiling((height+chromeHeight)*scale);
  window.Position=new PixelPoint(Math.Clamp(window.Position.X,area.X,Math.Max(area.X,area.Right-outerWidth)),Math.Clamp(window.Position.Y,area.Y,Math.Max(area.Y,area.Bottom-outerHeight)));
 }
}
