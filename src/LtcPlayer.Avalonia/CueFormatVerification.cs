using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using LtcPlayer.App;
namespace LtcPlayer.Avalonia;
public static class CueFormatVerification
{
 public static async Task RunAsync(MainWindow w,string folder)
 {
  Directory.CreateDirectory(folder);var results=new List<object>();bool failed=false;
  void Check(bool ok,string why){if(!ok)throw new IOException(why);}
  async Task Test(string name,Func<Task> action){try{await action();results.Add(new{test=name,status="PASS"});}catch(Exception e){failed=true;results.Add(new{test=name,status="FAIL",detail=e.ToString()});}File.WriteAllText(Path.Combine(folder,"results.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));}
  var flags=BindingFlags.Instance|BindingFlags.NonPublic;
  var settings=typeof(MainWindow).GetField("settings",flags)!;
  var apply=typeof(MainWindow).GetMethod("ApplyCueFormat",flags)!;
  var grid=w.FindControl<DataGrid>("CueGrid")!;
  var column=grid.Columns.Single(c=>Equals(c.Tag,"resolution"));
  async Task Set(CueFormatPreferences p){settings.SetValue(w,((InputSettings)settings.GetValue(w)!) with{CueFormat=p});apply.Invoke(w,null);await Task.Delay(100);}
  void Capture(Control c,string file){using var image=new RenderTargetBitmap(new PixelSize((int)c.Bounds.Width,(int)c.Bounds.Height),new Vector(96,96));image.Render(c);image.Save(Path.Combine(folder,file+".png"));}
  Button Button(Window d,string text)=>d.GetVisualDescendants().OfType<Button>().Single(b=>Equals(b.Content,text));
  void Click(Button b)=>b.RaiseEvent(new RoutedEventArgs(global::Avalonia.Controls.Button.ClickEvent));
  CueFormatDialog? dialog=null;
  try{
   w.Width=1400;w.Height=880;
   var row=new CueRow{FilePath="format-test.mov",Codec="HAP",PixelWidth=1920,PixelHeight=1080};w.Editor.Add(row);
   double shortWidth=0,longWidth=0;
   await Test("registered wide names grow the column and deleting them shrinks it",async()=>{
    await Set(new(){Presets=[new(1920,1080,"HD")]});shortWidth=column.ActualWidth;
    await Set(new(){Presets=[new(1920,1080,"超高精細映像素材")]});longWidth=column.ActualWidth;
    Check(longWidth>shortWidth,"long preset did not grow column");
    var cell=grid.GetVisualDescendants().OfType<TextBlock>().Single(t=>t.Text=="超高精細映像素材");
    var measure=new TextBlock{Text=cell.Text,FontFamily=cell.FontFamily,FontSize=cell.FontSize};measure.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));
    Check(cell.Bounds.Width+1>=measure.DesiredSize.Width,"preset label clipped");Capture(w,"long-preset");
    await Set(new());Check(column.ActualWidth<longWidth,"deleted name retained width");
   });
   await Test("empty presets preserve the header menu and unknown label",async()=>{
    await Set(new(){Presets=[]});Check(row.ResolutionDisplay=="未登録","empty fallback");
    var header=grid.GetVisualDescendants().OfType<DataGridColumnHeader>().Single(h=>Equals(h.Content,"解像度"));
    var button=header.GetVisualDescendants().OfType<Button>().Single();
    var point=button.TranslatePoint(default,header)!.Value;
    Check(button.Bounds.Width>=26&&point.X>=0&&point.X+button.Bounds.Width<=header.Bounds.Width+1,"header menu clipped");Capture(w,"empty-presets");
   });
   await Test("dimensions mode stays fixed and metadata and new rows do not resize",async()=>{
    await Set(new(){Mode=CueResolutionMode.Dimensions});Check(Math.Abs(column.ActualWidth-150)<1,"dimensions width");
    row.PixelWidth=3840;row.PixelHeight=2160;Check(row.ResolutionDisplay=="3840×2160"&&row.ResolutionTooltip=="3840×2160"&&row.ContainerDisplay=="MOV","metadata stale");
    await Task.Delay(50);
    var codecCell=grid.GetVisualDescendants().OfType<TextBlock>().Single(t=>t.Text=="HAP");
    var resolutionCell=grid.GetVisualDescendants().OfType<TextBlock>().Single(t=>t.Text=="3840×2160");
    Check(Equals(ToolTip.GetTip(codecCell),"MOV")&&Equals(ToolTip.GetTip(resolutionCell),"3840×2160"),"column tooltip bindings");
    w.Editor.Add(new(){FilePath="portrait.mov",Codec="HAP",PixelWidth=1080,PixelHeight=1920});await Task.Delay(100);
    Check(w.Editor.Rows.Last().ResolutionDisplay=="1080×1920"&&Math.Abs(column.ActualWidth-150)<1,"new row settings/width");
   });
   await Test("settings dialog fits minimum window and offers only two modes",async()=>{
    dialog=new(new(),_=>{});_=dialog.ShowDialog(w);await Task.Delay(150);dialog.Width=560;dialog.Height=420;await Task.Delay(150);
    var combo=dialog.GetVisualDescendants().OfType<ComboBox>().Single();Check(combo.ItemCount==2,"mode count");
    foreach(var b in dialog.GetVisualDescendants().OfType<Button>().Where(b=>b.Content is string)){
     var p=b.TranslatePoint(default,dialog)!.Value;Check(p.X>=0&&p.Y>=0&&p.X+b.Bounds.Width<=dialog.ClientSize.Width+1&&p.Y+b.Bounds.Height<=dialog.ClientSize.Height+1,"button outside dialog: "+b.Content);
    }
    Capture(dialog,"dialog-minimum");Click(Button(dialog,"キャンセル"));dialog=null;
   });
   await Test("invalid input and save failures retain draft without applying",async()=>{
    int saves=0;dialog=new(new(),_=>{saves++;throw new IOException("保存失敗テスト");});_=dialog.ShowDialog(w);await Task.Delay(100);
    Click(Button(dialog,"プリセットを追加"));await Task.Delay(100);Click(Button(dialog,"適用"));Check(saves==0&&dialog.IsVisible,"invalid input saved/closed");
    Click(dialog.GetVisualDescendants().OfType<Button>().Where(b=>Equals(b.Content,"削除")).Last());Click(Button(dialog,"適用"));
    Check(saves==1&&dialog.IsVisible,"save error closed dialog");Capture(dialog,"save-error");dialog.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Key=Key.Escape});Check(!dialog.IsVisible,"Escape did not close");dialog=null;
   });
   await Test("apply saves trimmed preset and column width survives settings reload",async()=>{
    var prefs=new CueFormatPreferences{Presets=[new(3840,2160,"超高精細映像素材")]};
    string file=Path.Combine(folder,"roundtrip-settings.json");new InputSettings{CueFormat=prefs}.Save(file);await Set(InputSettings.Load(file).CueFormat);
    double before=column.ActualWidth;await Set(new());await Set(InputSettings.Load(file).CueFormat);Check(Math.Abs(before-column.ActualWidth)<1,"restored width changed");
    CueFormatPreferences? saved=null;dialog=new(prefs,p=>saved=p);_=dialog.ShowDialog(w);await Task.Delay(100);
    var name=dialog.GetVisualDescendants().OfType<TextBox>().Single(t=>t.Text=="超高精細映像素材");name.Text=" LED ";Click(Button(dialog,"適用"));
    Check(saved?.Presets.Single().Name=="LED"&&!dialog.IsVisible,"apply did not trim/save/close");dialog=null;
   });
  }finally{dialog?.Close();await w.Monitor.DisposeAsync();MainWindow.Shutdown(failed?1:0);}
 }
}
