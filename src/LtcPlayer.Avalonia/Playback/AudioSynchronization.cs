namespace LtcPlayer.App;

public enum AudioSyncAction { Silence,KeepPreparing,AdjustRate,Reseek }
public readonly record struct AudioSyncDecision(AudioSyncAction Action,float RateRatio,double TargetPts,double ErrorSeconds);

// Shared audio follower policy; video/LTC remains the time authority.
public sealed class AudioSynchronizationPolicy
{
    public const double DeadbandSeconds=.002;
    public const double ReseekThresholdSeconds=.100;
    public const float MaximumRateCorrection=.005f;
    double nextRecovery;
    public AudioSyncDecision Evaluate(double targetPts,double playedPts,double hostTime,bool advancing,bool preparing=false)
    {
        if(!double.IsFinite(targetPts)||!double.IsFinite(playedPts)||!double.IsFinite(hostTime))throw new ArgumentOutOfRangeException();
        double error=targetPts-playedPts;
        if(!advancing)return new(AudioSyncAction.Silence,1,targetPts,error);
        if(preparing)return new(AudioSyncAction.KeepPreparing,1,targetPts,error);
        if(Math.Abs(error)>ReseekThresholdSeconds&&hostTime>=nextRecovery)
        {
            nextRecovery=hostTime+1;
            return new(AudioSyncAction.Reseek,1,targetPts,error);
        }
        float ratio=Math.Abs(error)<=DeadbandSeconds?1:(float)Math.Clamp(1+error*.5,1-MaximumRateCorrection,1+MaximumRateCorrection);
        return new(AudioSyncAction.AdjustRate,ratio,targetPts,error);
    }
}
