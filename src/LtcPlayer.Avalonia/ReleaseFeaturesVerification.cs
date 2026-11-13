using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia.Platform;
using Avalonia.Controls;
using LtcPlayer.App;
namespace LtcPlayer.Avalonia;
public static class ReleaseFeaturesVerification
{
 [DllImport("user32.dll")] static extern bool SetWindowPos(nint window,nint after,int x,int y,int w,int h,uint flags);
 [DllImport("user32.dll")] static extern bool GetClientRect(nint window,out Rect rect);
 [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left,Top,Right,Bottom; }
 static void Check(bool value,string message){if(!value)throw new IOException(message);}
 public static async Task RunAsync(MainWindow w,string folder)
 {
  Directory.CreateDirectory(folder);var results=new List<object>();bool failed=false;
  async Task Test(string name,Func<Task<object>> action){try{results.Add(new{test=name,status="PASS",detail=await action()});}catch(Exception e){failed=true;results.Add(new{test=name,status="FAIL",detail=e.ToString()});}}
  try {
   await Test("Bundled zero-start LTC / PCM / DF continuity",()=>{
    using var reader=new BinaryReader(File.OpenRead(Path.Combine(AppContext.BaseDirectory,"TestMedia","LTC_29.97DF_from-zero.wav")));
    Check(new string(reader.ReadChars(4))=="RIFF","RIFF header");reader.ReadInt32();Check(new string(reader.ReadChars(8))=="WAVEfmt ","WAVE format");Check(reader.ReadInt32()==16,"PCM format length");
    Check(reader.ReadInt16()==1&&reader.ReadInt16()==1&&reader.ReadInt32()==48000,"PCM16 mono 48kHz");reader.ReadInt32();reader.ReadInt16();Check(reader.ReadInt16()==16,"16-bit");reader.ReadChars(4);int count=reader.ReadInt32()/2;
    var mode=TimecodeRate.Modes.First(x=>x.Name=="29.97 DF");using var decoder=new LtcDecoder(48000,mode.Fps);var buffer=new float[4096];var frames=new List<long>();
    for(int at=0;at<count;at+=buffer.Length){int n=Math.Min(buffer.Length,count-at);for(int i=0;i<n;i++)buffer[i]=reader.ReadInt16()/32768f;foreach(var tc in decoder.Feed(buffer,n,at)){Check(tc.Drop==1&&tc.Reverse==0,"forward DF");frames.Add(mode.Frames(tc.Hours,tc.Minutes,tc.Seconds,tc.Frame));}}
    Check(frames.Count>=1948&&frames[0]<=1&&frames[^1]>=1948,"frame range");for(int i=1;i<frames.Count;i++)Check(frames[i]==frames[i-1]+1,"DF continuity at "+i);
    return Task.FromResult<object>(new{Samples=count,Duration=count/48000.0,Decoded=frames.Count,First=frames[0],Last=frames[^1]});
   });
   await Test("Embedded app icon",()=>{using var icon=AssetLoader.Open(new Uri("avares://LTCue/Assets/LtcPlayer.ico"));Check(icon.ReadByte()==0&&icon.ReadByte()==0&&icon.ReadByte()==1,"ICO header");return Task.FromResult<object>("Embedded ICO available");});
   await Test("Second process exits while first remains active",async()=>{
    w.WindowState=WindowState.Minimized;
    using var second=Process.Start(new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true,ArgumentList={"--verify-secondary"}})!;
    try{await second.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));Check(second.ExitCode==0,"second instance exit code");for(int i=0;i<100&&w.WindowState==WindowState.Minimized;i++)await Task.Delay(20);Check(w.WindowState!=WindowState.Minimized,"existing window was not restored");return new{second.ExitCode,Restored=true};}
    finally{w.WindowState=WindowState.Normal;if(!second.HasExited){second.Kill();await second.WaitForExitAsync();}}
   });
   await Test("Loading, playback preparation, seek and failure clear activity",async()=>{
    var changes=new List<(bool Busy,string Message)>();void Changed(PreviewSnapshot s){
     changes.Add((w.Preview.IsBusy,w.Preview.BusyMessage));
     Check(w.FindControl<Grid>("ActivitySlot")!.Bounds.Height==28,"activity slot changed height");if(!w.Preview.IsBusy)Check(w.FindControl<ProgressBar>("ActivityBar")!.Opacity==0,"idle progress visible");
     if(w.Preview.IsBusy)Check(!w.FindControl<Slider>("Seek")!.IsEnabled,"seek disabled while loading");
    }w.Preview.Changed+=Changed;
    try {
     await w.Preview.LoadAsync(Path.Combine(AppContext.BaseDirectory,"TestMedia","HAP_1_1080p_2997_10s.mov"));Check(changes.Any(x=>x.Busy&&x.Message.Contains("読み込み")),"load activity");Check(!w.Preview.IsBusy,"load completion");
     changes.Clear();await w.Preview.PlayAsync();Check(changes.Any(x=>x.Busy&&x.Message.Contains("準備")),"play activity");await w.Preview.PauseAsync();
     changes.Clear();await w.Preview.SeekAsync(1);Check(changes.Any(x=>x.Busy&&x.Message.Contains("再生位置"))&&!w.Preview.IsBusy,"seek activity");
     try{await w.Preview.LoadAsync(Path.Combine(folder,"missing.mov"));throw new Exception("missing file accepted");}catch(IOException){}
     Check(!w.Preview.IsBusy&&w.Preview.Snapshot.State==PreviewState.Paused&&w.Preview.LoadedPath!="","failure state");
     await w.Preview.LoadAsync(Path.Combine(AppContext.BaseDirectory,"TestMedia","HAP_1_1080p_2997_10s.mov"));return new{Recovered=w.Preview.Snapshot.State.ToString()};
    }finally{w.Preview.Changed-=Changed;await w.Preview.StopAsync();}
   });
   await Test("Production mirror fills physical pixels and resizes",async()=>{
    var target=w.Surface.BeginProgram();try {
     var display=VideoRenderer.Displays()[0];using var renderer=new VideoRenderer(display);renderer.AttachMonitor(target);
     var pixels=new byte[display.Width*display.Height*4];for(int i=0;i<pixels.Length;i+=4){pixels[i+1]=255;pixels[i+3]=255;}
     var sizes=new List<object>();
     foreach(var size in new[]{(Width:840,Height:472),(Width:504,Height:284)}){
      Check(SetWindowPos(target,0,0,0,size.Width,size.Height,0x14),"resize child");Check(GetClientRect(target,out var client),"client rect");
      ulong before=renderer.MonitorStatus().Rendered;renderer.Present(pixels);await PreviewRecoveryVerification.Fresh(renderer,before);
      var stats=renderer.MonitorStats();Check(stats.Width==client.Right&&stats.Height==client.Bottom,"monitor pixel size mismatch");
      var mirror=renderer.MonitorReadback();int firstX=stats.Width,lastX=0,firstY=stats.Height,lastY=0;
      for(int y=0;y<stats.Height;y++)for(int x=0;x<stats.Width;x++){int p=(y*stats.Width+x)*4;if(mirror[p+1]>200){firstX=Math.Min(firstX,x);lastX=Math.Max(lastX,x);firstY=Math.Min(firstY,y);lastY=Math.Max(lastY,y);}}
      double scale=Math.Min(stats.Width/(double)display.Width,stats.Height/(double)display.Height);
      Check(Math.Abs(lastX-firstX+1-display.Width*scale)<=2&&Math.Abs(lastY-firstY+1-display.Height*scale)<=2,"mirror does not fill available area");
      sizes.Add(new{stats.Width,stats.Height,ContentWidth=lastX-firstX+1,ContentHeight=lastY-firstY+1});
     }
     return sizes;
    }finally{w.Surface.EndProgram();}
   });
  }finally{await w.Monitor.DisposeAsync();File.WriteAllText(Path.Combine(folder,"results.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));MainWindow.Shutdown(failed?1:0);}
 }
}
