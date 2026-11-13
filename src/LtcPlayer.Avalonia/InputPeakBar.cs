using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
namespace LtcPlayer.Avalonia;
public sealed class InputPeakBar : Control
{
 public static readonly StyledProperty<double> ValueProperty=AvaloniaProperty.Register<InputPeakBar,double>(nameof(Value));
 public static readonly StyledProperty<IBrush?> ForegroundProperty=AvaloniaProperty.Register<InputPeakBar,IBrush?>(nameof(Foreground),Brush.Parse("#55B2F6"));
 public static readonly StyledProperty<IBrush?> BackgroundProperty=AvaloniaProperty.Register<InputPeakBar,IBrush?>(nameof(Background),Brush.Parse("#303743"));
 public double Value{get=>GetValue(ValueProperty);set=>SetValue(ValueProperty,value);}
 public IBrush? Foreground{get=>GetValue(ForegroundProperty);set=>SetValue(ForegroundProperty,value);}
 public IBrush? Background{get=>GetValue(BackgroundProperty);set=>SetValue(BackgroundProperty,value);}
 static InputPeakBar(){AffectsRender<InputPeakBar>(ValueProperty,ForegroundProperty,BackgroundProperty);}
 public override void Render(DrawingContext context){base.Render(context);var rect=new Rect(0,0,Bounds.Width,Bounds.Height);context.DrawRectangle(Background,null,rect,2,2);double width=Bounds.Width*Math.Clamp(double.IsFinite(Value)?Value:0,0,60)/60;if(width>0)context.DrawRectangle(Foreground,null,new Rect(0,0,width,Bounds.Height),2,2);}
}
