namespace LtcPlayer.Avalonia;

public readonly record struct LtcInputLevelSnapshot(bool HasSamples,float Peak,double Dbfs,bool NearLimit);
// Packet peaks are already calculated by the capture path; no extra audio pass or device.
public sealed class LtcInputLevel
{
 readonly object gate=new();
 float held;
 double last=double.NegativeInfinity,heldAt;
 public void Push(float peak,double now){if(!float.IsFinite(peak)||peak<0||!double.IsFinite(now))return;lock(gate){if(now-last>.3||peak>=held||now-heldAt>=.5){held=peak;heldAt=now;}last=now;}}
 public void Reset(){lock(gate){held=0;last=double.NegativeInfinity;}}
 public LtcInputLevelSnapshot Read(double now){lock(gate){bool valid=now-last<=.3;float peak=valid?held:0;return new(valid,peak,peak>0?20*Math.Log10(peak):double.NegativeInfinity,valid&&peak>=.99f);}}
}
