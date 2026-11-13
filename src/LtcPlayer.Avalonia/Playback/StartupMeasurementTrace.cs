using System.Diagnostics;
namespace LtcPlayer.App;

// Opt-in, single sequential trial at a time. No file I/O on playback threads.
// Calls disappear from ordinary builds. The harness writes the bounded snapshot.
internal static class StartupMeasurementTrace
{
 internal sealed record Entry(string Trial,string Event,double Seconds,double? Value,string? Note);
 static readonly object gate=new();static readonly List<Entry> entries=[];static readonly HashSet<string> once=[];
 static string trial="";static bool enabled;
 internal static void Begin(string id){lock(gate){enabled=true;trial=id;}}
 [Conditional("VERIFICATION")]
 internal static void Mark(string name,double? value=null,string? note=null){lock(gate){if(enabled&&entries.Count<512)entries.Add(new(trial,name,MasterClock.Timestamp(),value,note));}}
 [Conditional("VERIFICATION")]
 internal static void Once(string name,double? value=null,string? note=null){lock(gate){if(enabled&&entries.Count<512&&once.Add(trial+"/"+name))entries.Add(new(trial,name,MasterClock.Timestamp(),value,note));}}
 internal static Entry[] Snapshot(){lock(gate)return entries.ToArray();}
 internal static void End(){lock(gate)enabled=false;}
}
