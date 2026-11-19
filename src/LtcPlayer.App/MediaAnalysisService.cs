using System.Globalization;
using System.Text.Json;
namespace LtcPlayer.App;

public readonly record struct MediaFileVersion(string Path,long Length,long Modified,long Created)
{
    public static MediaFileVersion Capture(string path){var f=new FileInfo(path);if(!f.Exists)throw new FileNotFoundException("ファイルなし",path);return new(f.FullName.ToUpperInvariant(),f.Length,f.LastWriteTimeUtc.Ticks,f.CreationTimeUtc.Ticks);}
}

// All first video/audio/container fields are obtained by a single probe. No live resources.
public sealed record MediaAnalysis(MediaFileVersion Version,string FilePath,string Json)
{
    internal static double? Number(JsonElement s,string key)=>s.TryGetProperty(key,out var v)&&double.TryParse(v.GetString(),NumberStyles.Float,CultureInfo.InvariantCulture,out var n)&&double.IsFinite(n)?n:null;
    public (string Codec,double VideoStart,double? AudioStart,double Duration,double ContainerStart) Audio(){
        using var doc=JsonDocument.Parse(Json);JsonElement? video=null,audio=null;
        foreach(var s in doc.RootElement.GetProperty("streams").EnumerateArray()){
            if(s.GetProperty("codec_type").GetString()=="video"&&video==null)video=s;
            if(s.GetProperty("codec_type").GetString()=="audio"&&audio==null)audio=s;
        }
        if(video==null)throw new InvalidDataException("映像トラックなし");
        var f=doc.RootElement.GetProperty("format");double duration=Number(video.Value,"duration")??Number(f,"duration")??0;
        if(duration<=0)throw new InvalidDataException("再生時間を取得できません");
        return(audio?.GetProperty("codec_name").GetString()??"",Number(video.Value,"start_time")??Number(f,"start_time")??0,audio is {} a?Number(a,"start_time"):null,duration,Number(f,"start_time")??0);
    }
    public string AudioCodec(){using var doc=JsonDocument.Parse(Json);foreach(var s in doc.RootElement.GetProperty("streams").EnumerateArray())if(s.GetProperty("codec_type").GetString()=="audio")return s.GetProperty("codec_name").GetString()??"";return "";}
    MediaInfo? videoInfo;
    public MediaInfo Video(){lock(this)return videoInfo??=ParseVideo();}
    // Video inspection historically rejects non-finite duration even when the
    // container has a valid duration. Audio's nullable/finite fallback differs.
    static double VideoNumber(JsonElement s,string key){double value=0;if(s.TryGetProperty(key,out var v))double.TryParse(v.GetString(),NumberStyles.Float,CultureInfo.InvariantCulture,out value);return value;}
    MediaInfo ParseVideo(){
        using var doc=JsonDocument.Parse(Json);
        var videos=doc.RootElement.GetProperty("streams").EnumerateArray().Where(s=>s.GetProperty("codec_type").GetString()=="video").ToArray();
        if(videos.Length==0)throw new InvalidDataException("映像トラックなし");var s=videos[0];
        string ext=System.IO.Path.GetExtension(FilePath).ToLowerInvariant();
        if(ext is not (".mp4" or ".mov"))throw new InvalidDataException("対応形式：MP4/H.264、MOV/HAP・HAP Q");
        var info=new MediaInfo(s.GetProperty("width").GetInt32(),s.GetProperty("height").GetInt32(),s.GetProperty("codec_name").GetString()!);
        string tag=s.GetProperty("codec_tag_string").GetString()!;
        if(ext==".mov"&&info.Codec=="hap"&&tag is "Hap1" or "HapY")info=info with{Codec=tag=="Hap1"?"hap":"hap_q"};
        else if(ext!=".mp4"||info.Codec!="h264")throw new InvalidDataException("非対応形式："+tag+" / "+info.Codec);
        if(info.Codec!="h264"&&(info.Width%4!=0||info.Height%4!=0))throw new InvalidDataException("HAPの縦横サイズは4の倍数が必要です");
        if(info.Codec!="h264"&&s.TryGetProperty("sample_aspect_ratio",out var sar)&&sar.GetString() is not ("1:1" or "0:1" or "N/A"))throw new InvalidDataException("HAPの非正方形ピクセルは非対応です");
        if(info.Width<=0||info.Height<=0||info.Width>8192||info.Height>8192)throw new InvalidDataException("映像サイズが範囲外");
        double duration=VideoNumber(s,"duration");
        if(duration<=0&&doc.RootElement.TryGetProperty("format",out var format))duration=VideoNumber(format,"duration");
        double fps=0;if(s.TryGetProperty("avg_frame_rate",out var rate)){var parts=rate.GetString()!.Split('/');if(parts.Length==2&&double.TryParse(parts[0],NumberStyles.Float,CultureInfo.InvariantCulture,out var n)&&double.TryParse(parts[1],NumberStyles.Float,CultureInfo.InvariantCulture,out var d)&&d>0)fps=n/d;}
        if(!double.IsFinite(duration)||duration<=0||!double.IsFinite(fps)||fps<=0)throw new InvalidDataException("再生時間・fpsを取得できません");
        return info with{Duration=duration,FrameRate=fps,StartTime=VideoNumber(s,"start_time"),FileVersion=Version};
    }
}

