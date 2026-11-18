using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
namespace LtcPlayer.App;
public sealed class CueRow : INotifyPropertyChanged
{
    public Guid CueId{get;set;}=Guid.NewGuid();
    private string path="",hours="00",minutes="00",seconds="00",frames="00",warning="",error="";private bool enabled=true;
    public string FilePath{get=>path;set{if(path==value)return;path=value;SourceRevision=Interlocked.Increment(ref nextSourceRevision);Duration=FrameRate=0;PixelWidth=PixelHeight=0;Codec="";SourceError="";AudioIssue="";Changed();Changed(nameof(Name));Changed(nameof(ContainerDisplay));}}
    public string Name { get { try{return Path.GetFileName(FilePath);}catch(ArgumentException){return FilePath;} } }
    public string Hours{get=>hours;set{hours=value;Changed();}}
    public string Minutes{get=>minutes;set{minutes=value;Changed();}}
    public string Seconds{get=>seconds;set{seconds=value;Changed();}}
    public string Frames{get=>frames;set{frames=value;Changed();}}
    public bool Enabled{get=>enabled;set{enabled=value;Changed();}}
    public string Warning{get=>warning;set{warning=value;Changed();Changed(nameof(WarningMark));Changed(nameof(IssueText));}}
    public string WarningMark=>Warning==""?"":"⚠";
    public string Error{get=>error;set{error=value;Changed();Changed(nameof(IssueText));Changed(nameof(HasError));}}
    string sourceError="";
    static int nextSourceRevision;
    public int SourceRevision {get;private set;}=Interlocked.Increment(ref nextSourceRevision);
    public int MaterialStatusRevision{get;private set;}
    public string SourceError {get=>sourceError;set{if(sourceError==value)return;sourceError=value;MaterialStatusRevision++;Changed();Changed(nameof(IssueText));Changed(nameof(HasError));}}
    string audioIssue="";
    public string AudioIssue{get=>audioIssue;set{audioIssue=value;Changed();Changed(nameof(HasError));Changed(nameof(IssueText));}}
    public bool HasError=>Error!=""||SourceError!=""||AudioIssue!="";
    public string IssueText=>string.Join(" / ",new[]{(Error!=""||SourceError!="")?(Enabled?"同期対象外：":"無効／")+string.Join(" / ",new[]{Error,SourceError}.Where(x=>x!="")):"",AudioIssue!=""?"音声エラー："+AudioIssue:"",Warning}.Where(x=>!string.IsNullOrWhiteSpace(x)));
    public void ClearSourceError(){if(SourceError!=""){SourceRevision=Interlocked.Increment(ref nextSourceRevision);SourceError="";}}
    public void AcceptMedia(MediaInfo info){PixelWidth=info.Width;PixelHeight=info.Height;Duration=info.Duration;FrameRate=info.FrameRate;Codec=info.Codec;AudioIssue="";SourceRevision=Interlocked.Increment(ref nextSourceRevision);SourceError="";Changed(nameof(Duration));Changed(nameof(FrameRate));Changed(nameof(Codec));}
    int pixelWidth,pixelHeight;string codec="";
    CueFormatPreferences formatPreferences=new();
    public CueFormatPreferences FormatPreferences{get=>formatPreferences;set{formatPreferences=value;FormatChanged();}}
    public string ResolutionDisplay=>formatPreferences.Resolution(PixelWidth,PixelHeight);
    public string ContainerDisplay { get { try{return Path.GetExtension(FilePath).TrimStart('.').ToUpperInvariant();}catch(ArgumentException){return "";} } }
    public string ResolutionTooltip=>PixelWidth>0&&PixelHeight>0?$"{PixelWidth}×{PixelHeight}":"";
    void FormatChanged(){Changed(nameof(ResolutionDisplay));Changed(nameof(ResolutionTooltip));}
    public int PixelWidth{get=>pixelWidth;set{if(pixelWidth==value)return;pixelWidth=value;Changed();FormatChanged();}}
    public int PixelHeight{get=>pixelHeight;set{if(pixelHeight==value)return;pixelHeight=value;Changed();FormatChanged();}}
    public double Duration{get;set;}
    public double FrameRate{get;set;}
    public string Codec{get=>codec;set{if(codec==value)return;codec=value;Changed();FormatChanged();}}
    public int DisplayOrder{get;set;}
    public string StartTimecode=>$"{Hours.PadLeft(2,'0')}:{Minutes.PadLeft(2,'0')}:{Seconds.PadLeft(2,'0')}:{Frames.PadLeft(2,'0')}";
    public void SetStart(string value){var p=value.Split(':',';');if(p.Length!=4)throw new InvalidDataException("開始TCの形式不正");Hours=p[0];Minutes=p[1];Seconds=p[2];Frames=p[3];}
    public CueRow CloneDisabled()=>new(){FilePath=FilePath,Hours=Hours,Minutes=Minutes,Seconds=Seconds,Frames=Frames,Enabled=false,PixelWidth=PixelWidth,PixelHeight=PixelHeight,Duration=Duration,FrameRate=FrameRate,Codec=Codec};
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName]string? name=null)=>PropertyChanged?.Invoke(this,new(name));
}
public record ScheduledCue(Guid CueId,string FilePath,double Start,double Duration,double FrameRate,string Codec)
{ public int SourceRevision{get;init;} public bool CanStart{get;init;}=true; }
public record CueValidation(CueRow Row,string Error,string Warning);
public record ProgramValidation(List<ScheduledCue> Timeline,List<CueValidation> Rows);
public sealed class CueScheduler
{
    private readonly ScheduledCue[] sorted;
    public CueScheduler(IEnumerable<ScheduledCue> cues)
    {
        sorted=cues.OrderBy(c=>c.Start).ToArray();
        if(sorted.GroupBy(c=>c.Start).Any(g=>g.Count()>1))throw new InvalidDataException("開始TC重複");
    }
    public ScheduledCue? Select(double seconds)
    {
        var last=sorted.LastOrDefault(c=>c.Start<=seconds);
        return last!=null && seconds<last.Start+last.Duration?last:null;
    }
    public ScheduledCue? Next(double seconds)=>sorted.FirstOrDefault(c=>c.Start>seconds);
}
public record SavedCue(Guid CueId,string FilePath,string StartTimecode,double Duration,double FrameRate,string Codec,bool Enabled,int DisplayOrder);
public record ShowProgramDocument(int Version,string TimecodeRate,List<SavedCue> Cues);
public static class ShowProgramService
{
    public static void RepairIds(IEnumerable<CueRow> rows)
    {
        var used=new HashSet<Guid>();
        foreach(var row in rows){if(row.CueId==Guid.Empty||!used.Add(row.CueId)){do{row.CueId=Guid.NewGuid();}while(!used.Add(row.CueId));}}
    }
    public static ProgramValidation Evaluate(IList<CueRow> rows,TimecodeRate rate)
    {
        if(rows.Count>1000)throw new InvalidDataException("キューは1000件まで");
        if(rows.Any(c=>c.CueId==Guid.Empty)||rows.Select(c=>c.CueId).Distinct().Count()!=rows.Count)
            throw new InvalidDataException("キューID不正");
        var starts=new Dictionary<Guid,double>();var errors=new Dictionary<Guid,string>();var warnings=new Dictionary<Guid,string>();
        foreach(var row in rows){
            try{starts[row.CueId]=rate.Parse(row.StartTimecode);}
            catch(Exception e)when(e is FormatException or OverflowException or InvalidDataException){errors[row.CueId]=e.Message;}
        }
        foreach(var group in rows.Where(r=>r.Enabled&&starts.ContainsKey(r.CueId)).GroupBy(r=>starts[r.CueId]).Where(g=>g.Count()>1))
            foreach(var row in group)errors[row.CueId]="開始TC重複";
        var timeline=new List<ScheduledCue>();
        foreach(var row in rows){
            bool metadata=double.IsFinite(row.Duration)&&row.Duration>0&&double.IsFinite(row.FrameRate)&&row.FrameRate>0;
            if(!metadata&&!errors.ContainsKey(row.CueId)&&row.SourceError=="")errors[row.CueId]="動画情報を取得できません";
            if(metadata&&row.Codec is not ("h264" or "hap" or "hap_q"))errors.TryAdd(row.CueId,"非対応コーデック");
            if(row.Enabled&&!errors.ContainsKey(row.CueId)&&metadata)
                timeline.Add(new(row.CueId,row.FilePath,starts[row.CueId],row.Duration,row.FrameRate,row.Codec){SourceRevision=row.SourceRevision,CanStart=row.SourceError==""});
        }
        var ready=timeline.Where(c=>c.CanStart).ToArray();
        for(int i=0;i<ready.Length;i++)for(int j=i+1;j<ready.Length;j++){
            var a=ready[i];var b=ready[j];double overlap=Math.Min(a.Start+a.Duration,b.Start+b.Duration)-Math.Max(a.Start,b.Start);
            if(overlap>0){warnings[a.CueId]=warnings.GetValueOrDefault(a.CueId,"")+"再生区間の重複。 ";warnings[b.CueId]=warnings.GetValueOrDefault(b.CueId,"")+"再生区間の重複。 ";}
        }
        return new(timeline,rows.Select(r=>new CueValidation(r,errors.GetValueOrDefault(r.CueId,""),warnings.GetValueOrDefault(r.CueId,""))).ToList());
    }
    public static List<ScheduledCue> Validate(IList<CueRow> rows,TimecodeRate rate)
    {
        var result=Evaluate(rows,rate);
        foreach(var item in result.Rows){item.Row.Error=item.Error;item.Row.Warning=item.Warning;}
        return result.Timeline.Where(c=>c.CanStart).ToList();
    }
    public static async Task RefreshAsync(CueRow row,CancellationToken token)
    {
        var info=await new MediaDecoder().InspectAsync(row.FilePath,token);
        row.AcceptMedia(info);
    }
    public static ShowProgramDocument Capture(IList<CueRow> rows,TimecodeRate rate)
    {
        if(rows.Count>1000||rows.Any(r=>r.CueId==Guid.Empty)||rows.Select(r=>r.CueId).Distinct().Count()!=rows.Count)
            throw new InvalidDataException("キューリストの構造不正");
        return new(1,rate.Name,rows.Select((r,i)=>new SavedCue(r.CueId,r.FilePath,r.StartTimecode,r.Duration,r.FrameRate,r.Codec,r.Enabled,i)).ToList());
    }
    public static Task SaveAsync(string path,IList<CueRow> rows,TimecodeRate rate,CancellationToken token=default)=>SaveDocumentAsync(path,Capture(rows,rate),token);
    public static async Task SaveDocumentAsync(string path,ShowProgramDocument document,CancellationToken token=default)
    {
        string json=JsonSerializer.Serialize(document,new JsonSerializerOptions{WriteIndented=true});
        string destination=Path.GetFullPath(path);
        string temporary=destination+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{
            await File.WriteAllTextAsync(temporary,json,token);
            token.ThrowIfCancellationRequested();
            File.Move(temporary,destination,true);
        }finally{
            try{File.Delete(temporary);}catch(IOException){}catch(UnauthorizedAccessException){}
        }
    }
    public static async Task<(TimecodeRate Rate,List<CueRow> Rows,List<string> Notices)> LoadAsync(string path,CancellationToken token)
    {
        if(new FileInfo(path).Length>4*1024*1024)throw new InvalidDataException("キューリストが大きすぎます（上限4MiB）");
        var doc=JsonSerializer.Deserialize<ShowProgramDocument>(await File.ReadAllTextAsync(path,token))??throw new InvalidDataException("キューリストを読み込めません");
        if(doc.Version!=1 || doc.Cues==null || doc.Cues.Count>1000)throw new InvalidDataException("キューリストの形式不正");
        var rate=TimecodeRate.Modes.FirstOrDefault(r=>r.Name==doc.TimecodeRate)??throw new InvalidDataException("非対応LTC形式");
        var rows=new List<CueRow>();var notices=new List<string>();var ids=new HashSet<Guid>();
        foreach(var c in doc.Cues.OrderBy(c=>c.DisplayOrder))
        {
            var id=c.CueId;
            if(id==Guid.Empty||!ids.Add(id)){do{id=Guid.NewGuid();}while(!ids.Add(id));notices.Add("キューIDを修復しました");}
            var row=new CueRow{CueId=id,FilePath=c.FilePath??"",Enabled=c.Enabled,DisplayOrder=rows.Count};
            try{row.SetStart(c.StartTimecode??"");}catch(InvalidDataException){row.Hours=c.StartTimecode??"形式不正";}
            try{
                row.FilePath=Path.IsPathFullyQualified(row.FilePath)?row.FilePath:Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!,row.FilePath));
                await RefreshAsync(row,token);
            }
            catch(OperationCanceledException)when(token.IsCancellationRequested){throw;}
            catch(Exception e)when(e is IOException or InvalidDataException or ArgumentException or System.ComponentModel.Win32Exception or OperationCanceledException){row.SourceError=e.Message;notices.Add(row.Name+": "+e.Message);}
            rows.Add(row);
        }
        return(rate,rows,notices);
    }
}
