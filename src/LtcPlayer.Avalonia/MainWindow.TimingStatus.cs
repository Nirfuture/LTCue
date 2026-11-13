using Avalonia.Controls;
using Avalonia.Media;
using LtcPlayer.App;
namespace LtcPlayer.Avalonia;
public partial class MainWindow
{
 readonly PlaybackTimingStatus videoTiming=new(),audioTiming=new();
 void UpdateTimingStatus()
 {
  double now=MasterClock.Timestamp();var input=monitor.Clock.Read(now);var p=Preview.Snapshot;var a=SharedAudio;
  string? inactive=null;double? error=null;string identity;double fps=Preview.FrameRate;
  if(SyncOwnsSurface){
   var o=playback.Observation;var cue=o?.ScheduledCue;
   identity=$"ltc/{playback.SynchronizationSession}/{input.Generation}/{cue?.CueId}";
   fps=cue?.FrameRate??0;
   inactive=input.State switch{SyncState.ERROR=>"入力エラー",SyncState.LOST=>"入力待ち",SyncState.WAITING=>"待機中",_=>null};
   if(inactive==null){
    if(o==null||o.InputGeneration!=input.Generation||now-o.HostSeconds>.5)inactive="確認中";
    else if(cue==null)inactive="待機中";
    else if(o.Recovering)inactive="再同期中";
    else if(o.CueId!=cue.CueId||o.FramePts==null)inactive="準備中";
    else error=input.Seconds+Editor.Rate.FramesToSeconds(settings.Offset)-cue.Start-o.FramePts.Value;
   }
  }else{
   identity=$"manual/{Preview.PlaybackSessionId}/{Preview.SourceGeneration}";
   inactive=Preview.IsBusy?"準備中":p.State switch{PreviewState.Playing=>null,PreviewState.Paused=>"一時停止",PreviewState.Ended=>"終了",PreviewState.Error=>"エラー",PreviewState.Ready=>"待機中",_=>"停止中"};
   if(inactive==null)error=p.FramePts is {} frame?Preview.PositionForAudio-frame:null;
  }
  if(Surface.Ready&&Surface.Canvas.SharedDeviceError<0)inactive="描画エラー";
  SetTimingText("VideoTimingStatus","映像",videoTiming.Read(identity,error,Math.Max(.05,fps>0?1.5/fps:.05),now,inactive));
  string? audioState=!audioRequested?"OFF":a.State switch{
   SharedAudioState.Off=>"OFF",SharedAudioState.Preparing=>"準備中",SharedAudioState.Resyncing=>"再同期中",SharedAudioState.NoAudio=>"音声なし／対象外",SharedAudioState.Error=>"エラー",
   SharedAudioState.Waiting=>p.State==PreviewState.Paused&&!SyncOwnsSurface?"一時停止":"待機中",_=>null};
  var head=playback.ReadAudioPlayhead();
  if(audioState==null&&(now-a.HostSeconds>.5||!head.Usable(now)||head.CueId!=a.CueId||head.Generation!=a.Generation))audioState="確認中";
  SetTimingText("AudioTimingStatus","音声",audioTiming.Read($"{head.SessionId}/{a.CueId}/{a.Generation}",a.ErrorSeconds,.05,now,audioState));
 }
 void SetTimingText(string name,string label,TimingStatus status){
  var text=Control<TextBlock>(name);text.Text=$"{label}：{status.Text}";
  text.Foreground=Brush.Parse(status.Warning?"#E6B566":"#A5B0BF");
  ToolTip.SetTip(text,"共通時計との差。許容幅内は遅延なし、ずれが0.3秒続くと警告します。");
 }
}