// One window/session owns this bounded LRU and its HAP index cache. Worker launches
// keep synchronous Process.Start off the UI. Each caller cancels only its own wait.
public sealed class MediaAnalysisService : IAsyncDisposable
{
    sealed class Entry(MediaFileVersion version){public readonly MediaFileVersion Version=version;public readonly CancellationTokenSource Cancel=new();public Task<MediaAnalysis> Task=null!;public MediaAnalysis? Value;public int Waiters;public long Used;public bool Released;}
    readonly object state=new();readonly Dictionary<MediaFileVersion,Entry> entries=[];readonly HashSet<Task> workers=[];
    readonly SemaphoreSlim probeGate=new(1);readonly int capacity;readonly Func<string,CancellationToken,Task<string>> probe;
    long clock,runs,hits;bool disposed;
    public HapIndexCache IndexCache{get;}=new();
    public long ProbeRuns=>Interlocked.Read(ref runs);public long Hits=>Interlocked.Read(ref hits);
    public int Count{get{lock(state)return entries.Values.Count(e=>e.Value!=null);}}
    public MediaAnalysisService(int capacity=8,Func<string,CancellationToken,Task<string>>? probe=null){if(capacity<4)throw new ArgumentOutOfRangeException(nameof(capacity));this.capacity=capacity;this.probe=probe??ProbeAsync;}
    public async Task<MediaAnalysis> GetAsync(string path,CancellationToken token){
        token.ThrowIfCancellationRequested();var version=MediaFileVersion.Capture(path);Entry entry;
        lock(state){
            ObjectDisposedException.ThrowIf(disposed,this);
            foreach(var old in entries.Values.Where(e=>e.Version.Path==version.Path&&e.Version!=version).ToArray()){entries.Remove(old.Version);old.Cancel.Cancel();Release(old);}
            if(entries.TryGetValue(version,out entry!)){entry.Used=++clock;Interlocked.Increment(ref hits);MediaProbeTrace.Mark("analysis_cache_hit",path);}
            else{
                if(workers.Count>=8)throw new IOException("素材の準備要求が上限超過");
                entry=new(version);entries.Add(version,entry);var owner=entry;
                entry.Task=Task.Run(()=>BuildAsync(path,owner));workers.Add(entry.Task);
                _=entry.Task.ContinueWith(t=>{_ = t.Exception;lock(state){workers.Remove(t);Release(owner);}},CancellationToken.None,TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
            }
            entry.Waiters++;
        }
        try{
            var value=await entry.Task.WaitAsync(token).ConfigureAwait(false);token.ThrowIfCancellationRequested();
            lock(state){ObjectDisposedException.ThrowIf(disposed,this);entry.Cancel.Token.ThrowIfCancellationRequested();}
            if(MediaFileVersion.Capture(path)!=version)throw new IOException("解析中にファイルが変更されました");return value;
        }finally{lock(state){entry.Waiters--;if(entry.Waiters==0&&entry.Value==null){if(entries.TryGetValue(version,out var current)&&ReferenceEquals(current,entry))entries.Remove(version);entry.Cancel.Cancel();}Release(entry);}}
    }
    async Task<MediaAnalysis> BuildAsync(string path,Entry owner){
        bool entered=false;
        try{
            await probeGate.WaitAsync(owner.Cancel.Token).ConfigureAwait(false);entered=true;owner.Cancel.Token.ThrowIfCancellationRequested();
            Interlocked.Increment(ref runs);MediaProbeTrace.Mark("analysis_begin",path);
            string json=await probe(path,owner.Cancel.Token).ConfigureAwait(false);owner.Cancel.Token.ThrowIfCancellationRequested();
            if(json.Length>1024*1024)throw new InvalidDataException("素材情報が上限超過");
            // Parse before publishing: incomplete/invalid JSON never survives a request.
            using(var document=JsonDocument.Parse(json)){_ = document.RootElement.GetProperty("streams");_ = document.RootElement.GetProperty("format");}
            if(MediaFileVersion.Capture(path)!=owner.Version)throw new IOException("解析中にファイルが変更されました");
            var result=new MediaAnalysis(owner.Version,Path.GetFullPath(path),json);
            lock(state){owner.Cancel.Token.ThrowIfCancellationRequested();if(!disposed&&entries.TryGetValue(owner.Version,out var current)&&ReferenceEquals(current,owner)){
                owner.Value=result;owner.Used=++clock;
                while(entries.Values.Count(e=>e.Value!=null)>capacity){var oldest=entries.Values.Where(e=>e.Value!=null).MinBy(e=>e.Used)!;entries.Remove(oldest.Version);Release(oldest);}
            }}
            MediaProbeTrace.Mark("analysis_complete",path);return result;
        }catch{lock(state){if(entries.TryGetValue(owner.Version,out var current)&&ReferenceEquals(current,owner))entries.Remove(owner.Version);}throw;}
        finally{if(entered)probeGate.Release();}
    }
    void Release(Entry e){if(!e.Released&&e.Waiters==0&&e.Task.IsCompleted&&(!entries.TryGetValue(e.Version,out var current)||!ReferenceEquals(current,e))){e.Released=true;e.Cancel.Dispose();}}
    static async Task<string> ProbeAsync(string path,CancellationToken token){
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var p=MediaDecoder.Start("ffprobe",["-v","error","-show_entries","stream=codec_type,codec_name,codec_tag_string,width,height,duration,avg_frame_rate,start_time,sample_aspect_ratio:format=start_time,duration","-of","json",path]);
        using var registration=timeout.Token.Register(()=>MediaDecoder.Kill(p));var output=ReadBoundedAsync(p,p.StandardOutput,1024*1024);var errors=ReadBoundedAsync(p,p.StandardError,64*1024);
        try{
            try{await p.WaitForExitAsync(timeout.Token).ConfigureAwait(false);}catch(OperationCanceledException e)when(!token.IsCancellationRequested){throw new IOException("素材情報の取得がタイムアウト",e);}
            string json=await output,detail=await errors;if(p.ExitCode!=0)throw new InvalidDataException("素材情報の取得失敗："+detail);return json;
        }finally{MediaDecoder.Kill(p);try{await Task.WhenAll(output,errors).ConfigureAwait(false);}catch{}}
    }
    static async Task<string> ReadBoundedAsync(System.Diagnostics.Process process,StreamReader reader,int limit){
        var text=new System.Text.StringBuilder();var buffer=new char[4096];int n;
        while((n=await reader.ReadAsync(buffer))>0){if(text.Length+n>limit){MediaDecoder.Kill(process);throw new InvalidDataException("素材情報が上限超過");}text.Append(buffer,0,n);}return text.ToString();
    }
    public void Invalidate(string path){
        string normalized=Path.GetFullPath(path).ToUpperInvariant();lock(state){foreach(var e in entries.Values.Where(e=>e.Version.Path==normalized).ToArray()){entries.Remove(e.Version);e.Cancel.Cancel();Release(e);}}
        IndexCache.Invalidate(path);MediaProbeTrace.Mark("analysis_invalidated",path);
    }
    public async ValueTask DisposeAsync(){Task[] pending;lock(state){if(disposed)return;disposed=true;foreach(var e in entries.Values){e.Cancel.Cancel();}var removed=entries.Values.ToArray();entries.Clear();foreach(var e in removed)Release(e);pending=workers.ToArray();}try{await Task.WhenAll(pending).ConfigureAwait(false);}catch{}await IndexCache.DisposeAsync();probeGate.Dispose();}
}
