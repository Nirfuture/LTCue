using Avalonia.Controls;
using Avalonia.Media;
using LtcPlayer.App;
namespace LtcPlayer.Avalonia;
public partial class MainWindow
{
 readonly PlaybackTimingStatus videoTiming=new(),audioTiming=new();
 static string? LtcTimingPhase(SyncState state)=>state switch{
  SyncState.LOCKED=>null,SyncState.RESYNC=>"再同期中",SyncState.FREEWHEEL=>"補間中",
  SyncState.LOST or SyncState.WAITING=>"入力待ち",SyncState.ERROR=>"入力エラー",_=>"確認中"};
 void UpdateTimingStatus()
 {
  double now=MasterClock.Timestamp();var input=monitor.Clock.Read(now);var p=Preview.Snapshot;var a=SharedAudio;
  string? inactive=null;double? error=null;string identity;double fps=Preview.FrameRate;
  if(SyncOwnsSurface){
   var o=playback.Observation;var cue=o?.ScheduledCue;
   identity=$"ltc/{playback.SynchronizationSession}/{input.Generation}/{cue?.CueId}";
   fps=cue?.FrameRate??0;
   inactive=LtcTimingPhase(input.State);
   if(inactive==null){
    if(o==null||o.InputGeneration!=input.Generation||now-o.HostSeconds>.5)inactive="確認中";
    else if(cue==null)inactive="待機中";
    else if(o.Recovering)inactive="再同期中";
    else if(o.CueId!=cue.CueId||o.FramePts==null)inactive="準備中";
    else error=input.Seconds+Editor.Rate.FramesToSeconds(settings.Offset)-cue.Start-o.FramePts.Value;
   }
  }else{
   identity=$"manual/{Preview.PlaybackSessionId}/{Preview.SourceGeneration}";
   inactive=Preview.IsBusy?"準備中":p.State switch{PreviewState.Playing=>"再生中",PreviewState.Paused=>"一時停止",PreviewState.Ended=>"終了",PreviewState.Error=>"エラー",PreviewState.Ready=>"待機中",_=>"停止中"};
  }
  if(Surface.Ready&&Surface.Canvas.SharedDeviceError<0)inactive="描画エラー";
  SetTimingText("VideoTimingStatus","映像",videoTiming.Read(identity,error,Math.Max(.05,fps>0?1.5/fps:.05),now,inactive),SyncOwnsSurface);
  var head=playback.ReadAudioPlayhead();bool syncAudio=SyncOwnsSurface||head.LtcDriven;
  string? audioState=!audioRequested?"OFF":a.State switch{
   SharedAudioState.Off=>"OFF",SharedAudioState.Preparing=>"準備中",SharedAudioState.Resyncing=>"再同期中",SharedAudioState.NoAudio=>"音声なし／非対応",SharedAudioState.Error=>"エラー",
   SharedAudioState.Waiting=>syncAudio?LtcTimingPhase(input.State)??"待機中":p.State switch{
    PreviewState.Paused=>"一時停止",PreviewState.Stopped=>"停止中",_=>"待機中"},
   _=>null};
  if(audioState==null){
   if(syncAudio&&LtcTimingPhase(input.State) is {} phase)audioState=phase;
   else if(now-a.HostSeconds>.5||!head.Usable(now)||head.CueId!=a.CueId||head.Generation!=a.Generation)audioState="確認中";
   else if(!syncAudio)audioState="再生中";
  }
  SetTimingText("AudioTimingStatus","音声",audioTiming.Read($"{head.SessionId}/{a.CueId}/{a.Generation}",a.ErrorSeconds,.05,now,audioState),syncAudio);
 }
 void SetTimingText(string name,string label,TimingStatus status,bool synchronized){
  var text=Control<TextBlock>(name);text.Text=$"{label}：{status.Text}";
  text.Foreground=(status.Warning?ThemePalette.Warning:ThemePalette.SecondaryText);
  ToolTip.SetTip(text,synchronized?"LTCとの位置差":null);
 }
}
