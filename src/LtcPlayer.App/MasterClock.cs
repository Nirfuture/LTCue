using System.Diagnostics;
namespace LtcPlayer.App;
public record TimecodeRate(string Name, int Nominal, double Fps, bool Drop)
{
    public static readonly TimecodeRate[] Modes = [new("24",24,24,false),new("25",25,25,false),new("29.97 NDF",30,30000.0/1001,false),new("29.97 DF",30,30000.0/1001,true),new("30",30,30,false)];
    public override string ToString() => Name;
    public long Frames(int h,int m,int s,int f)
    {
        if(h<0 || h>23 || m<0 || m>59 || s<0 || s>59 || f<0 || f>=Nominal || (Drop && m%10!=0 && s==0 && f<2)) throw new InvalidDataException("無効なタイムコードです。");
        return ((h*3600L+m*60+s)*Nominal+f)-(Drop?2*(h*60+m-(h*60+m)/10):0);
    }
    public double Parse(string value)
    {
        var parts=value.Split(':',';'); if(parts.Length!=4) throw new InvalidDataException("TCはHH:MM:SS:FFで入力してください。");
        var p=parts.Select(int.Parse).ToArray(); return Frames(p[0],p[1],p[2],p[3])/Fps;
    }
    public double FramesToSeconds(int frames)=>frames/Fps;
    public double DaySeconds => ((24*3600L*Nominal)-(Drop?2592:0))/Fps;
}
public enum SyncState { WAITING, LOCKED, FREEWHEEL, LOST, RESYNC, ERROR }
public record ClockSnapshot(SyncState State,double Seconds,int Generation,string Label);
public sealed class MasterClock
{
    private readonly object gate=new(); private readonly TimecodeRate rate;
    private bool received,error; private double anchor,stamp,day; private int generation; private bool resync; private string label="--:--:--:--";
    public double FreewheelSeconds { get; }
    public double ResyncFrames { get; }
    public MasterClock(TimecodeRate rate,double freewheel=0.1,double resyncFrames=2)
    {
        if(!double.IsFinite(freewheel)||freewheel<0 || !double.IsFinite(resyncFrames)||resyncFrames<=0) throw new ArgumentOutOfRangeException();
        this.rate=rate; FreewheelSeconds=freewheel; ResyncFrames=resyncFrames;
    }
    public void Accept(TcFrame tc,double capturedSeconds)
    {
        if(tc.Drop!=(rate.Drop?1:0) || tc.Reverse!=0) throw new InvalidDataException("受信LTCのDF/方向が選択モードと一致しません。");
        // The decoded label names the frame start; off_end timestamps its end.
        var seconds=(rate.Frames(tc.Hours,tc.Minutes,tc.Seconds,tc.Frame)+1)/rate.Fps;
        lock(gate)
        {
            if(received && anchor-day>rate.DaySeconds-2 && seconds<2) day+=rate.DaySeconds;
            seconds+=day;
            bool jump=received && (error || capturedSeconds-stamp>FreewheelSeconds || Math.Abs(seconds-(anchor+capturedSeconds-stamp))>ResyncFrames/rate.Fps);
            if(jump) ++generation;
            resync=jump; anchor=seconds; stamp=capturedSeconds; received=true; error=false;
            label=$"{tc.Hours:00}:{tc.Minutes:00}:{tc.Seconds:00}{(rate.Drop?";":":")}{tc.Frame:00}";
        }
    }
    public void Fault(){lock(gate) error=true;}
    public ClockSnapshot Read(double? now=null)
    {
        lock(gate)
        {
            if(error) return new(SyncState.ERROR,anchor,generation,label);
            if(!received) return new(SyncState.WAITING,0,generation,label);
            double age=Math.Max(0,(now??Timestamp())-stamp);
            var state=age>FreewheelSeconds?SyncState.LOST:age>2/rate.Fps?SyncState.FREEWHEEL:resync?SyncState.RESYNC:SyncState.LOCKED;
            return new(state,anchor+Math.Min(age,FreewheelSeconds),generation,label);
        }
    }
    public static double Timestamp()=>Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency;
}
