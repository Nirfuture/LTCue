using LtcPlayer.App;
namespace LtcPlayer.Avalonia;

// Enabled only during a bounded diagnostic segment, on the UI thread.
internal static class PresentationTimingVerification
{
    static bool active;
    static readonly List<object> samples=[];
    static readonly List<double> queue=[];
    static double previousVideo;
    internal static double? CurrentQueueDelayMs;
    public static void Start(){samples.Clear();queue.Clear();previousVideo=0;active=true;}
    public static object Finish(){active=false;return new{Samples=samples.ToArray(),QueueDelayMs=queue.ToArray()};}
    public static void QueueDelay(double ms){if(active)queue.Add(ms);}
    public static IDisposable? Measure(RenderSession canvas,string source)=>active?new Measurement(canvas,source):null;
    sealed class Measurement : IDisposable
    {
        readonly RenderSession canvas;readonly string source;readonly double start=MasterClock.Timestamp();
        readonly long composition;readonly (double Composition,double Present) timing;
        readonly double? queueDelay=CurrentQueueDelayMs;
        public Measurement(RenderSession canvas,string source){this.canvas=canvas;this.source=source;composition=canvas.CompositionId;timing=canvas.TimingForTest();}
        public void Dispose(){var end=canvas.TimingForTest();double now=MasterClock.Timestamp();
            double? interval=source=="video"&&previousVideo!=0?(now-previousVideo)*1000:null;
            if(source=="video")previousVideo=now;
            samples.Add(new{Source=source,At=now,QueueDelayMs=queueDelay,DurationMs=(now-start)*1000,CompositionMs=end.Composition-timing.Composition,PresentMs=end.Present-timing.Present,Compositions=canvas.CompositionId-composition,VideoIntervalMs=interval});
        }
    }
}
