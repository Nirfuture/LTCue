namespace LtcPlayer.App;
public sealed record CoverImage(byte[] Pixels,int Width,int Height,string Path);

public sealed class CoverController
{
 readonly object gate=new();double from,to,started,duration;CoverImage? image;
 public CoverImage? Image{get=>Volatile.Read(ref image);set=>Volatile.Write(ref image,value);}
 public bool IsCovered{get{lock(gate)return to==1;}}
 public double Amount(double? at=null){lock(gate)return Value(at??MasterClock.Timestamp());}
 double Value(double at)=>duration<=0?to:from+(to-from)*Math.Clamp((at-started)/duration,0,1);
 public void Set(bool covered,double seconds,double? at=null){if(!double.IsFinite(seconds)||seconds<0||seconds>60)throw new ArgumentOutOfRangeException(nameof(seconds));lock(gate){double now=at??MasterClock.Timestamp();from=Value(now);to=covered?1:0;started=now;duration=seconds;}}
 public bool Transitioning=>Math.Abs(Amount()-(IsCovered?1:0))>.00001;
}
public interface ICoverRenderer
{
 void SetCover(CoverImage? image);
 void SetCoverMix(float amount);
 void Redraw();
}
public sealed class CoverBinding
{
 CoverImage? image;bool initialized;float amount=-1;
 public bool Apply(CoverController controller,ICoverRenderer renderer){
  bool changed=false;var next=controller.Image;if(!initialized||!ReferenceEquals(next,image)){renderer.SetCover(next);image=next;initialized=true;changed=true;}
  float mix=(float)controller.Amount();if(amount!=mix){renderer.SetCoverMix(mix);amount=mix;changed=true;}return changed;
 }
}
