using Avalonia.Controls;
using Avalonia.Media;
namespace LtcPlayer.Avalonia;

// Edit UI colors here. Video pixels and cover compositing are not theme colors.
// Registered once at startup; changing this palette requires rebuilding the app.
public static class ThemePalette
{
 // 基本色：通常の配色変更では、この10色だけを編集してください。
 public static SolidColorBrush AppBackground { get; } = new(Color.Parse("#121212")); // 全体背景
 public static SolidColorBrush PanelBackground { get; } = new(Color.Parse("#1E1E1E")); // パネル
 public static SolidColorBrush Border { get; } = new(Color.Parse("#353535")); // 境界線
 public static SolidColorBrush Accent { get; } = new(Color.Parse("#55B2F6")); // アクセント
 public static SolidColorBrush SecondaryText { get; } = new(Color.Parse("#A5B0BF")); // 補助文字
 public static SolidColorBrush IconFallback { get; } = new(Color.Parse("#C8D4E0")); // アイコンの代替色
 public static SolidColorBrush Warning { get; } = new(Color.Parse("#E6B566")); // 警告
 public static SolidColorBrush Success { get; } = new(Color.Parse("#80D5B1")); // 正常
 public static SolidColorBrush OutputActive { get; } = new(Color.Parse("#BC303A")); // 外部出力中
 public static SolidColorBrush PlayingCue { get; } = new(Color.Parse("#214F73")); // 再生中の行

 // 派生色：用途名は維持し、基本色から自動的に作ります。
 public static SolidColorBrush AccentHover { get; } = Mix(Accent,Colors.White,.30);
 public static SolidColorBrush AccentPressed { get; } = Mix(Accent,Colors.White,.55);
 public static SolidColorBrush AccentMuted { get; } = Mix(Accent,PanelBackground.Color,.25);
 public static SolidColorBrush Selection { get; } = Mix(PanelBackground,Colors.White,.16);
 public static SolidColorBrush SelectionHover { get; } = Mix(PanelBackground,Colors.White,.23);
 public static SolidColorBrush SelectionInactive { get; } = Mix(PanelBackground,Colors.White,.10);
 public static SolidColorBrush SelectionInactiveHover { get; } = Mix(PanelBackground,Colors.White,.16);
 public static SolidColorBrush AudioEnabledBackground { get; } = Mix(PanelBackground,Accent.Color,.18);
 public static SolidColorBrush InfoText => AccentHover;
 public static SolidColorBrush HelpText => SecondaryText;
 public static SolidColorBrush StatusText => InfoText;
 public static SolidColorBrush PreviewSurround { get; } = Mix(PanelBackground,Border.Color,.50);
 public static SolidColorBrush OutputIdle => Border;

 // RGBを混ぜた不透明色。Opacityで下の映像や背景が透けることを避けます。
 static SolidColorBrush Mix(SolidColorBrush source,Color target,double amount)
 {
  var color=source.Color;
  byte Blend(byte from,byte to)=>(byte)Math.Round(from+(to-from)*amount);
  return new(Color.FromRgb(Blend(color.R,target.R),Blend(color.G,target.G),Blend(color.B,target.B)));
 }

 public static SolidColorBrush ErrorText {get;}=Mix(OutputActive,Colors.White,.50);
 public static void Register(IResourceDictionary resources)
 {
  resources["Palette.ErrorText"]=ErrorText;
  resources["Palette.AppBackground"] = AppBackground;
  resources["Palette.AppBackgroundColor"] = AppBackground.Color;
  resources["Palette.PanelBackground"] = PanelBackground;
  resources["Palette.PanelBackgroundColor"] = PanelBackground.Color;
  resources["Palette.Border"] = Border;
  resources["Palette.BorderColor"] = Border.Color;
  resources["Palette.Accent"] = Accent;
  resources["Palette.AccentColor"] = Accent.Color;
  resources["Palette.AccentHover"] = AccentHover;
  resources["Palette.AccentHoverColor"] = AccentHover.Color;
  resources["Palette.AccentPressed"] = AccentPressed;
  resources["Palette.AccentPressedColor"] = AccentPressed.Color;
  resources["Palette.AccentMuted"] = AccentMuted;
  resources["Palette.AccentMutedColor"] = AccentMuted.Color;
  resources["Palette.Selection"] = Selection;
  resources["Palette.SelectionColor"] = Selection.Color;
  resources["Palette.SelectionHover"] = SelectionHover;
  resources["Palette.SelectionHoverColor"] = SelectionHover.Color;
  resources["Palette.SelectionInactive"] = SelectionInactive;
  resources["Palette.SelectionInactiveColor"] = SelectionInactive.Color;
  resources["Palette.SelectionInactiveHover"] = SelectionInactiveHover;
  resources["Palette.SelectionInactiveHoverColor"] = SelectionInactiveHover.Color;
  resources["Palette.AudioEnabledBackground"] = AudioEnabledBackground;
  resources["Palette.AudioEnabledBackgroundColor"] = AudioEnabledBackground.Color;
  resources["Palette.InfoText"] = InfoText;
  resources["Palette.InfoTextColor"] = InfoText.Color;
  resources["Palette.SecondaryText"] = SecondaryText;
  resources["Palette.SecondaryTextColor"] = SecondaryText.Color;
  resources["Palette.HelpText"] = HelpText;
  resources["Palette.HelpTextColor"] = HelpText.Color;
  resources["Palette.StatusText"] = StatusText;
  resources["Palette.StatusTextColor"] = StatusText.Color;
  resources["Palette.IconFallback"] = IconFallback;
  resources["Palette.IconFallbackColor"] = IconFallback.Color;
  resources["Palette.PreviewSurround"] = PreviewSurround;
  resources["Palette.PreviewSurroundColor"] = PreviewSurround.Color;
  resources["Palette.Warning"] = Warning;
  resources["Palette.WarningColor"] = Warning.Color;
  resources["Palette.Success"] = Success;
  resources["Palette.SuccessColor"] = Success.Color;
  resources["Palette.OutputActive"] = OutputActive;
  resources["Palette.OutputActiveColor"] = OutputActive.Color;
  resources["Palette.OutputIdle"] = OutputIdle;
  resources["Palette.OutputIdleColor"] = OutputIdle.Color;
  resources["Palette.PlayingCue"] = PlayingCue;
  resources["Palette.PlayingCueColor"] = PlayingCue.Color;
 }
}
