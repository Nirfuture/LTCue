namespace LtcPlayer.App;

public readonly record struct TimingStatus(string Text,bool Warning=false);

// Display-only policy. Positive error means playback is behind the common clock.
public sealed class PlaybackTimingStatus
{
 public const double PersistenceSeconds=.3;
 string identity="";int direction;double changedAt;bool warned;
 public TimingStatus Read(string source,double? error,double tolerance,double now,string? inactive=null)
 {
  if(source!=identity){identity=source;direction=0;warned=false;changedAt=now;}
  if(inactive!=null||error==null||!double.IsFinite(error.Value)){
   direction=0;warned=false;changedAt=now;return new(inactive??"確認中");
  }
  int next=Math.Abs(error.Value)>tolerance?Math.Sign(error.Value):0;
  if(next!=direction){if(next!=0)warned=false;direction=next;changedAt=now;}
  if(now-changedAt>=PersistenceSeconds)warned=direction!=0;
  if(warned&&direction!=0)return new($"{Math.Round(Math.Abs(error.Value)*100)*10:0}ms{(direction>0?"遅れ":"先行")}",true);
  // A new out-of-range sample must not be called delay-free while being confirmed.
  return new(direction!=0||warned?"確認中":"遅延なし");
 }
}
