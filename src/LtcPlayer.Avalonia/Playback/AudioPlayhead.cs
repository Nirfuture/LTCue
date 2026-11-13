namespace LtcPlayer.App;

// A source-owned clock advances until an explicit pause/stop/fault/end invalidates it.
// Observation-only playheads expire after 250ms. Reading never renews their timestamp.
public sealed record AudioPlayhead(Guid SessionId,Guid CueId,int Generation,string Path,double Position,double Duration,bool Advancing,double HostSeconds,bool LtcDriven=false,string Notice="",bool SourceOwnsClock=false)
{
    // Preparation grants decode permission only; it never grants output permission.
    public bool PreparationRequested{get;init;}
    [System.Text.Json.Serialization.JsonIgnore]
    public CancellationToken PreparationCancellation{get;init;}
    public bool CanPrepare(double now)=>Usable(now)||(PreparationRequested&&!PreparationCancellation.IsCancellationRequested&&SessionId!=Guid.Empty&&CueId!=Guid.Empty&&Path!=""&&Duration>0&&(SourceOwnsClock||now-HostSeconds<.250));
    public static AudioPlayhead Empty{get;}=new(Guid.Empty,Guid.Empty,0,"",0,0,false,0);
    public double Estimate(double now)=>Math.Clamp(Position+(Advancing?Math.Max(0,now-HostSeconds):0),0,Math.Max(0,Duration));
    public bool Usable(double now)=>SessionId!=Guid.Empty&&CueId!=Guid.Empty&&Path!=""&&Duration>0&&Advancing&&(SourceOwnsClock||now-HostSeconds<.250);
}
