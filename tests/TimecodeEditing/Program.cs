using LtcPlayer.App;
using LtcPlayer.Avalonia;
using System.Text.Json;

var results = new List<object>();
void Check(bool ok, string why) { if (!ok) throw new Exception(why); }
void Reject(Action action) { try { action(); } catch (Exception e) when (e is InvalidDataException or InvalidOperationException or FormatException or OverflowException or IOException or UnauthorizedAccessException) { return; } throw new Exception("Expected rejection"); }
async Task Test(string name, Func<Task> body) { await body(); results.Add(new { test = name, status = "PASS" }); Console.WriteLine("PASS " + name); }
Task Run(Action body) { body(); return Task.CompletedTask; }
CueRow Row(string name, string tc, double duration = 10, bool enabled = true) { var r = new CueRow { FilePath = name, Duration = duration, FrameRate = 25, Codec = "h264", Enabled = enabled }; r.SetStart(tc); return r; }
CueTimecodeSource[] Capture(params CueRow[] rows) => rows.Select(CueTimecodeSource.Capture).ToArray();
var rate = TimecodeRate.Modes.Single(r => r.Name == "25");
var df = TimecodeRate.Modes.Single(r => r.Name == "29.97 DF");

await Test("TC formatter has independent DF boundary expectations", () => Run(() => {
    foreach (var pair in new (long Frames, string Label)[] { (0,"00:00:00;00"),(1799,"00:00:59;29"),(1800,"00:01:00;02"),(17982,"00:10:00;00"),(107892,"01:00:00;00"),(2589407,"23:59:59;29") })
        Check(df.FormatFrames(pair.Frames) == pair.Label, pair.Label);
    Reject(() => df.ParseFrames("00:01:00;00")); Reject(() => df.FormatFrames(df.FramesPerDay));
}));
await Test("all supported LTC formats round trip across minute hour and day boundaries", () => Run(() => {
    foreach (var r in TimecodeRate.Modes) {
        foreach (long f in new long[] { 0, 1, 1499, 1500, 1799, 1800, 17981, 17982, 89999, r.FramesPerDay - 1 })
            Check(r.ParseFrames(r.FormatFrames(f)) == f, r.Name + "/" + f);
    }
    for (int minute = 0; minute < 1440; minute++) {
        long frame = df.Frames(minute/60, minute%60, 0, minute%10 == 0 ? 0 : 2);
        Check(df.ParseFrames(df.FormatFrames(frame)) == frame, "minute " + minute);
    }
}));
await Test("text accepts BOM CRLF whitespace and blank lines", () => Run(() => {
    var input = CueTimecodes.ReadText("\uFEFF\r\n 00:00:05:00 \r\n\r\n00:00:18:00\n");
    Check(input.Length == 2 && input[0].Line == 2 && input[1].Line == 4, "lines");
}));
await Test("CSV handles header quotes BOM and physical line numbers", () => Run(() => {
    var input = CueTimecodes.ReadCsv("\uFEFF開始TC\r\n\r\n\"00:00:05:00\"\r\n00:00:18:00");
    Check(input.Length == 2 && input[0].Value == "00:00:05:00" && input[0].Line == 3, "CSV");
}));
await Test("multi-column and malformed CSV are rejected", () => Run(() => {
    Reject(() => CueTimecodes.Assign(Capture(Row("A","00:00:00:00")), CueTimecodes.ReadCsv("\"00:00:05\n:00\""), rate));
    Reject(() => CueTimecodes.ReadCsv("00:00:05:00,A.mov")); Reject(() => CueTimecodes.ReadCsv("\"00:00:05:00"));
}));
await Test("quoted empty CSV records are not silently removed", () => Run(() => {
    var row = Row("A", "00:00:00:00"); Reject(() => CueTimecodes.Assign(Capture(row), CueTimecodes.ReadCsv("\"\""), rate));
}));
await Test("input limits reject oversized text and excess records", () => Run(() => {
    Reject(() => CueTimecodes.ReadText(new string('x', CueTimecodes.MaximumInputCharacters + 1)));
    Reject(() => CueTimecodes.ReadCsv(string.Join('\n', Enumerable.Repeat("00:00:00:00", 1001))));
}));
await Test("input errors retain the offending physical line", () => Run(() => {
    try { CueTimecodes.Assign(Capture(Row("A","00:00:00:00")), CueTimecodes.ReadText("\n\nnot TC"), rate); throw new Exception("accepted"); }
    catch (InvalidDataException e) { Check(e.Message.StartsWith("3行目"), e.Message); }
}));
await Test("assignment validates all rows without partial edits", () => Run(() => {
    var a = Row("A", "00:00:01:00"); var b = Row("B", "00:00:02:00");
    Reject(() => CueTimecodes.Assign(Capture(a,b), CueTimecodes.ReadText("00:00:05:00\nbad"), rate));
    Check(a.StartTimecode == "00:00:01:00" && b.StartTimecode == "00:00:02:00", "partial mutation");
    Reject(() => CueTimecodes.Assign(Capture(a,b), CueTimecodes.ReadText("00:00:05:00"), rate));
}));
await Test("spacing uses video ends plus gap in display order", () => Run(() => {
    var a=Row("A","00:00:00:00",10);var b=Row("B","00:00:00:00",12,false);var c=Row("C","00:00:00:00",8);
    var p=CueTimecodes.Arrange(Capture(a,b,c),"00:00:05:00",3,rate);
    Check(p.Changes.Select(c=>c.Start).SequenceEqual(new[]{"00:00:05:00","00:00:18:00","00:00:33:00"}),"not end+gap");
}));
await Test("mixed media fps use LTC grid and zero gap never overlaps", () => Run(() => {
    var a=Row("A","00:00:00:00",10.01);a.FrameRate=30000.0/1001;
    var b=Row("B","00:00:00:00",1);
    var p=CueTimecodes.Arrange(Capture(a,b),"00:00:00:00",0,rate);
    Check(p.Changes[1].Start=="00:00:10:01","25fps rounding");
    var q=CueTimecodes.Arrange(Capture(a,b),"00:00:00;00",0,df);
    Check(df.ParseFrames(q.Changes[1].Start)==300,"rational frame boundary");
}));
await Test("negative gap unknown duration and midnight overflow fail before editing", () => Run(() => {
    var a=Row("A","00:00:00:00");var b=Row("B","00:00:00:00");
    Reject(()=>CueTimecodes.Arrange(Capture(a,b),"00:00:00:00",-1,rate));
    b.Duration=double.NaN;Reject(()=>CueTimecodes.Arrange(Capture(a,b),"00:00:00:00",0,rate));
    b.Duration=10;Reject(()=>CueTimecodes.Arrange(Capture(a,b),"23:59:55:00",0,rate));
}));
await Test("append tail includes disabled rows and ignores display order", () => Run(() => {
    var a=Row("late","00:01:00:00",10,false);var b=Row("early","00:00:05:00",20);
    Check(CueTimecodes.AppendStart(Capture(a,b),3,rate)=="00:01:13:00","disabled tail missing");
    Check(CueTimecodes.AppendStart(Capture(b,a),3,rate)=="00:01:13:00","order changed tail");
    Check(CueTimecodes.AppendStart([],3,rate)=="00:00:00:00","empty tail");
}));
await Test("bad tail fails rather than silently skipping a row", () => Run(() => {
    Reject(()=>CueTimecodes.AppendStart(Capture(Row("bad","bad:00:00:00")),0,rate));
    Reject(()=>CueTimecodes.AppendStart(Capture(Row("unknown","00:00:00:00",0)),0,rate));
}));
await Test("batch application publishes one complete timeline and preserves source identity", () => Run(() => {
    var e=new ShowEditor{Rate=rate};var a=Row("A","00:00:00:00");var b=Row("B","00:00:20:00");e.Add(a);e.Add(b);
    var id=a.CueId;var rev=a.SourceRevision;int changed=0,validated=0;e.PlaybackChanged+=()=>changed++;e.Changed+=()=>validated++;
    var plan=CueTimecodes.Arrange(Capture(a,b),"00:00:05:00",3,rate);e.SetTimecodes(plan);
    Check(changed==1&&validated==1&&a.CueId==id&&a.SourceRevision==rev&&b.StartTimecode=="00:00:18:00","batch publication/identity");
}));
await Test("stale row source duration and rate reject the entire proposal", () => Run(() => {
    foreach(string mutation in new[]{"remove","source","duration","rate","tc"}){
        var e=new ShowEditor{Rate=rate};var a=Row("A","00:00:00:00");var b=Row("B","00:00:20:00");e.Add(a);e.Add(b);
        var plan=CueTimecodes.Arrange(Capture(a,b),"00:00:05:00",3,rate);
        if(mutation=="remove")e.Delete(b);if(mutation=="source")b.FilePath="replacement";if(mutation=="duration")b.Duration=11;if(mutation=="rate")e.Rate=df;if(mutation=="tc")b.SetStart("00:00:21:00");
        Reject(()=>e.SetTimecodes(plan));Check(a.StartTimecode=="00:00:00:00","partial "+mutation);
    }
}));
await Test("selected subset leaves unselected and enabled state unchanged", () => Run(() => {
    var e=new ShowEditor{Rate=rate};var a=Row("A","00:00:01:00");var b=Row("B","00:00:02:00",10,false);e.Add(a);e.Add(b);
    e.SetTimecodes(CueTimecodes.Assign(Capture(b),CueTimecodes.ReadText("00:00:05:00"),rate));
    Check(a.StartTimecode=="00:00:01:00"&&b.StartTimecode=="00:00:05:00"&&!b.Enabled,"subset");
}));
await Test("duplicate imported starts retain existing exclusion semantics", () => Run(() => {
    var e=new ShowEditor{Rate=rate};var a=Row("A","00:00:01:00");var b=Row("B","00:00:02:00");e.Add(a);e.Add(b);
    e.SetTimecodes(CueTimecodes.Assign(Capture(a,b),CueTimecodes.ReadText("00:00:05:00\n00:00:05:00"),rate));
    Check(a.Enabled&&b.Enabled&&a.Error.Contains("重複")&&b.Error.Contains("重複")&&e.Timeline.Length==0,"duplicate behavior");
}));
await Test("LTC acquisition blocks batch at final editor boundary", () => Run(() => {
    bool allowed=true;var e=new ShowEditor{Rate=rate,DefinitionAuthority=()=>allowed};var a=Row("A","00:00:01:00");e.Add(a);
    var p=CueTimecodes.Assign(Capture(a),CueTimecodes.ReadText("00:00:05:00"),rate);allowed=false;
    Reject(()=>e.SetTimecodes(p));Check(a.StartTimecode=="00:00:01:00","locked apply");
}));
await Test("single invalid TC editing still reports status rather than refusing input", () => Run(() => {
    var e=new ShowEditor{Rate=rate};var a=Row("A","00:00:00:00");e.Add(a);e.SetTimecode(a,["xx","00","00","00"]);Check(a.Error!="","invalid single TC missing");
    e.SetTimecode(a,["00","00","05","00"]);Check(a.Error=="","editing suppression was stuck");
}));
await Test("multi-file automatic import appends atomically after latest end", async () => {
    var e=new ShowEditor{Rate=rate};e.Add(Row("existing","00:00:20:00",10,false));int changes=0;e.PlaybackChanged+=()=>changes++;
    await e.ImportAsync(["one.mp4","two.mp4"],"00:00:00:00",new(true,3));
    Check(e.Rows[1].StartTimecode=="00:00:33:00"&&e.Rows[2].StartTimecode=="00:00:46:00"&&changes==1,"import arrangement");
});
await Test("default import and clone retain old initial TC policy", async () => {
    var e=new ShowEditor{Rate=rate};await e.ImportAsync(["one.mp4"],"00:00:00:00",new());
    Check(e.Rows[0].StartTimecode=="00:00:00:00","default changed");var clone=e.Clone(e.Rows[0]);Check(!clone.Enabled&&clone.StartTimecode==e.Rows[0].StartTimecode,"clone policy");
});
await Test("bad automatic import leaves original rows intact", async () => {
    var e=new ShowEditor{Rate=rate};e.Add(Row("unknown","00:00:00:00",0));bool rejected=false;
    try{await e.ImportAsync(["one.mp4","two.mp4"],"00:00:00:00",new(true,0));}catch(InvalidDataException){rejected=true;}
    Check(rejected&&e.Rows.Count==1,"partial automatic import");
});
await Test("LTC starts during media preparation: no new rows are committed", async () => {
    bool allowed=true;var e=new ShowEditor{Rate=rate,DefinitionAuthority=()=>allowed};e.Add(Row("existing","00:00:00:00"));
    var gate=new TaskCompletionSource<MediaInfo>();MediaDecoder.Inspect=_=>gate.Task;
    try{var import=e.ImportAsync(["pending.mp4"],"00:00:00:00",new(true,0));allowed=false;gate.SetResult(new());bool rejected=false;try{await import;}catch(InvalidOperationException){rejected=true;}Check(rejected&&e.Rows.Count==1,"partial race import");}
    finally{MediaDecoder.Inspect=_=>Task.FromResult(new MediaInfo());}
});

