using System.Text.Json;
namespace LtcPlayer.App;

// Stage in the destination directory; replacement is the single persistence commit.
internal sealed class PendingSettings : IDisposable
{
 readonly string destination;
 string? temporary;
 public PendingSettings(string file,InputSettings settings,string? displayName)
 {
  destination=Path.GetFullPath(file);
  var node=JsonSerializer.SerializeToNode(settings)!.AsObject();node["OutputDisplayName"]=displayName;
  temporary=destination+"."+Guid.NewGuid().ToString("N")+".tmp";
  try{File.WriteAllText(temporary,node.ToJsonString(new JsonSerializerOptions{WriteIndented=true}));}
  catch{Dispose();throw;}
 }
 public void Commit(){File.Move(temporary!,destination,true);temporary=null;}
 public void Dispose(){if(temporary is {} file){try{File.Delete(file);}catch(IOException){}catch(UnauthorizedAccessException){}temporary=null;}}
}
