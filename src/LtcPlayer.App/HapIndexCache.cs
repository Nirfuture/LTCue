namespace LtcPlayer.App;

// A window/session owns this cache. Only immutable analysis is shared; live streams
// and handles stay with their player. No file reads or waits under state.
public sealed class HapIndexCache : IAsyncDisposable
{
 readonly record struct Version(string Path,long Length,long Modified,long Created);
 sealed class Entry(Version version,MediaInfo info){
  public readonly Version Version=version;public readonly MediaInfo Info=info;
  public readonly CancellationTokenSource Cancel=new();
  public Task<IReadOnlyList<HapMediaDecoder.Packet>> Task=null!;
  public IReadOnlyList<HapMediaDecoder.Packet>? Value;public int Waiters;public long Used,Bytes;public bool Released;
 }
 readonly object state=new();readonly SemaphoreSlim buildGate=new(1);
 readonly Dictionary<Version,Entry> entries=[];readonly HashSet<Task> workers=[];bool disposed;
 readonly int maxEntries;readonly long maxBytes;long clock,builds,hits;
 public long Builds=>Interlocked.Read(ref builds);public long Hits=>Interlocked.Read(ref hits);
 public int Count{get{lock(state)return entries.Values.Count(e=>e.Value!=null);}}
 public long RetainedBytes{get{lock(state)return entries.Values.Sum(e=>e.Bytes);}}
 public HapIndexCache(int maxEntries=4,long maxBytes=32*1024*1024){if(maxEntries<1||maxBytes<1)throw new ArgumentOutOfRangeException();this.maxEntries=maxEntries;this.maxBytes=maxBytes;}
 static Version Capture(string path){var file=new FileInfo(path);if(!file.Exists)throw new FileNotFoundException("ファイルなし",path);return new(file.FullName.ToUpperInvariant(),file.Length,file.LastWriteTimeUtc.Ticks,file.CreationTimeUtc.Ticks);}
 public async Task<IReadOnlyList<HapMediaDecoder.Packet>> GetAsync(string path,MediaInfo info,Func<CancellationToken,Task<IReadOnlyList<HapMediaDecoder.Packet>>> build,CancellationToken token)
 {
  token.ThrowIfCancellationRequested();var version=Capture(path);Entry entry;
  if(info.FileVersion is {} source&&source!=new MediaFileVersion(version.Path,version.Length,version.Modified,version.Created))throw new IOException("解析後にファイルが変更されました");
  lock(state){
   ObjectDisposedException.ThrowIf(disposed,this);
   foreach(var old in entries.Values.Where(e=>e.Version.Path==version.Path&&e.Version!=version&&e.Value!=null).ToArray()){entries.Remove(old.Version);Release(old);}
   entries.TryGetValue(version,out entry!);
   if(entry==null||entry.Info!=info){
    if(entries.Values.Count(e=>e.Value==null)>=8)throw new IOException("素材の準備要求が上限超過");
    entry=new(version,info);entries[version]=entry;var owner=entry;
    entry.Task=Task.Run(()=>BuildAsync(path,owner,build));workers.Add(entry.Task);
    _=entry.Task.ContinueWith(t=>{_ = t.Exception;lock(state){workers.Remove(t);Release(owner);}},CancellationToken.None,TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
   }else{entry.Used=++clock;Interlocked.Increment(ref hits);}
   entry.Waiters++;
  }
  try{
   var value=await entry.Task.WaitAsync(token).ConfigureAwait(false);token.ThrowIfCancellationRequested();
   entry.Cancel.Token.ThrowIfCancellationRequested();
   if(Capture(path)!=version)throw new IOException("準備中にファイルが変更されました");return value;
  }finally{
   lock(state){entry.Waiters--;if(entry.Waiters==0&&entry.Value==null){if(entries.TryGetValue(version,out var current)&&ReferenceEquals(current,entry))entries.Remove(version);entry.Cancel.Cancel();}Release(entry);}
  }
 }
 async Task<IReadOnlyList<HapMediaDecoder.Packet>> BuildAsync(string path,Entry owner,Func<CancellationToken,Task<IReadOnlyList<HapMediaDecoder.Packet>>> build)
 {
  bool entered=false;
  try{
   await buildGate.WaitAsync(owner.Cancel.Token).ConfigureAwait(false);entered=true;owner.Cancel.Token.ThrowIfCancellationRequested();Interlocked.Increment(ref builds);
   var value=await build(owner.Cancel.Token).ConfigureAwait(false);owner.Cancel.Token.ThrowIfCancellationRequested();
   if(Capture(path)!=owner.Version)throw new IOException("準備中にファイルが変更されました");
   long bytes=checked(256L+owner.Version.Path.Length*2L+value.Count*64L);
   lock(state){
    owner.Cancel.Token.ThrowIfCancellationRequested();
    if(entries.TryGetValue(owner.Version,out var current)&&ReferenceEquals(current,owner)){
     if(bytes<=maxBytes){owner.Value=value;owner.Bytes=bytes;owner.Used=++clock;Trim();}else entries.Remove(owner.Version);
    }
   }
   return value;
  }catch{
   lock(state){if(entries.TryGetValue(owner.Version,out var current)&&ReferenceEquals(current,owner))entries.Remove(owner.Version);}throw;
  }finally{if(entered)buildGate.Release();}
 }
 void Trim()
 {
  while(entries.Values.Count(e=>e.Value!=null)>maxEntries||entries.Values.Sum(e=>e.Bytes)>maxBytes){var oldest=entries.Values.Where(e=>e.Value!=null).MinBy(e=>e.Used)!;entries.Remove(oldest.Version);Release(oldest);}
 }
 void Release(Entry e){if(!e.Released&&e.Waiters==0&&e.Task.IsCompleted&&(!entries.TryGetValue(e.Version,out var current)||!ReferenceEquals(current,e))){e.Released=true;e.Cancel.Dispose();}}
 public async ValueTask DisposeAsync(){Task[] pending;lock(state){if(disposed)return;disposed=true;var removed=entries.Values.ToArray();entries.Clear();foreach(var e in removed){e.Cancel.Cancel();Release(e);}pending=workers.ToArray();}try{await Task.WhenAll(pending);}catch{}buildGate.Dispose();}
 public void Invalidate(string path)
 {
  string normalized=Path.GetFullPath(path).ToUpperInvariant();Entry[] removed;
  lock(state){removed=entries.Values.Where(e=>e.Version.Path==normalized).ToArray();foreach(var e in removed){entries.Remove(e.Version);e.Cancel.Cancel();Release(e);}}
  // Existing streams keep their immutable arrays; in-flight results cannot be adopted.
 }
}
