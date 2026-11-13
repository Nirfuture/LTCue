using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
namespace LtcPlayer.Avalonia;
// Lucide SVG paths, ISC license in Assets/Lucide/LICENSE. Rendered as vectors at any scale.
public sealed class LucideIcon : Control
{
 public static readonly StyledProperty<string> KindProperty=AvaloniaProperty.Register<LucideIcon,string>(nameof(Kind),"play");
 public string Kind{get=>GetValue(KindProperty);set=>SetValue(KindProperty,value);}
 public static readonly StyledProperty<IBrush?> ForegroundProperty=global::Avalonia.Controls.Documents.TextElement.ForegroundProperty.AddOwner<LucideIcon>();
 static readonly Dictionary<string,Geometry[]> icons=new Dictionary<string,string[]>{
 ["monitor-up"]=new[]{"m9 10 3-3 3 3","M12 13V7","M4.0 3.0H20.0Q22.0 3.0 22.0 5.0V15.0Q22.0 17.0 20.0 17.0H4.0Q2.0 17.0 2.0 15.0V5.0Q2.0 3.0 4.0 3.0Z","M12 17v4","M8 21h8"},
  ["grip-vertical"]=new[]{"M8.0 12.0a1.0 1.0 0 1 0 2.0 0a1.0 1.0 0 1 0 -2.0 0","M8.0 5.0a1.0 1.0 0 1 0 2.0 0a1.0 1.0 0 1 0 -2.0 0","M8.0 19.0a1.0 1.0 0 1 0 2.0 0a1.0 1.0 0 1 0 -2.0 0","M14.0 12.0a1.0 1.0 0 1 0 2.0 0a1.0 1.0 0 1 0 -2.0 0","M14.0 5.0a1.0 1.0 0 1 0 2.0 0a1.0 1.0 0 1 0 -2.0 0","M14.0 19.0a1.0 1.0 0 1 0 2.0 0a1.0 1.0 0 1 0 -2.0 0"},
  ["image-plus"]=new[]{"M16 5h6","M19 2v6","M21 11.5V19a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h7.5","m21 15-3.086-3.086a2 2 0 0 0-2.828 0L6 21","M7.0 9.0a2.0 2.0 0 1 0 4.0 0a2.0 2.0 0 1 0 -4.0 0"},
  ["layers"]=new[]{"M12.83 2.18a2 2 0 0 0-1.66 0L2.6 6.08a1 1 0 0 0 0 1.83l8.58 3.91a2 2 0 0 0 1.66 0l8.58-3.9a1 1 0 0 0 0-1.83z","M2 12a1 1 0 0 0 .58.91l8.6 3.91a2 2 0 0 0 1.65 0l8.58-3.9A1 1 0 0 0 22 12","M2 17a1 1 0 0 0 .58.91l8.6 3.91a2 2 0 0 0 1.65 0l8.58-3.9A1 1 0 0 0 22 17"},
  ["pause"]=new[]{"M15.0 3.0H18.0Q19.0 3.0 19.0 4.0V20.0Q19.0 21.0 18.0 21.0H15.0Q14.0 21.0 14.0 20.0V4.0Q14.0 3.0 15.0 3.0Z","M6.0 3.0H9.0Q10.0 3.0 10.0 4.0V20.0Q10.0 21.0 9.0 21.0H6.0Q5.0 21.0 5.0 20.0V4.0Q5.0 3.0 6.0 3.0Z"},
  ["play"]=new[]{"M5 5a2 2 0 0 1 3.008-1.728l11.997 6.998a2 2 0 0 1 .003 3.458l-12 7A2 2 0 0 1 5 19z"},
  ["refresh-cw"]=new[]{"M3 12a9 9 0 0 1 9-9 9.75 9.75 0 0 1 6.74 2.74L21 8","M21 3v5h-5","M21 12a9 9 0 0 1-9 9 9.75 9.75 0 0 1-6.74-2.74L3 16","M8 16H3v5"},
  ["settings-2"]=new[]{"M14 17H5","M19 7h-9","M14.0 17.0a3.0 3.0 0 1 0 6.0 0a3.0 3.0 0 1 0 -6.0 0","M4.0 7.0a3.0 3.0 0 1 0 6.0 0a3.0 3.0 0 1 0 -6.0 0"},
  ["square"]=new[]{"M5.0 3.0H19.0Q21.0 3.0 21.0 5.0V19.0Q21.0 21.0 19.0 21.0H5.0Q3.0 21.0 3.0 19.0V5.0Q3.0 3.0 5.0 3.0Z"},
  ["volume-2"]=new[]{"M11 4.702a.705.705 0 0 0-1.203-.498L6.413 7.587A1.4 1.4 0 0 1 5.416 8H3a1 1 0 0 0-1 1v6a1 1 0 0 0 1 1h2.416a1.4 1.4 0 0 1 .997.413l3.383 3.384A.705.705 0 0 0 11 19.298z","M16 9a5 5 0 0 1 0 6","M19.364 18.364a9 9 0 0 0 0-12.728"},
  ["volume-x"]=new[]{"M11 4.702a.7.7 0 0 0-1.203-.498L6.413 7.587A1.4 1.4 0 0 1 5.416 8H3a1 1 0 0 0-1 1v6a1 1 0 0 0 1 1h2.416a1.4 1.4 0 0 1 .997.413l3.383 3.384A.7.7 0 0 0 11 19.298z","m16.5 14.5 5-5","m16.5 9.5 5 5"},
  ["x"]=new[]{"M18 6 6 18","m6 6 12 12"},
 }.ToDictionary(p=>p.Key,p=>p.Value.Select(Geometry.Parse).ToArray());
 static LucideIcon(){AffectsRender<LucideIcon>(KindProperty,ForegroundProperty);}
 public LucideIcon(){Width=16;Height=16;}
 public override void Render(DrawingContext context){base.Render(context);if(!icons.TryGetValue(Kind,out var path))return;double scale=Math.Min(Bounds.Width,Bounds.Height)/24;using(context.PushTransform(Matrix.CreateScale(scale,scale)*Matrix.CreateTranslation((Bounds.Width-24*scale)/2,(Bounds.Height-24*scale)/2)))foreach(var part in path)context.DrawGeometry(null,new Pen(GetValue(ForegroundProperty)??Brush.Parse("#C8D4E0"),2,lineCap:PenLineCap.Round,lineJoin:PenLineJoin.Round),part);}
}
