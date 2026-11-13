using System.Diagnostics;
using System.Text.Json;
using LtcPlayer.App;
namespace LtcPlayer.Avalonia;
public static class Phase5Verification
{
 static void Assert(bool value,string message){if(!value)throw new InvalidDataException(message);}
 static async Task Until(Func<bool> check,int milliseconds=5000)
 {
  var clock=Stopwatch.StartNew();while(!check()){if(clock.ElapsedMilliseconds>milliseconds)throw new TimeoutException("condition timeout");await Task.Delay(15);}
 }
 public static async Task RunAsync(MainWindow window,string folder)
 {
  Directory.CreateDirectory(folder);var results=new List<object>();bool failed=false;
  var player=window.Preview;string fixtures=Program.Option("--fixtures")??Path.Combine(folder,"fixtures");
  string aac=Path.Combine(fixtures,"h264-aac.mp4"),pcm=Path.Combine(fixtures,"hap-pcm.mov"),silent=Path.Combine(fixtures,"hap-q-silent.mov");
  async Task Test(string name,Func<Task<object>> action)
  {
   try{results.Add(new{test=name,status="PASS",detail=await action()});}
   catch(Exception e){failed=true;results.Add(new{test=name,status="FAIL",detail=e.ToString()});}
   finally{await window.DisarmAsync();await window.StopSyncPreviewAsync();await window.Monitor.ConfigureAsync(new());await player.SetArmedAsync(false);await player.StopAsync();}
   await File.WriteAllTextAsync(Path.Combine(folder,"results.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
  }
  try {
   await Test("Avalonia editor import/clone/TC conflict/reorder/JSON",async()=>{
    var editor=new ShowEditor();await editor.ImportAsync([aac,pcm,silent],"00:00:00:00");Assert(!editor.CanArm,"same TC must conflict");
    editor.Rows[1].Seconds="02";editor.Rows[2].Seconds="03";editor.Validate();Assert(editor.CanArm,"distinct TC should ARM");
    var a=editor.Rows[0];var clone=editor.Clone(a);Assert(!clone.Enabled&&clone.CueId!=a.CueId,"disabled clone");
    var before=new CueScheduler(ShowProgramService.Validate(editor.Rows,editor.Rate));editor.Move(a,3);
    var after=new CueScheduler(ShowProgramService.Validate(editor.Rows,editor.Rate));foreach(double t in new[]{0.5,2.5,3.5,5.5})Assert(before.Select(t)?.CueId==after.Select(t)?.CueId,"reorder changed schedule");
    var ids=editor.Rows.Select(r=>r.CueId).ToArray();string json=Path.Combine(folder,"editor-show.json");await editor.SaveAsync(json);int notifications=0;editor.Changed+=()=>notifications++;await editor.LoadAsync(json);Assert(notifications==1,"bulk load repeated validation notifications");Assert(ids.SequenceEqual(editor.Rows.Select(r=>r.CueId)),"JSON identity/order");
    editor.Locked=true;bool rejected=false;try{editor.Delete(editor.Rows[0]);}catch(InvalidOperationException){rejected=true;}Assert(rejected,"locked editor allows delete");
    return new{Rows=editor.Rows.Count,ReorderedWithoutScheduleChange=true,LockedMutationRejected=rejected};
   });
   await Test("AAC playback/audio clock/pause/resume",async()=>{
    player.SetVolume(.15,false);await player.LoadAsync(aac);await player.PlayAsync();await Until(()=>player.Snapshot.Position>.35);
    Assert(player.Snapshot.Audio is {Consumed:>0},"WASAPI did not consume AAC decoded audio");await player.PauseAsync();var p=player.Snapshot;await Task.Delay(220);var q=player.Snapshot;
    Assert(Math.Abs(p.Position-q.Position)<.001&&p.Presented==q.Presented,"paused clock/present changed");
    await player.PlayAsync();await Until(()=>player.Snapshot.Position>p.Position+.2);return new{PausedAt=p.Position,ResumedAt=player.Snapshot.Position,player.Snapshot.Audio};
   });
   await Test("seek while paused then playing / fresh audio epoch",async()=>{
    await player.LoadAsync(aac);await player.SeekAsync(2.21);var paused=player.Snapshot;Assert(paused.State==PreviewState.Paused&&paused.FramePts>=2.21&&paused.FramePts<2.26,"seek frame");
    await player.PlayAsync();await Until(()=>player.Snapshot.Position>2.4);await player.SeekAsync(4.05);await Until(()=>player.Snapshot.Position>4.2);
    Assert(player.Snapshot.Position<4.8&&player.Snapshot.Audio!.Seconds<.8,"old audio epoch leaked after seek");
    return new{PausedSeek=paused,PlayingSeek=player.Snapshot};
   });
   await Test("volume/mute applied to WASAPI submitted samples",async()=>{
    await player.LoadAsync(aac);player.SetVolume(.25,false);await player.PlayAsync();await Until(()=>player.Snapshot.Audio is {Consumed:>5000});float quarter=player.Snapshot.Audio!.Peak;
    player.SetVolume(1,false);await Until(()=>player.Snapshot.Audio!.Peak>quarter*3);float full=player.Snapshot.Audio!.Peak;
    player.SetVolume(1,true);await Until(()=>player.Snapshot.Audio!.Peak==0);float mute=player.Snapshot.Audio!.Peak;
    Assert(quarter>0&&full>quarter*3&&mute==0,"gain/mute samples");player.SetVolume(.15,false);
    return new{QuarterPeak=quarter,FullPeak=full,MutedPeak=mute,Measurement="post-gain software samples submitted to WASAPI; not acoustic measurement"};
   });
   await Test("HAP/PCM video and audio pause",async()=>{
    await player.LoadAsync(pcm);await player.PlayAsync();await Until(()=>player.Snapshot.Position>.3);Assert(player.Snapshot.Audio is {Consumed:>0},"PCM not consumed");await player.PauseAsync();var a=player.Snapshot;
    await Task.Delay(150);Assert(Math.Abs(a.Position-player.Snapshot.Position)<.001,"PCM pause changed");RegressionWindow.SavePng(Path.Combine(folder,"hap-pcm-preview.png"),window.Surface.Readback(),window.Surface.PixelSize.Width,window.Surface.PixelSize.Height);
    return a;
   });
   await Test("HAP Q silent preview natural end",async()=>{
    await player.LoadAsync(silent);await player.PlayAsync();await Until(()=>player.Snapshot.State==PreviewState.Ended);Assert(player.Snapshot.Audio==null&&PreviewAudio.ActiveStreams==0,"silent preview opened audio");
    var s=window.Surface.PixelSize;RegressionWindow.SavePng(Path.Combine(folder,"hap-q-preview.png"),window.Surface.Readback(),s.Width,s.Height);return player.Snapshot;
   });
   await Test("unsupported preview audio is explicit video-only",async()=>{
    await player.LoadAsync(Path.Combine(fixtures,"h264-mp3.mp4"));await player.PlayAsync();await Until(()=>player.Snapshot.Position>.2);
    Assert(player.Snapshot.Audio==null&&player.Snapshot.Notice.Contains("対象外"),"unsupported audio was not explicit");return player.Snapshot;
   });
   await Test("stop holds frame and closes audio resources",async()=>{
    await player.LoadAsync(aac);await player.PlayAsync();await Until(()=>player.Snapshot.Position>.2);await player.StopAsync();
    var pixels=window.Surface.Readback();Assert(pixels.Where((_,i)=>i%4!=3).Any(x=>x>0),"stop lost held image");Assert(player.Snapshot.Position>0&&player.Snapshot.State==PreviewState.Stopped&&PreviewAudio.ActiveStreams==0,"stop leaked audio or position");
    return new{player.Snapshot,ActiveAudioStreams=PreviewAudio.ActiveStreams,Holding=true};
   });
   await Test("playing resize preserves audio/video session",async()=>{
    await player.LoadAsync(aac);await player.PlayAsync();await Until(()=>player.Snapshot.Position>.2);var before=player.Snapshot;
    window.Width=1250;window.Height=850;await Task.Delay(220);window.Width=700;window.Height=1100;await Task.Delay(220);window.Width=1500;window.Height=940;await Task.Delay(220);
    Assert(player.Snapshot.State==PreviewState.Playing&&player.Snapshot.Position>before.Position+.2&&PreviewAudio.ActiveStreams==1,"resize broke playback");return player.Snapshot;
   });
   await Test("failed load keeps last valid media and closes audio",async()=>{
    await player.LoadAsync(aac);await player.PlayAsync();await Until(()=>player.Snapshot.Position>.1);bool missing=false;
    try{await player.LoadAsync(Path.Combine(fixtures,"missing.mp4"));}catch(FileNotFoundException){missing=true;}
    Assert(missing&&player.Snapshot.State==PreviewState.Paused&&player.LoadedPath==aac&&PreviewAudio.ActiveStreams==0,"failed load lost valid media or retained audio");
    return new{MissingRejected=missing,State=player.Snapshot.State,AudioStreams=PreviewAudio.ActiveStreams};
   });
   await Test("ARM cancels audio; service guards; synthetic production renders",async()=>{
    var row=new CueRow{FilePath=aac};row.SetStart("00:00:00:00");await ShowProgramService.RefreshAsync(row,CancellationToken.None);window.Editor.Add(row);
    await player.LoadAsync(aac);await player.PlayAsync();await Until(()=>player.Snapshot.Position>.2);
    await window.ArmAsync(async(clock,token)=>{int i=0;while(!token.IsCancellationRequested){clock.Accept(new TcFrame{Seconds=(i/25)%60,Frame=i%25},MasterClock.Timestamp());i++;await Task.Delay(40,token);}});
    try {
     await Until(()=>player.Armed&&window.LatestObservation is {FramePts:>0});Assert(window.IsArmed&&player.Armed&&PreviewAudio.ActiveStreams==0,"ARM did not terminate preview audio");
     int blocked=0;foreach(var action in new Func<Task>[]{player.PlayAsync,()=>player.LoadAsync(pcm),()=>player.SeekAsync(1)})try{await action();}catch(InvalidOperationException){blocked++;}
     Assert(blocked==3,"preview command bypassed ARM");await Until(()=>window.LatestObservation is {Presented:>5,FramePts:>0});Assert(window.IsArmed,"production unexpectedly stopped");return new{BlockedCommands=blocked,AudioStreams=PreviewAudio.ActiveStreams,SyntheticInput=true,Observation=window.LatestObservation};
    }finally{await window.DisarmAsync();}
   });
   await Test("stop during ARM preparation prevents late startup",async()=>{
    var prepare=window.ArmAsync(async(clock,token)=>await Task.Delay(Timeout.Infinite,token));
    await Task.Delay(1);await window.DisarmAsync();bool cancelled=false;try{await prepare;}catch(OperationCanceledException){cancelled=true;}
    Assert(!window.IsArmed&&!player.Armed&&PreviewAudio.ActiveStreams==0,"late ARM startup or preview lock retained");
    return new{PreparationCancelled=cancelled,window.IsArmed,player.Armed};
   });
   if(Program.Arguments.Contains("--regression")) {
    var adapter=new RegressionWindow();
    foreach(int phase in new[]{1,2,3,4})await Test("Avalonia port Phase "+phase+" regression",async()=>{
     string sub=Path.Combine(folder,"phase"+phase+"-regression");
     if(phase==1)await Phase1Verification.RunAsync(adapter,Path.Combine(sub,"media.mp4"),sub);
     if(phase==2)await Phase2Verification.RunAsync(adapter,sub);
     if(phase==3)await Phase3Verification.RunAsync(adapter,sub);
     if(phase==4)await Phase4Verification.RunAsync(adapter,sub);
     Assert(File.ReadAllText(Path.Combine(sub,"exit-code.txt")).Trim()=="0","regression failed: "+sub);
     using var json=JsonDocument.Parse(File.ReadAllText(Path.Combine(sub,"results.json")));return new{Tests=json.RootElement.GetArrayLength(),AllPassed=true};
    });
   }
  }catch(Exception e){failed=true;File.WriteAllText(Path.Combine(folder,"fatal.txt"),e.ToString());}
  finally{await window.DisarmAsync();await player.StopAsync();File.WriteAllText(Path.Combine(folder,"exit-code.txt"),failed?"1":"0");MainWindow.Shutdown(failed?1:0);}
 }
}
