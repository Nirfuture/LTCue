param([Parameter(Mandatory)][string]$Path)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
if(Test-Path -LiteralPath $Path){throw 'Fixture directory must be new.'}
New-Item -ItemType Directory -Path $Path | Out-Null
function Encode([string[]]$Arguments){ & ffmpeg -hide_banner -nostdin -loglevel error @Arguments; if($LASTEXITCODE -ne 0){throw 'Fixture generation failed.'} }
Encode @('-f','lavfi','-i','testsrc2=size=1920x1080:rate=25','-f','lavfi','-i','sine=frequency=440:sample_rate=48000','-t','6','-c:v','libx264','-preset','ultrafast','-pix_fmt','yuv420p','-c:a','aac',"$Path/h264-aac.mp4")
Encode @('-f','lavfi','-i','testsrc2=size=640x360:rate=25','-f','lavfi','-i','sine=frequency=660:sample_rate=48000','-t','4','-c:v','hap','-format','hap','-c:a','pcm_s16le',"$Path/hap-pcm.mov")
Encode @('-f','lavfi','-i','testsrc2=size=640x360:rate=25','-t','1','-c:v','hap','-format','hap_q',"$Path/hap-q-silent.mov")
Encode @('-i',"$Path/h264-aac.mp4",'-c:v','copy','-c:a','libmp3lame',"$Path/h264-mp3.mp4")
Encode @('-stream_loop','2','-i',"$repo/TestMedia/HAP_1_1080p_2997_10s.mov",'-c','copy',"$Path/hap-sync-30s.mov")
Encode @('-f','lavfi','-i','testsrc2=size=640x360:rate=25:duration=3','-itsoffset','0.4','-f','lavfi','-i','sine=frequency=440:sample_rate=48000:duration=2.6','-map','0:v','-map','1:a','-c:v','hap','-c:a','pcm_f32le',"$Path/late-pcm.mov")
Encode @('-copyts','-itsoffset','0.48','-f','lavfi','-i','testsrc2=size=640x360:rate=25:duration=2','-f','lavfi','-i','aevalsrc=0.1+0.1*t|0.1+0.1*t:s=48000:d=2.5','-map','0:v','-map','1:a','-c:v','hap','-c:a','pcm_f32le','-fps_mode','passthrough',"$Path/early-pcm.mov")
Encode @('-i',"$Path/h264-aac.mp4",'-c','copy','-output_ts_offset','5','-movie_timescale','48000',"$Path/nonzero-aac.mp4")
