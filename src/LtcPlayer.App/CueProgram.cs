using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
namespace LtcPlayer.App;
public sealed class CueRow : INotifyPropertyChanged
{
    public Guid CueId{get;set;}=Guid.NewGuid();
    private string path="",hours="00",minutes="00",seconds="00",frames="00",warning="",error="";private bool enabled=true;
    public string FilePath{get=>path;set{path=value;Changed();Changed(nameof(Name));}}
    public string Name=>Path.GetFileName(FilePath);
    public string Hours{get=>hours;set{hours=value;Changed();}}
    public string Minutes{get=>minutes;set{minutes=value;Changed();}}
    public string Seconds{get=>seconds;set{seconds=value;Changed();}}
    public string Frames{get=>frames;set{frames=value;Changed();}}
    public bool Enabled{get=>enabled;set{enabled=value;Changed();}}
    public string Warning{get=>warning;set{warning=value;Changed();Changed(nameof(WarningMark));Changed(nameof(IssueText));}}
    public string WarningMark=>Warning==""?"":"⚠";
    public string Error{get=>error;set{error=value;Changed();Changed(nameof(IssueText));}}
    public string IssueText=>string.Join(" / ",new[]{Error,Warning}.Where(x=>!string.IsNullOrWhiteSpace(x)));
    public int PixelWidth{get;set;}
    public int PixelHeight{get;set;}
    public double Duration{get;set;}
    public double FrameRate{get;set;}
    public string Codec{get;set;}="";
    public int DisplayOrder{get;set;}
    public string StartTimecode=>$"{Hours.PadLeft(2,'0')}:{Minutes.PadLeft(2,'0')}:{Seconds.PadLeft(2,'0')}:{Frames.PadLeft(2,'0')}";
    public void SetStart(string value){var p=value.Split(':',';');if(p.Length!=4)throw new InvalidDataException("開始TCの形式が不正です。");Hours=p[0];Minutes=p[1];Seconds=p[2];Frames=p[3];}
    public CueRow CloneDisabled()=>new(){FilePath=FilePath,Hours=Hours,Minutes=Minutes,Seconds=Seconds,Frames=Frames,Enabled=false,PixelWidth=PixelWidth,PixelHeight=PixelHeight,Duration=Duration,FrameRate=FrameRate,Codec=Codec};
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName]string? name=null)=>PropertyChanged?.Invoke(this,new(name));
}
public record ScheduledCue(Guid CueId,string FilePath,double Start,double Duration,double FrameRate,string Codec);
public sealed class CueScheduler
{
    private readonly ScheduledCue[] sorted;
    public CueScheduler(IEnumerable<ScheduledCue> cues)
    {
        sorted=cues.OrderBy(c=>c.Start).ToArray();
        if(sorted.GroupBy(c=>c.Start).Any(g=>g.Count()>1))throw new InvalidDataException("同一開始TCの有効キューが競合しています。");
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
    public static List<ScheduledCue> Validate(IList<CueRow> rows,TimecodeRate rate)
    {
        var result=new List<ScheduledCue>();var errors=new List<string>();
        if(rows.Count>1000)throw new InvalidDataException("キューは1000件以内にしてください。");
        if(rows.Any(c=>c.CueId==Guid.Empty))errors.Add("空のCueIDがあります。");
        if(rows.Select(c=>c.CueId).Distinct().Count()!=rows.Count)errors.Add("CueIDが重複しています。");
        foreach(var row in rows)
        {
            row.Error="";row.Warning="";
            try {
                var start=rate.Parse(row.StartTimecode);
                if(row.Duration<=0||!double.IsFinite(row.Duration)||row.FrameRate<=0||!double.IsFinite(row.FrameRate))throw new InvalidDataException("動画情報が未検証です。");
                if(row.Codec is not ("h264" or "hap" or "hap_q"))throw new InvalidDataException("対応外コーデックです。");
                if(row.Enabled)result.Add(new(row.CueId,row.FilePath,start,row.Duration,row.FrameRate,row.Codec));
            }catch(Exception e){row.Error=e.Message;if(row.Enabled)errors.Add(row.Name+": "+e.Message);}
        }
        var byId=rows.GroupBy(r=>r.CueId).ToDictionary(g=>g.Key,g=>g.First());
        foreach(var group in result.GroupBy(c=>c.Start).Where(g=>g.Count()>1))
            foreach(var c in group){byId[c.CueId].Error="同一開始TCの競合";errors.Add("同一開始TCの競合");}
        for(int i=0;i<result.Count;++i)for(int j=i+1;j<result.Count;++j)
        {
            var a=result[i];var b=result[j];double start=Math.Max(a.Start,b.Start),end=Math.Min(a.Start+a.Duration,b.Start+b.Duration);
            if(start<end){var ra=byId[a.CueId];var rb=byId[b.CueId];ra.Warning+=$"{rb.Name}と{end-start:F3}秒重複。 ";rb.Warning+=$"{ra.Name}と{end-start:F3}秒重複。 ";}
        }
        if(errors.Count>0)throw new InvalidDataException(string.Join(" / ",errors.Distinct()));return result;
    }
    public static async Task RefreshAsync(CueRow row,CancellationToken token)
    {
        var info=await new MediaDecoder().InspectAsync(row.FilePath,token);
        row.PixelWidth=info.Width;row.PixelHeight=info.Height;row.Duration=info.Duration;row.FrameRate=info.FrameRate;row.Codec=info.Codec;
    }
    public static async Task SaveAsync(string path,IList<CueRow> rows,TimecodeRate rate)
    {
        Validate(rows,rate);
        if(rows.Any(r=>r.Error!=""))throw new InvalidDataException("無効なキューの開始TC・動画情報も修正してから保存してください。");
        var doc=new ShowProgramDocument(1,rate.Name,rows.Select((r,i)=>new SavedCue(r.CueId,r.FilePath,r.StartTimecode,r.Duration,r.FrameRate,r.Codec,r.Enabled,i)).ToList());
        var tmp=path+".tmp";await File.WriteAllTextAsync(tmp,JsonSerializer.Serialize(doc,new JsonSerializerOptions{WriteIndented=true}));File.Move(tmp,path,true);
    }
    public static async Task<(TimecodeRate Rate,List<CueRow> Rows,List<string> Notices)> LoadAsync(string path,CancellationToken token)
    {
        if(new FileInfo(path).Length>4*1024*1024)throw new InvalidDataException("プログラムJSONが大きすぎます。");
        var doc=JsonSerializer.Deserialize<ShowProgramDocument>(await File.ReadAllTextAsync(path,token))??throw new InvalidDataException("空のプログラムです。");
        if(doc.Version!=1 || doc.Cues==null || doc.Cues.Count>1000 || doc.Cues.Select(c=>c.CueId).Distinct().Count()!=doc.Cues.Count)throw new InvalidDataException("プログラム形式/CueIDが不正です。");
        var rate=TimecodeRate.Modes.FirstOrDefault(r=>r.Name==doc.TimecodeRate)??throw new InvalidDataException("未対応のLTCモードです。");
        var rows=new List<CueRow>();var notices=new List<string>();
        foreach(var c in doc.Cues.OrderBy(c=>c.DisplayOrder))
        {
            var row=new CueRow{CueId=c.CueId,FilePath=Path.IsPathFullyQualified(c.FilePath)?c.FilePath:Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!,c.FilePath)),Enabled=c.Enabled,DisplayOrder=rows.Count};row.SetStart(c.StartTimecode);
            try{await RefreshAsync(row,token);if(Math.Abs(row.Duration-c.Duration)>0.001 || Math.Abs(row.FrameRate-c.FrameRate)>0.001 || row.Codec!=c.Codec)notices.Add(row.Name+": 実ファイルのメタデータに更新しました。");}
            catch(Exception e){row.Error=e.Message;notices.Add(row.Name+": "+e.Message);}
            rows.Add(row);
        }
        return(rate,rows,notices);
    }
}
