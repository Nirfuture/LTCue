using System.Text.Json;
using System.Text.Json.Serialization;
namespace LtcPlayer.App;
[JsonConverter(typeof(JsonStringEnumConverter<OutputPresentationMode>))]
public enum OutputPresentationMode { Standard, Compatibility }
public static class OutputPresentation
{
    public static void Validate(OutputPresentationMode mode){if(!Enum.IsDefined(mode))throw new InvalidDataException("表示方式の設定が不正です。");}
    public static string Label(OutputPresentationMode mode)=>mode switch{
        OutputPresentationMode.Standard=>"標準（Flip）",
        OutputPresentationMode.Compatibility=>"互換（BitBlt）",
        _=>throw new InvalidDataException("表示方式の設定が不正です。")};
}
public record InputSettings(string DeviceId="",string DeviceName="",int Channel=0,string Rate="25",string Start="00:00:00:00",int Offset=0,double Freewheel=0.1,double Resync=2,OutputPresentationMode OutputMode=OutputPresentationMode.Standard,string CoverPath="",double FadeSeconds=1,string AudioDeviceId="",double AudioVolume=.5)
{
    public static string PathName=>Path.Combine(AppContext.BaseDirectory,"phase2-settings.json");
    public static InputSettings Load(string? path=null)
    {
        path??=PathName;
        if(!File.Exists(path)) return new();
        var settings=JsonSerializer.Deserialize<InputSettings>(File.ReadAllText(path))??new();
        if(!double.IsFinite(settings.AudioVolume)||settings.AudioVolume<0||settings.AudioVolume>1)throw new InvalidDataException("音量は0〜1で指定してください。");OutputPresentation.Validate(settings.OutputMode);if(!double.IsFinite(settings.FadeSeconds)||settings.FadeSeconds<0||settings.FadeSeconds>60)throw new InvalidDataException("フェード時間は0〜60秒で指定してください。");return settings;
    }
    public void Save(string? path=null)
    {
        OutputPresentation.Validate(OutputMode);
        path??=PathName;
        var temp=path+".tmp"; File.WriteAllText(temp,JsonSerializer.Serialize(this,new JsonSerializerOptions{WriteIndented=true}));
        File.Move(temp,path,true);
    }
}