string folder=Path.Combine(Path.GetTempPath(),"ltcue-timecodes-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
try {
await Test("old settings load default policy and new policy round trips", () => Run(() => {
    string file=Path.Combine(folder,"settings.json");File.WriteAllText(file,"{\"Rate\":\"25\"}");Check(!InputSettings.Load(file).CuePlacement.AfterExisting,"legacy default");
    var settings=new InputSettings{CuePlacement=new(true,1.25m)};using(var pending=new PendingSettings(file,settings,"DISPLAY"))pending.Commit();
    var loaded=InputSettings.Load(file);Check(loaded.CuePlacement==settings.CuePlacement,"roundtrip");using var json=JsonDocument.Parse(File.ReadAllText(file));Check(json.RootElement.GetProperty("OutputDisplayName").GetString()=="DISPLAY","display key lost");
}));
await Test("invalid saved gap is rejected", () => Run(() => {
    string file=Path.Combine(folder,"invalid.json");File.WriteAllText(file,"{\"CuePlacement\":{\"AfterExisting\":true,\"GapSeconds\":-1}}");Reject(()=>InputSettings.Load(file));
}));
await Test("failed preference save preserves existing file and cleans temporary data", () => Run(() => {
    string file=Path.Combine(folder,"locked.json");File.WriteAllText(file,"old");
    using(var held=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read))Reject(()=>{using var pending=new PendingSettings(file,new InputSettings{CuePlacement=new(true,3)},"DISPLAY");pending.Commit();});
    Check(File.ReadAllText(file)=="old"&&!Directory.GetFiles(folder,"*.tmp").Any(),"failed save changed file");
}));
} finally { Directory.Delete(folder,true); }

