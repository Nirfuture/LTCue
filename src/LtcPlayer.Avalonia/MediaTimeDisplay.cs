using System.Globalization;
using Avalonia.Data.Converters;
using LtcPlayer.App;
namespace LtcPlayer.Avalonia;
public static class MediaTimeDisplay
{
 // Elapsed wall time plus frame within the second; independent of the LTC rate/DF mode.
 public static string Format(double? seconds,double fps)
 {
  if(seconds is not {} value||!double.IsFinite(value)||!double.IsFinite(fps)||fps<=0)return "--:--:--:--";
  value=Math.Max(0,value);long whole=(long)Math.Floor(value+1e-8);
  int frame=Math.Clamp((int)Math.Floor(Math.Max(0,value-whole)*fps+1e-7),0,(int)Math.Ceiling(fps)-1);
  return $"{whole/3600:00}:{whole/60%60:00}:{whole%60:00}:{frame:00}";
 }
}
public sealed class CueDurationConverter : IValueConverter
{
 public object Convert(object? value,Type targetType,object? parameter,CultureInfo culture)=>value is CueRow cue?MediaTimeDisplay.Format(cue.Duration,cue.FrameRate):"--:--:--:--";
 public object ConvertBack(object? value,Type targetType,object? parameter,CultureInfo culture)=>throw new NotSupportedException();
}
