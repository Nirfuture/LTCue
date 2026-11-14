using Microsoft.VisualBasic.FileIO;

namespace LtcPlayer.App;

public sealed record CuePlacementPreferences(bool AfterExisting = false, decimal GapSeconds = 0)
{
    public void Validate()
    {
        if (GapSeconds < 0 || GapSeconds >= 86400)
            throw new InvalidDataException("動画間隔は0秒以上、24時間未満で入力してください");
    }
}

public sealed record CueTimecodeSource(Guid CueId, string FilePath, int SourceRevision, string Start, double Duration)
{
    public static CueTimecodeSource Capture(CueRow row) =>
        new(row.CueId, row.FilePath, row.SourceRevision, row.StartTimecode, row.Duration);
}

public sealed record TimecodeInput(string Value, long Line);
public sealed record CueTimecodeChange(CueTimecodeSource Source, string Start);

// An immutable proposal. Neither parsing nor previewing edits live rows.
public sealed class CueTimecodePlan
{
    public TimecodeRate Rate { get; }
    public IReadOnlyList<CueTimecodeChange> Changes { get; }
    public bool UsesDuration { get; }
    internal CueTimecodePlan(TimecodeRate rate, IEnumerable<CueTimecodeChange> changes, bool usesDuration)
    {
        Rate = rate;
        Changes = Array.AsReadOnly(changes.ToArray());
        UsesDuration = usesDuration;
    }
}

public static class CueTimecodes
{
    public const int MaximumInputCharacters = 1024 * 1024;

    public static TimecodeInput[] ReadText(string text)
    {
        CheckSize(text);
        var values = new List<TimecodeInput>();
        using var reader = new StringReader(text.TrimStart('\uFEFF'));
        string? line; long number = 0;
        while ((line = reader.ReadLine()) != null)
        {
            number++;
            if (!string.IsNullOrWhiteSpace(line)) Add(values, new(line.Trim(), number));
        }
        return values.ToArray();
    }

    public static TimecodeInput[] ReadCsv(string text)
    {
        CheckSize(text);
        using var reader = new StringReader(text.TrimStart('\uFEFF'));
        using var parser = new TextFieldParser(reader) { HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = true };
        parser.SetDelimiters(",");
        var values = new List<TimecodeInput>();
        bool first = true;
        // TextFieldParser skips blank records internally. Retain a physical-line map for diagnostics.
        var physicalLines = text.TrimStart('\uFEFF').Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        int cursor = 0;
        while (!parser.EndOfData)
        {
            while (cursor < physicalLines.Length && string.IsNullOrWhiteSpace(physicalLines[cursor])) cursor++;
            long number = cursor + 1;
            string[] fields;
            try { fields = parser.ReadFields() ?? []; }
            catch (MalformedLineException) { throw new InvalidDataException($"CSV {parser.ErrorLineNumber}行目：引用符の形式が不正です"); }
            cursor = parser.LineNumber > 0 ? checked((int)parser.LineNumber - 1) : physicalLines.Length;
            if (fields.Length != 1) throw new InvalidDataException($"CSV {number}行目：開始TCを1列で指定してください");
            string value = fields[0].Trim();
            if (first && value == "開始TC") { first = false; continue; }
            first = false;
            // A quoted empty field is an input error, not a skipped CSV record.
            Add(values, new(value, number));
        }
        return values.ToArray();
    }

    static void CheckSize(string text)
    {
        if (text.Length > MaximumInputCharacters) throw new InvalidDataException("入力が大きすぎます（最大1Mi文字）");
    }
    static void Add(List<TimecodeInput> values, TimecodeInput value)
    {
        if (values.Count >= 1000) throw new InvalidDataException("開始TCは1000件までです");
        values.Add(value);
    }

