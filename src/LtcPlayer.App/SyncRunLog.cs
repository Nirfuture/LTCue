using System.Text.Json;
namespace LtcPlayer.App;
// Diagnostics must never stop production or grow without a session limit.
public sealed class SyncRunLog : IDisposable
{
 readonly object gate=new();StreamWriter? writer;double lastWrite;long bytes;
 const long SessionLimit=8*1024*1024,FolderLimit=256*1024*1024;
 public string FileName{get;}="";public string? Error{get;private set;}
 public SyncRunLog():this(null){}
 internal SyncRunLog(string? directory){try{var folder=directory??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LtcPlayer","logs");Directory.CreateDirectory(folder);if(Directory.EnumerateFiles(folder,"sync-*.jsonl").Sum(f=>new FileInfo(f).Length)>=FolderLimit){Error="Log folder limit reached";return;}FileName=Path.Combine(folder,$"sync-{DateTimeOffset.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.jsonl");writer=new(new FileStream(FileName,FileMode.CreateNew,FileAccess.Write,FileShare.Read));}catch(Exception e)when(e is IOException or UnauthorizedAccessException){Error=e.Message;}}
 public void Write(object value){lock(gate){double now=MasterClock.Timestamp();if(writer==null||now-lastWrite<.1)return;try{string line=JsonSerializer.Serialize(value,new JsonSerializerOptions{IncludeFields=true});long size=System.Text.Encoding.UTF8.GetByteCount(line)+2;if(bytes+size>SessionLimit){writer.Dispose();writer=null;return;}writer.WriteLine(line);writer.Flush();bytes+=size;lastWrite=now;}catch(Exception e)when(e is IOException or UnauthorizedAccessException){Error=e.Message;try{writer?.Dispose();}catch(IOException){}writer=null;}}}
 public void Dispose(){lock(gate){try{writer?.Dispose();}catch(IOException){}writer=null;}}
}