await Test("cue format matches both dimensions and handles missing values in every mode", () => Run(() => {
    var p=new CueFormatPreferences();
    Check(p.Format("HAP",1920,1080)=="HAP · FHD","default FHD");
    Check(p.Format("HAP",1980,1080)=="HAP · 未登録","unknown");
    Check(p.Format("HAP",1080,1920)=="HAP · 未登録","portrait");
    foreach(var mode in Enum.GetValues<CueResolutionMode>()){
        var m=p with{Mode=mode};Check(m.Format("HAP",0,1080)=="HAP","unknown size");
        Check(m.Format("HAP",1920,1080,true)=="HAP · 1920×1080","tooltip");
        Check(m.Format("",0,0)=="","empty separator");
    }
    Check((p with{Mode=CueResolutionMode.Dimensions}).Format("",1920,1080)=="1920×1080","dimensions without codec");
    var custom=p with{Presets=[new(1980,1080,"LED")]};Check(custom.Format("HAP",1980,1080)=="HAP · LED","custom");
    Check((custom with{Presets=[]}).Format("HAP",1980,1080)=="HAP · 未登録","delete");
}));
await Test("cue format validates dimensions duplicate rows and labels", () => Run(() => {
    foreach(var presets in new ResolutionPreset[][] { [new(0,720,"HD")], [new(1,-1,"X")], [new(1,1,"")], [new(1,1,"123456789")], [new(1,1,"A"),new(1,1,"B")] })
        Reject(()=>new CueFormatPreferences{Presets=presets}.Validate());
    new CueFormatPreferences{Presets=[]}.Validate();
}));
await Test("cue format updates notify bindings and replacement clears stale dimensions", () => Run(() => {
    var row=new CueRow{FilePath="old",Codec="HAP",PixelWidth=1920,PixelHeight=1080};var names=new List<string?>();row.PropertyChanged+=(_,e)=>names.Add(e.PropertyName);
    row.PixelWidth=1980;Check(row.ResolutionDisplay=="未登録"&&names.Contains("ResolutionDisplay")&&names.Contains("FormatTooltip"),"metadata notifications");
    row.FormatPreferences=new(){Presets=[new(1980,1080,"LED")]};Check(row.ResolutionDisplay=="LED","settings notification");
    row.FilePath="replacement";Check(row.PixelWidth==0&&row.PixelHeight==0&&row.ResolutionDisplay=="","stale dimensions");
}));
await Test("cue format legacy defaults and explicit empty presets survive persistence", () => Run(() => {
    string file=Path.Combine(Path.GetTempPath(),"ltcue-format-"+Guid.NewGuid().ToString("N")+".json");
    try{
        File.WriteAllText(file,"{}");Check(InputSettings.Load(file).CueFormat.Presets.Length==3,"legacy defaults");
        File.WriteAllText(file,"{\"CueFormat\":{\"Mode\":\"Hidden\",\"Presets\":[]}}");Check(InputSettings.Load(file).CueFormat.Mode==CueResolutionMode.Preset,"legacy hidden migration");
        Check(new CueFormatPreferences().Resolution(1920,1080)=="FHD","resolution column excludes codec");
        Check(new CueFormatPreferences().Resolution(0,1080)=="","missing resolution stays blank");
        var settings=new InputSettings{AudioVolume=.25,CuePlacement=new(true,2),CueFormat=new(){Mode=CueResolutionMode.Dimensions,Presets=[]}};
        using(var pending=new PendingSettings(file,settings,"DISPLAY"))pending.Commit();
        var loaded=InputSettings.Load(file);Check(loaded.CueFormat.Mode==CueResolutionMode.Dimensions&&loaded.CueFormat.Presets.Length==0&&loaded.AudioVolume==.25&&loaded.CuePlacement.AfterExisting,"settings roundtrip");
    }finally{File.Delete(file);}
}));

string output=args.FirstOrDefault()??"results.json";Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
File.WriteAllText(output,JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine($"{results.Count} checks passed");
