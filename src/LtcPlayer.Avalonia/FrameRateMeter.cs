namespace LtcPlayer.Avalonia;

// Distinct application submissions, sampled at most once per second.
internal sealed class FrameRateMeter
{
    readonly Queue<(double Time,long Frames)> samples=new();
    string? source;
    double expected,lowSince=double.NaN;
    double? rate;
    public bool IsLow{get;private set;}
    public void Reset(){samples.Clear();source=null;expected=0;lowSince=double.NaN;rate=null;IsLow=false;}
    public double? Read(string identity,double time,long frames,double sourceFps=0)
    {
        if(!double.IsFinite(time)||frames<0){Reset();return null;}
        double reference=double.IsFinite(sourceFps)&&sourceFps>0?sourceFps:0;
        if(source!=identity||expected!=reference||(samples.Count>0&&(time<samples.Last().Time||frames<samples.Last().Frames))){Reset();source=identity;expected=reference;}
        if(samples.Count>0&&time-samples.Last().Time<1-1e-9)return rate;
        samples.Enqueue((time,frames));
        // Two recent sampling intervals, normally about two seconds.
        while(samples.Count>3)samples.Dequeue();
        var first=samples.Peek();var last=samples.Last();double elapsed=last.Time-first.Time;
        rate=elapsed>=1-1e-9?(last.Frames-first.Frames)/elapsed:null;
        if(rate.HasValue&&expected>0&&rate.Value<expected*.9){
            if(double.IsNaN(lowSince))lowSince=time;
            IsLow=time-lowSince>=2-1e-9;
        }else{lowSince=double.NaN;IsLow=false;}
        return rate;
    }
}
