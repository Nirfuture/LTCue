namespace LtcPlayer.App;

public enum TimelineExecutionState { Idle, Active, Failed }
public sealed record TimelineFailure(ScheduledCue Cue, string Reason){public Guid SessionId{get;init;}}

// The only owner of LTC source selection, shared by video and the audio clock reader.
// No IO, UI calls or task waits under this gate. Published cue arrays are immutable.
public sealed class PlaybackTimeline
{
    readonly object gate = new();
    ScheduledCue[] cues = [];
    readonly Dictionary<Guid, ScheduledCue> failed = new();
    ScheduledCue? execution;
    TimelineExecutionState state;
    int clockGeneration = -1;
    public event Action<TimelineFailure>? Failed;

    public void Update(IEnumerable<ScheduledCue> source)
    {
        var next = source.OrderBy(c => c.Start).ToArray();
        lock (gate)
        {
            cues = next;
            foreach (var id in failed.Keys.ToArray())
                if (!next.Any(c => c.CueId == id && SameSource(c, failed[id]))) failed.Remove(id);
            if (execution != null && !next.Any(c => SameSource(c, execution) && c.Start == execution.Start && c.Duration == execution.Duration))
            { execution = null; state = TimelineExecutionState.Idle; }
        }
    }
    public static bool SameSource(ScheduledCue a, ScheduledCue b) =>
        a.CueId == b.CueId && a.FilePath == b.FilePath && a.SourceRevision == b.SourceRevision;
    bool Available(ScheduledCue c) => c.CanStart && !failed.ContainsKey(c.CueId);

    public ScheduledCue? Select(double seconds, int generation)
    {
        lock (gate)
        {
            if (clockGeneration != generation)
            { clockGeneration = generation; execution = null; state = TimelineExecutionState.Idle; }
            var candidate = cues.LastOrDefault(c => c.Start <= seconds && Available(c));
            // A failed running cue remains the current execution until a later cue,
            // an explicit clock jump, or a verified source replacement supersedes it.
            if (execution != null && state == TimelineExecutionState.Failed &&
                (candidate == null || candidate.Start <= execution.Start)) return null;
            // A path may disappear while an already-open decoder still works.
            if (execution != null && state == TimelineExecutionState.Active &&
                cues.Any(c => c.CueId == execution.CueId && SameSource(c, execution)) &&
                seconds >= execution.Start && seconds < execution.Start + execution.Duration &&
                (candidate == null || candidate.Start < execution.Start)) return execution;
            if (candidate == null || seconds >= candidate.Start + candidate.Duration)
            { if (state != TimelineExecutionState.Failed) { execution = null; state = TimelineExecutionState.Idle; } return null; }
            execution = candidate; state = TimelineExecutionState.Active;
            return candidate;
        }
    }
    public ScheduledCue? Next(double seconds)
    { lock (gate) return cues.FirstOrDefault(c => c.Start > seconds && Available(c)); }

    public void Fail(ScheduledCue cue, Exception error)
    {
        bool publish;
        lock (gate)
        {
            if (!cues.Any(c => SameSource(c, cue)) || failed.ContainsKey(cue.CueId)) return;
            failed[cue.CueId] = cue;
            if (execution != null && SameSource(execution, cue)) state = TimelineExecutionState.Failed;
            publish = true;
        }
        if (publish) Failed?.Invoke(new(cue, error.Message));
    }
}
