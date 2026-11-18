using System.Text.Json;
using System.Text.Json.Serialization;
namespace LtcPlayer.App;
[JsonConverter(typeof(JsonStringEnumConverter<OutputPresentationMode>))]
public enum OutputPresentationMode { Standard, Compatibility }
public static class OutputPresentation
{
    public static void Validate(OutputPresentationMode mode){if(!Enum.IsDefined(mode))throw new InvalidDataException("表示方式の設定不正");}
    public static string Label(OutputPresentationMode mode)=>mode switch{
        OutputPresentationMode.Standard=>"標準",
        OutputPresentationMode.Compatibility=>"互換",
        _=>throw new InvalidDataException("表示方式の設定不正")};
}
public record InputSettings(string DeviceId="",string DeviceName="",int Channel=0,string Rate="25",string Start="00:00:00:00",int Offset=0,double Freewheel=0.1,double Resync=2,OutputPresentationMode OutputMode=OutputPresentationMode.Standard,string CoverPath="",double FadeSeconds=1,string AudioDeviceId="",double AudioVolume=.5)
{
    public CueFormatPreferences CueFormat { get; init; } = new();
    public CuePlacementPreferences CuePlacement { get; init; } = new();
    public static string PathName=>Path.Combine(AppContext.BaseDirectory,"phase2-settings.json");
    public static InputSettings Load(string? path=null)
    {
        path??=PathName;
        if(!File.Exists(path)) return new();
        var json=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path));
        // Restore the former hidden mode as the default now that resolution has its own column.
        if(json?["CueFormat"] is System.Text.Json.Nodes.JsonObject format && format["Mode"] is {} mode &&
           (mode.ToJsonString()=="\"Hidden\"" || mode.ToJsonString()=="2"))format["Mode"]="Preset";
        var settings=json?.Deserialize<InputSettings>()??new();
        if(!double.IsFinite(settings.AudioVolume)||settings.AudioVolume<0||settings.AudioVolume>1)throw new InvalidDataException("音量は0～1");OutputPresentation.Validate(settings.OutputMode);if(!double.IsFinite(settings.FadeSeconds)||settings.FadeSeconds<0||settings.FadeSeconds>60)throw new InvalidDataException("フェードは0～60秒");if(settings.CuePlacement==null)throw new InvalidDataException("追加時の開始TC設定が不正です");settings.CuePlacement.Validate();settings.CueFormat.Validate();return settings;
    }
    public void Save(string? path=null)
    {
        OutputPresentation.Validate(OutputMode);CuePlacement.Validate();CueFormat.Validate();
        path??=PathName;
        var temp=path+".tmp"; File.WriteAllText(temp,JsonSerializer.Serialize(this,new JsonSerializerOptions{WriteIndented=true}));
        File.Move(temp,path,true);
    }
}

[JsonConverter(typeof(JsonStringEnumConverter<CueResolutionMode>))]
public enum CueResolutionMode { Preset, Dimensions }
public sealed record ResolutionPreset(int Width, int Height, string Name);
public sealed record CueFormatPreferences
{
 public CueResolutionMode Mode { get; init; }
 public ResolutionPreset[] Presets { get; init; } = [new(1280,720,"HD"),new(1920,1080,"FHD"),new(3840,2160,"UHD")];
 public void Validate()
 {
  if(!Enum.IsDefined(Mode)||Presets==null)throw new InvalidDataException("形式の表示設定が不正です");
  var seen=new Dictionary<(int,int),int>();
  for(int i=0;i<Presets.Length;i++){
   var p=Presets[i];var label=$"プリセット {i+1}行目：";
   if(p==null||p.Width<=0||p.Height<=0)throw new InvalidDataException(label+"幅・高さは正の整数を入力してください");
   if(string.IsNullOrWhiteSpace(p.Name)||p.Name!=p.Name.Trim()||new System.Globalization.StringInfo(p.Name).LengthInTextElements>8)throw new InvalidDataException(label+"表示名は1～8文字を入力してください");
   if(seen.TryGetValue((p.Width,p.Height),out int previous))throw new InvalidDataException(label+$"{previous}行目と寸法が重複しています");
   seen[(p.Width,p.Height)]=i+1;
  }
 }
 public string Resolution(int width,int height)
 {
  if(width<=0||height<=0)return "";
  return Mode==CueResolutionMode.Dimensions ? $"{width}×{height}" : Presets.FirstOrDefault(p=>p.Width==width&&p.Height==height)?.Name??"未登録";
 }
 public string Format(string codec,int width,int height,bool tooltip=false)
 {
  string resolution=tooltip ? width>0&&height>0 ? $"{width}×{height}" : "" : Resolution(width,height);
  return string.Join(" · ",new[]{codec,resolution}.Where(v=>!string.IsNullOrWhiteSpace(v)));
 }
}
