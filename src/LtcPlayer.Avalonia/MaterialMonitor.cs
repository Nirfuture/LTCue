using LtcPlayer.App;
namespace LtcPlayer.Avalonia;

public sealed record MaterialRequest(Guid Id,string Path,int Revision,bool HasMetadata,bool HasError,int StatusRevision=0);
public sealed record MaterialResult(MaterialRequest Request,MediaInfo? Info,string Error);

// Owns all background file checks. Results are applied by the UI owner only after
// matching the original row, path and revision. Never decodes on the rendering thread.
public sealed class MaterialMonitor : IAsyncDisposable
{
    sealed record Stamp(long Length,DateTime Modified);
    sealed record Observed(Stamp? Stamp,bool Pending);
    readonly Func<Task<MaterialRequest[]>> snapshot;
    readonly Func<MaterialResult[],Task> apply;
    readonly Func<bool> preparing;
    readonly Func<MediaDecoder> createDecoder;
    readonly CancellationTokenSource stop=new();
    readonly Task worker;
    readonly SemaphoreSlim wake=new(0,1);
    readonly Dictionary<string,FileSystemWatcher> watchers=new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string,Observed> observations=new(StringComparer.OrdinalIgnoreCase);
    public MaterialMonitor(Func<Task<MaterialRequest[]>> snapshot,Func<MaterialResult[],Task> apply,Func<bool> preparing,Func<MediaDecoder>? createDecoder=null)
    {this.snapshot=snapshot;this.apply=apply;this.preparing=preparing;this.createDecoder=createDecoder??(()=>new MediaDecoder());worker=Task.Run(RunAsync);}

    void Signal(){try{wake.Release();}catch(SemaphoreFullException){}catch(ObjectDisposedException){}}
    void UpdateWatchers(IEnumerable<string> paths)
    {
        var directories=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var path in paths)try{var directory=Path.GetDirectoryName(Path.GetFullPath(path));if(directory!=null)directories.Add(directory);}catch(ArgumentException){}
        foreach(var directory in watchers.Keys.Where(d=>!directories.Contains(d)).ToArray()){watchers[directory].Dispose();watchers.Remove(directory);}
        foreach(var directory in directories){
            if(watchers.ContainsKey(directory))continue;
            try{
                var watcher=new FileSystemWatcher(directory){NotifyFilter=NotifyFilters.FileName|NotifyFilters.Size|NotifyFilters.LastWrite};
                watcher.Changed+=(_,_)=>Signal();watcher.Created+=(_,_)=>Signal();watcher.Deleted+=(_,_)=>Signal();watcher.Renamed+=(_,_)=>Signal();watcher.Error+=(_,_)=>Signal();
                watcher.EnableRaisingEvents=true;watchers.Add(directory,watcher);
            }catch(Exception e)when(e is IOException or ArgumentException or UnauthorizedAccessException){}
        }
    }

    async Task RunAsync()
    {
        var token=stop.Token;
        try{while(!token.IsCancellationRequested){
            var requests=await snapshot();
            var paths=requests.Select(r=>r.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            UpdateWatchers(paths);
            foreach(var path in observations.Keys.Where(p=>!paths.Contains(p)).ToArray())observations.Remove(path);
            foreach(var group in requests.GroupBy(r=>r.Path,StringComparer.OrdinalIgnoreCase)){
                token.ThrowIfCancellationRequested();
                string path=group.Key;Stamp? stamp=null;
                try{var file=new FileInfo(path);if(file.Exists)stamp=new(file.Length,file.LastWriteTimeUtc);}
                catch(Exception e)when(e is IOException or UnauthorizedAccessException or ArgumentException){}
                observations.TryGetValue(path,out var prior);
                if(stamp==null){
                    observations[path]=new(null,false);
                    if(group.Any(r=>!r.HasError))await apply(group.Select(r=>new MaterialResult(r,null,"ファイルなし")).ToArray());
                    continue;
                }
                // Trust successful import/load metadata until a file change. A new
                // error on an unchanged source is not repeatedly retried in a loop.
                if(prior==null&&group.All(r=>r.HasMetadata&&!r.HasError)){observations[path]=new(stamp,false);continue;}
                if(prior!=null&&prior.Stamp==stamp&&!prior.Pending)continue;
                if(prior==null||prior.Stamp!=stamp){observations[path]=new(stamp,true);continue;}
                if(preparing())continue;
                observations[path]=new(stamp,false);
                using var check=CancellationTokenSource.CreateLinkedTokenSource(token);check.CancelAfter(TimeSpan.FromSeconds(12));
                MediaInfo? info=null;string error="";
                try{
                    var decoder=createDecoder();info=await decoder.InspectAsync(path,check.Token);
                    bool frame=false;
                    await foreach(var _ in decoder.DecodeAsync(path,64,64,check.Token)){frame=true;break;}
                    if(!frame)throw new InvalidDataException("表示できるフレームなし");
                    var after=new FileInfo(path);
                    if(!after.Exists||new Stamp(after.Length,after.LastWriteTimeUtc)!=stamp){observations[path]=new(stamp,true);continue;}
                }
                catch(OperationCanceledException)when(token.IsCancellationRequested){throw;}
                catch(Exception e)when(e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or OperationCanceledException){info=null;error=e is OperationCanceledException?"素材の確認がタイムアウト":e.Message;}
                await apply(group.Select(r=>new MaterialResult(r,info,error)).ToArray());
            }
            await wake.WaitAsync(TimeSpan.FromSeconds(1),token);
            await Task.Delay(200,token);
        }}catch(OperationCanceledException)when(token.IsCancellationRequested){}
        finally{foreach(var watcher in watchers.Values)watcher.Dispose();watchers.Clear();}
    }
    public async ValueTask DisposeAsync(){stop.Cancel();try{await worker;}finally{wake.Dispose();stop.Dispose();}}
}
