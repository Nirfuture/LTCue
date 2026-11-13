using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
namespace LtcPlayer.Avalonia;
internal static class Program
{
 public static string[] Arguments=[];
 public static bool VerificationOutput { get {
#if VERIFICATION
 return Arguments.Contains("--probe-output");
#else
 return false;
#endif
 }} 
 public static string? Option(string name){int i=Array.IndexOf(Arguments,name);return i>=0&&i+1<Arguments.Length?Arguments[i+1]:null;}
 [STAThread] public static int Main(string[] args)
 {
  #if VERIFICATION
  using var instance = new SingleInstance(args.Any(a=>a.StartsWith("--verify-",StringComparison.Ordinal))?".Verification":"");
#else
  using var instance = new SingleInstance();
#endif
  if (!instance.IsFirst) { instance.ActivateExisting(); return 0; }
  Arguments=args;
  try{return AppBuilder.Configure<PlayerApp>().UsePlatformDetect().LogToTrace().StartWithClassicDesktopLifetime(args);}
  catch(Exception e){var folder=Option("--verify-phase5");if(folder!=null){Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"startup-fatal.txt"),e.ToString());}return 1;}
 }
}
public sealed class PlayerApp : Application
{
 public override void Initialize()
 {
  var theme=new FluentTheme();theme.Palettes[ThemeVariant.Dark]=new ColorPaletteResources{Accent=global::Avalonia.Media.Color.Parse("#55B2F6")};theme.Palettes[ThemeVariant.Light]=new ColorPaletteResources{Accent=global::Avalonia.Media.Color.Parse("#368DCD")};Styles.Add(theme);
  Styles.Add(new StyleInclude(new Uri("avares://LTCue/")){Source=new Uri("avares://Avalonia.Controls.DataGrid/Themes/Fluent.xaml")});
  RequestedThemeVariant=ThemeVariant.Dark;
 }
 public override void OnFrameworkInitializationCompleted()
 {
  if(ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d)d.MainWindow=new MainWindow();
  base.OnFrameworkInitializationCompleted();
 }
}