    public static CueTimecodePlan Assign(IReadOnlyList<CueTimecodeSource> sources, IReadOnlyList<TimecodeInput> input, TimecodeRate rate)
    {
        CheckTargets(sources);
        if (sources.Count != input.Count) throw new InvalidDataException($"対象は{sources.Count}件、入力は{input.Count}件です");
        var changes = new List<CueTimecodeChange>();
        for (int i = 0; i < sources.Count; i++)
        {
            try { changes.Add(new(sources[i], rate.FormatFrames(ParseInput(input[i].Value, rate)))); }
            catch (Exception e) when (e is FormatException or OverflowException or InvalidDataException)
            { throw new InvalidDataException($"{input[i].Line}行目：開始TCが不正です", e); }
        }
        return new(rate, changes, false);
    }

    public static CueTimecodePlan Arrange(IReadOnlyList<CueTimecodeSource> sources, string first, decimal gapSeconds, TimecodeRate rate)
    {
        CheckTargets(sources);
        new CuePlacementPreferences(true, gapSeconds).Validate();
        long start = ParseInput(first, rate);
        var changes = new List<CueTimecodeChange>();
        for (int i = 0; i < sources.Count; i++)
        {
            CheckDuration(sources[i]);
            changes.Add(new(sources[i], rate.FormatFrames(start)));
            if (i + 1 < sources.Count) start = After(start, sources[i].Duration, gapSeconds, rate);
        }
        return new(rate, changes, true);
    }

    // Include disabled rows: registration, rather than playback eligibility, defines the tail.
    public static string AppendStart(IEnumerable<CueTimecodeSource> existing, decimal gapSeconds, TimecodeRate rate)
    {
        new CuePlacementPreferences(true, gapSeconds).Validate();
        long latest = 0; bool any = false;
        foreach (var row in existing)
        {
            CheckDuration(row);
            long start;
            try { start = rate.ParseFrames(row.Start); }
            catch (Exception e) when (e is FormatException or OverflowException or InvalidDataException)
            { throw new InvalidDataException($"終了時刻を計算できません：{Path.GetFileName(row.FilePath)}（開始TC不正）", e); }
            latest = Math.Max(latest, After(start, row.Duration, gapSeconds, rate));
            any = true;
        }
        return rate.FormatFrames(any ? latest : 0);
    }

    static long ParseInput(string value, TimecodeRate rate)
    {
        var parts = value.Split(':', ';');
        if (parts.Length != 4 || parts.Any(p => p.Length != 2 || p.Any(c => c < '0' || c > '9')))
            throw new InvalidDataException("開始TCはHH:MM:SS:FFで入力してください");
        return rate.ParseFrames(value);
    }

    static long After(long start, double duration, decimal gap, TimecodeRate rate)
    {
        // Convert the actual media duration to the LTC grid, not to the video's own frame count.
        decimal fps = rate.Nominal == 30 && rate.Fps < 30 ? 30000m / 1001m : rate.Nominal;
        decimal next = start + ((decimal)duration + gap) * fps;
        // Rational-rate division has decimal rounding at exact integral boundaries.
        decimal nearest = decimal.Round(next, 0);
        if (Math.Abs(next - nearest) < 0.000000001m) next = nearest;
        next = decimal.Ceiling(next);
        if (next >= rate.FramesPerDay) throw new InvalidDataException("開始TCが24時間を超えます");
        return (long)next;
    }
    static void CheckDuration(CueTimecodeSource row)
    {
        if (!double.IsFinite(row.Duration) || row.Duration <= 0 || row.Duration >= 86400)
            throw new InvalidDataException($"終了時刻を計算できません：{Path.GetFileName(row.FilePath)}（長さ不明・範囲外）");
    }
    static void CheckTargets(IReadOnlyList<CueTimecodeSource> rows)
    {
        if (rows.Count == 0) throw new InvalidDataException("対象キューがありません");
        if (rows.Count > 1000 || rows.Any(r => r.CueId == Guid.Empty) || rows.Select(r => r.CueId).Distinct().Count() != rows.Count)
            throw new InvalidDataException("対象キューが不正です");
    }
}
