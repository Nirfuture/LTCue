using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Channels;
using LtcPlayer.App;
namespace LtcPlayer.Avalonia;
public record PreviewAudioInfo(string Codec,bool Supported,string Notice);
public record AudioProgress(ulong Written=0,ulong Consumed=0,float Peak=0,bool Running=false,bool Done=false)
{public double Seconds=>Consumed/48000.0;}
// Each session owns one MTA worker, one FFmpeg child and one WASAPI stream.
public sealed class PreviewAudio : IAsyncDisposable
{
 readonly CancellationTokenSource cancel=new();
 readonly TaskCompletionSource primed=new(TaskCreationOptions.RunContinuationsAsynchronously);
 readonly Channel<float[]> blocks=Channel.CreateBounded<float[]>(8);
 readonly Task worker;
 volatile bool pause=true;float gain=.5f;AudioProgress progress=new();Exception? error;
 public AudioProgress Progress=>Volatile.Read(ref progress);
 public Exception? Error=>Volatile.Read(ref error);
 public float Gain{get=>Volatile.Read(ref gain);set=>Volatile.Write(ref gain,Math.Clamp(value,0,1));}
 public static int ActiveStreams=>Native.pa_active();
 public PreviewAudio(string path,double seek,double duration,float volume)
 {gain=volume;worker=Task.Run(()=>Run(path,seek,duration));}
 public Task ReadyAsync(CancellationToken token)=>primed.Task.WaitAsync(TimeSpan.FromSeconds(15),token);
 public async Task SetPausedAsync(bool value,CancellationToken token)
 {
  pause=value;var limit=Stopwatch.StartNew();
  while(!Progress.Done&&Progress.Running==value){if(Error is {} e)throw e;token.ThrowIfCancellationRequested();if(limit.Elapsed.TotalSeconds>3)throw new TimeoutException("WASAPI pause/start timeout");await Task.Delay(4,token);}
 }
 void Run(string path,double seek,double duration)
 {
  nint audio=0;Task? producer=null;
  try {
   Check(Native.pa_open(out audio));producer=Task.Run(()=>Decode(path,seek,duration,cancel.Token));
   float[]? pending=null;int offset=0;bool running=false;
   while(!cancel.IsCancellationRequested) {
    if(pending==null&&blocks.Reader.TryRead(out var next)){pending=next;offset=0;}
    if(pending!=null) {
     Check(Native.pa_write(audio,pending,offset,pending.Length/2-offset,Gain,out int n));offset+=n;
     if(offset==pending.Length/2)pending=null;
    }
    Check(Native.pa_stats(audio,out ulong written,out ulong consumed,out float peak));
    if(written>0)primed.TrySetResult();
    bool desired=written>0&&!pause;
    if(desired!=running){Check(Native.pa_run(audio,desired?1:0));running=desired;}
    Volatile.Write(ref progress,new(written,consumed,peak,running));
    if(blocks.Reader.Completion.IsFaulted)blocks.Reader.Completion.GetAwaiter().GetResult();
    if(pending==null&&blocks.Reader.Completion.IsCompleted&&consumed==written) {
     Volatile.Write(ref progress,new(written,consumed,peak,false,true));break;
    }
    Thread.Sleep(4);
   }
  }catch(Exception e){Volatile.Write(ref error,e);primed.TrySetException(e);}
  finally {
   cancel.Cancel();if(producer!=null){try{producer.GetAwaiter().GetResult();}catch(OperationCanceledException){}catch(Exception e){Volatile.Write(ref error,e);}}
   if(audio!=0)Native.pa_close(audio);primed.TrySetCanceled();
  }
 }
 async Task Decode(string path,double seek,double duration,CancellationToken token)
 {
  using var p=MediaDecoder.Start("ffmpeg",["-hide_banner","-v","error","-nostdin","-ss",MediaDecoder.FfmpegTime(seek),"-i",path,"-map","0:a:0","-vn","-sn","-af","aresample=async=1:first_pts=0,apad","-t",MediaDecoder.FfmpegTime(Math.Max(.001,duration-seek)),"-ar","48000","-ac","2","-c:a","pcm_f32le","-f","f32le","pipe:1"]);
  using var reg=token.Register(()=>MediaDecoder.Kill(p));var errors=p.StandardError.ReadToEndAsync();
  try {
   while(true){var bytes=new byte[2048*8];int at=0;
    while(at<bytes.Length){int n=await p.StandardOutput.BaseStream.ReadAsync(bytes.AsMemory(at),token);if(n==0)break;at+=n;}
    if(at==0)break;if(at%8!=0)throw new InvalidDataException("Audio frame truncated");
    var samples=new float[at/4];Buffer.BlockCopy(bytes,0,samples,0,at);await blocks.Writer.WriteAsync(samples,token);
   }
   await p.WaitForExitAsync(token);string detail=await errors;if(p.ExitCode!=0)throw new IOException("FFmpeg audio: "+detail);
   blocks.Writer.TryComplete();
  }catch(Exception e){blocks.Writer.TryComplete(e);throw;}
  finally{MediaDecoder.Kill(p);await errors;}
 }
 public async ValueTask DisposeAsync(){cancel.Cancel();await worker;cancel.Dispose();}
 public static async Task<PreviewAudioInfo> InspectAsync(string path,CancellationToken token)
 {
  using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(15));
  StartupMeasurementTrace.Mark("selection_audio_probe_begin");
  using var p=MediaDecoder.Start("ffprobe",["-v","error","-select_streams","a:0","-show_entries","stream=codec_name","-of","json",path]);
  using var reg=timeout.Token.Register(()=>MediaDecoder.Kill(p));var output=p.StandardOutput.ReadToEndAsync();var errors=p.StandardError.ReadToEndAsync();
  await p.WaitForExitAsync(timeout.Token);string detail=await errors;if(p.ExitCode!=0)throw new IOException(detail);
  using var doc=JsonDocument.Parse(await output);var streams=doc.RootElement.GetProperty("streams");
  StartupMeasurementTrace.Mark("selection_audio_probe_complete");
  if(streams.GetArrayLength()==0)return new("",false,"音声なし");
  string codec=streams[0].GetProperty("codec_name").GetString()??"";bool supported=codec=="aac"||codec.StartsWith("pcm_",StringComparison.Ordinal);
  return new(codec,supported,supported?$"{codec} / Windows既定出力":$"{codec}はプレビュー音声対象外（映像のみ）");
 }
 static void Check(int hr){if(hr<0)throw new IOException($"WASAPI preview failure 0x{hr:X8}");}
 static class Native
 {
  const string D="PlayerNative.dll";
  [DllImport(D,CallingConvention=CallingConvention.Cdecl)]internal static extern int pa_open(out nint h);
  [DllImport(D,CallingConvention=CallingConvention.Cdecl)]internal static extern int pa_write(nint h,float[] samples,int offset,int frames,float gain,out int written);
  [DllImport(D,CallingConvention=CallingConvention.Cdecl)]internal static extern int pa_run(nint h,int running);
  [DllImport(D,CallingConvention=CallingConvention.Cdecl)]internal static extern int pa_stats(nint h,out ulong written,out ulong consumed,out float peak);
  [DllImport(D,CallingConvention=CallingConvention.Cdecl)]internal static extern void pa_close(nint h);
  [DllImport(D,CallingConvention=CallingConvention.Cdecl)]internal static extern int pa_active();
 }
}
