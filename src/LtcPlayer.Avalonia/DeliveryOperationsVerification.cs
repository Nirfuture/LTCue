using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using LtcPlayer.App;
namespace LtcPlayer.Avalonia;
public static class DeliveryOperationsVerification
{
 static void Check(bool value,string message){if(!value)throw new IOException(message);}
 public static async Task RunAsync(MainWindow w,string folder)
 {
  Directory.CreateDirectory(folder);var results=new List<object>();bool failed=false;var button=w.FindControl<Button>("ArmButton")!;var hint=w.FindControl<TextBlock>("DeliveryHint")!;
  async Task Test(string name,Func<Task<object>> body){try{results.Add(new{test=name,status="PASS",detail=await body()});}catch(Exception e){failed=true;results.Add(new{test=name,status="FAIL",detail=e.ToString()});}File.WriteAllText(Path.Combine(folder,"results.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));}
  async Task Until(Func<bool> condition){for(int i=0;i<500;i++){if(condition())return;await Task.Delay(10);}throw new TimeoutException($"{w.Delivery} button={button.IsEnabled} reason={w.DeliveryBlockReason()}");}
  void Click()=>button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
  object State()=>new{Button=button.Content,Enabled=button.IsEnabled,Reason=hint.Text,Hint=hint.IsVisible,Delivery=w.Delivery.ToString()};
  string media=Program.Option("--media")??throw new IOException("--media missing");var rows=new List<CueRow>();
  try{
   await Test("empty queue / missing display gives actionable reason",async()=>{
    Check(!button.IsEnabled&&hint.IsVisible&&hint.Text!.Contains("1件"),"empty allowed or no reason");
    var cue=new CueRow{FilePath=media};await ShowProgramService.RefreshAsync(cue,CancellationToken.None);w.Editor.Add(cue);rows.Add(cue);Check(!button.IsEnabled&&hint.Text!.Contains("映像出力"),"missing saved display allowed");return State();
   });
   await Test("valid single cue / zero default / LTC not required",async()=>{
    await w.ApplySettingsAsync(new(new(Rate:"29.97 DF"),VideoRenderer.Displays().First(d=>d.Primary!=0)));Check(rows[0].StartTimecode=="00:00:00:00","default not zero");Check(button.IsEnabled&&!hint.IsVisible,"valid cue disabled");return State();
   });
   await Test("four zero-time cues disable button with visible conflict reason",async()=>{
    for(int i=1;i<4;i++){var c=new CueRow{FilePath=media};await ShowProgramService.RefreshAsync(c,CancellationToken.None);w.Editor.Add(c);rows.Add(c);}Check(!button.IsEnabled&&hint.IsVisible&&hint.Text!.Contains("重複"),"duplicate not explained");Check(rows.All(r=>r.Error=="同一開始TCの競合"),"row errors absent");File.WriteAllText(Path.Combine(folder,"duplicate-state.json"),JsonSerializer.Serialize(State()));return State();
   });
   await Test("fix time / toggle enabled / invalid TC and DF restore button",()=>{
    for(int i=0;i<4;i++)rows[i].Seconds=(i*15).ToString("00");Check(button.IsEnabled&&!hint.IsVisible,"TC correction not enabled");
    rows[1].Seconds="00";Check(!button.IsEnabled,"new conflict allowed");rows[1].Enabled=false;Check(button.IsEnabled,"disable conflict did not recover");rows[1].Enabled=true;Check(!button.IsEnabled,"reenable conflict allowed");rows[1].Seconds="15";
    rows[0].Hours="99";Check(!button.IsEnabled&&hint.IsVisible,"invalid TC allowed");rows[0].Hours="00";Check(button.IsEnabled,"valid TC not recovered");
    rows[0].Minutes="01";Check(!button.IsEnabled,"DF skipped label allowed");rows[0].Frames="02";Check(button.IsEnabled,"DF valid label blocked");rows[0].Minutes="00";rows[0].Frames="00";return Task.FromResult(State());
   });
   await Test("all disabled blocks / overlap warns but permits",()=>{
    foreach(var c in rows)c.Enabled=false;Check(!button.IsEnabled&&hint.Text!.Contains("1件"),"all disabled allowed");rows[0].Enabled=rows[1].Enabled=true;rows[1].Seconds="05";Check(button.IsEnabled&&rows[0].Warning!="","overlap wrongly blocks");return Task.FromResult(State());
   });
   await Test("JSON reload preserves conflict and recovers when corrected",async()=>{
    string file=Path.Combine(folder,"conflict-show.json");var doc=new ShowProgramDocument(1,"29.97 DF",rows.Select((c,i)=>new SavedCue(c.CueId,c.FilePath,"00:00:00:00",c.Duration,c.FrameRate,c.Codec,true,i)).ToList());await File.WriteAllTextAsync(file,JsonSerializer.Serialize(doc));await w.Editor.LoadAsync(file);rows=w.Editor.Rows.ToList();Check(!button.IsEnabled&&hint.Text!.Contains("重複"),"loaded conflict allowed");for(int i=0;i<rows.Count;i++)rows[i].Seconds=(i*15).ToString("00");Check(button.IsEnabled,"loaded correction not enabled");await w.Editor.SaveAsync(Path.Combine(folder,"valid-show.json"));return State();
   });
   await Test("button click starts without LTC / double click guarded / stream locks preview",async()=>{
    int receiving=0,index=15;await w.Monitor.ConfigureAsync(new(Rate:"29.97 DF"),async(clock,token)=>{while(!token.IsCancellationRequested){if(Volatile.Read(ref receiving)!=0){int f=Interlocked.Increment(ref index)%200;clock.Accept(new TcFrame{Seconds=f/30,Frame=f%30,Drop=1},MasterClock.Timestamp());}await Task.Delay(33,token);}});
    Click();Click();await Until(()=>w.Delivery==DeliveryState.Streaming&&w.LatestObservation!=null);Check(button.IsEnabled&&Equals(button.Content,"出力停止"),"stop button disabled");Check(w.FindControl<DataGrid>("CueGrid")!.IsReadOnly&&w.FindControl<StackPanel>("PreviewControls")!.IsVisible&&!w.FindControl<Slider>("Seek")!.IsEnabled&&w.Surface.ProgramMode,"stream controls unlocked");Check(w.LatestObservation!.State==SyncState.WAITING,"source unexpectedly active");
    Volatile.Write(ref receiving,1);await Until(()=>w.LatestObservation?.FramePts>0&&w.LatestObservation?.Monitor?.Presented>2);return new{State=State(),Observation=w.LatestObservation};
   });
   await Test("stop button cancel preserves output / accept returns ready / restart",async()=>{
    var decision=new TaskCompletionSource<bool>();w.ConfirmStopForTest=_=>decision.Task;int before=w.LatestObservation!.Presented;Click();await Until(()=>w.Delivery==DeliveryState.ConfirmingStop);Check(!button.IsEnabled,"confirmation button not blocked");await Task.Delay(200);Check(w.LatestObservation!.Presented>before,"confirmation halted output");decision.SetResult(false);await Until(()=>w.Delivery==DeliveryState.Streaming&&button.IsEnabled);
    w.ConfirmStopForTest=_=>Task.FromResult(true);Click();await Until(()=>w.Delivery==DeliveryState.Stopped&&button.IsEnabled&&!w.Surface.ProgramMode);Check(!hint.IsVisible,"reason left after stop");Click();await Until(()=>w.LatestObservation?.Presented>2&&w.Delivery==DeliveryState.Streaming);Click();await Until(()=>w.Delivery==DeliveryState.Stopped&&button.IsEnabled);return State();
   });
   await Test("preparation failure unlocks editing and allows retry",async()=>{
    foreach(var c in rows.Skip(1).ToArray())w.Editor.Delete(c);string missing=Path.Combine(folder,"temporarily-missing.mov");w.BeforeOutputAttachForTest=()=>throw new IOException("Synthetic output attach failure");bool rejected=false;try{await w.ArmAsync();}catch(IOException){rejected=true;}finally{w.BeforeOutputAttachForTest=null;}Check(rejected&&!w.IsArmed&&!w.Editor.Locked&&!w.Surface.ProgramMode,"failed preparation left locked");rows[0].FilePath=media;w.Editor.Validate();Check(button.IsEnabled,"retry stayed disabled");await w.ArmAsync();await Until(()=>w.LatestObservation?.Presented>2);await w.DisarmAsync();Check(button.IsEnabled,"retry stop disabled");return State();
   });
  }finally{w.ConfirmStopForTest=null;await w.DisarmAsync();await w.StopSyncPreviewAsync();await w.Coordinator.DisposeAsync();await w.Preview.DisposeAsync();await w.Monitor.DisposeAsync();File.WriteAllText(Path.Combine(folder,"exit-code.txt"),failed?"1":"0");MainWindow.Shutdown(failed?1:0);}
 }
}

