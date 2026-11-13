param([string]$NativeDll=(Join-Path $PSScriptRoot '../native/build/AudioNative.dll'))
$ErrorActionPreference='Stop'
$dll=(Resolve-Path -LiteralPath $NativeDll).Path.Replace('\','\\').Replace('"','\"')
$code=@"
using System;
using System.IO;
using System.Runtime.InteropServices;
public static class TestLtcWave {
 [DllImport("$dll",CallingConvention=CallingConvention.Cdecl)]
 static extern int tc_generate(double fps,int drop,int h,int m,int s,int f,int frames,[Out]float[] data,int capacity);
 public static void Write(string path) {
  const int frames=1950;
  var samples=new float[3123120+4800];
  int count=tc_generate(30000.0/1001,1,0,0,0,0,frames,samples,samples.Length);
  if(count<=0)throw new Exception("LTC generation failed: "+count);
  using(var w=new BinaryWriter(File.Create(path))) {
   w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));w.Write(36+count*2);
   w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));w.Write(16);
   w.Write((short)1);w.Write((short)1);w.Write(48000);w.Write(96000);w.Write((short)2);w.Write((short)16);
   w.Write(System.Text.Encoding.ASCII.GetBytes("data"));w.Write(count*2);
   for(int i=0;i<count;i++)w.Write((short)Math.Round(samples[i]*16384));
  }
 }
}
"@
Add-Type -TypeDefinition $code
$output=Join-Path $PSScriptRoot '../TestMedia/LTC_29.97DF_from-zero.wav'
[TestLtcWave]::Write($output)
Write-Output "Generated: $output (29.97 DF, 00:00:00:00, 1950 frames, mono PCM16/48kHz)"
