using System.Text.Json;
using LtcPlayer.App;
namespace LtcPlayer.Avalonia;
// Opt-in crash investigation only. Never let diagnostics stop output.
internal sealed class RenderCallTrace : IDisposable
{
 StreamWriter? writer;long bytes;readonly string file;
 public RenderCallTrace(string folder){
  file=Path.Combine(folder,$"render-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.jsonl");
  try{Directory.CreateDirectory(folder);writer=new(new FileStream(file,FileMode.CreateNew,FileAccess.Write,FileShare.Read)){AutoFlush=true};}catch(IOException){}catch(UnauthorizedAccessException){}
 }
 public int Call(string operation,Func<int> action){
  Write(operation,"enter",null);
  try{int result=action();Write(operation,"return",result);return result;}
  catch{Write(operation,"exception",null);throw;}
 }
 void Write(string operation,string phase,int? result){
  if(writer==null)return;
  try{string line=JsonSerializer.Serialize(new{operation,phase,result,hostSeconds=MasterClock.Timestamp(),thread=Environment.CurrentManagedThreadId});bytes+=System.Text.Encoding.UTF8.GetByteCount(line)+2;
   if(bytes>8*1024*1024){Dispose();return;}writer.WriteLine(line);
  }catch(IOException){Dispose();}catch(UnauthorizedAccessException){Dispose();}
 }
 public void Dispose(){try{writer?.Dispose();}catch(IOException){}writer=null;}
}
