$ErrorActionPreference='Stop'
$folder=$PSScriptRoot
$bin='C:/Users/izumi/AppData/Local/Programs/FFmpeg/8.1.2-full/ffmpeg-8.1.2-full_build/bin'
$ff="$bin/ffmpeg.exe"
$probe="$bin/ffprobe.exe"
$video='testsrc2=size=1920x1080:rate=30:duration=240'
$audio='aevalsrc=0.04*sin(2*PI*440*t)|0.04*sin(2*PI*660*t):s=48000:d=240'
$overlay="drawtext=fontfile='C\:/Windows/Fonts/consola.ttf':text='LTCue 4 MIN TEST':x=24:y=20:fontsize=36:fontcolor=white:box=1:boxcolor=black@0.7,drawtext=fontfile='C\:/Windows/Fonts/consola.ttf':text='%{pts\:hms}  frame %{n}':x=24:y=70:fontsize=40:fontcolor=white:box=1:boxcolor=black@0.7"
$common=@('-hide_banner','-nostdin','-loglevel','warning','-n','-f','lavfi','-i',$video,'-f','lavfi','-i',$audio,'-map','0:v:0','-map','1:a:0','-vf',$overlay,'-t','240')
& $ff @common -c:v hap -format hap -c:a pcm_s24le -ar 48000 -ac 2 "$folder/test-4min-fullhd-hap-pcm24.mov" 2> "$folder/mov-encode.log"
if($LASTEXITCODE){throw 'MOV encoding failed'}
& $ff @common -c:v libx264 -preset fast -crf 23 -pix_fmt yuv420p -c:a aac -b:a 192k -ar 48000 -ac 2 -movflags +faststart "$folder/test-4min-fullhd-h264-aac.mp4" 2> "$folder/mp4-encode.log"
if($LASTEXITCODE){throw 'MP4 encoding failed'}
$results=foreach($file in Get-ChildItem $folder -File | Where-Object Extension -in '.mov','.mp4'){
 $json=& $probe -v error -show_streams -show_format -of json $file.FullName
 if($LASTEXITCODE){throw 'ffprobe failed'}
 $json | Set-Content "$($file.FullName).ffprobe.json"
 $data=$json | ConvertFrom-Json
 $v=$data.streams | Where-Object codec_type -eq video
 $a=$data.streams | Where-Object codec_type -eq audio
 if([Math]::Abs([double]$v.duration-240) -gt 0.034 -or $v.width -ne 1920 -or $v.height -ne 1080 -or $v.nb_frames -ne '7200' -or $a.sample_rate -ne '48000' -or $a.channels -ne 2){throw 'Unexpected fixture properties'}
 [pscustomobject]@{Path=$file.FullName;Bytes=$file.Length;SHA256=(Get-FileHash $file.FullName).Hash;Duration=$data.format.duration;VideoCodec=$v.codec_name;VideoTag=$v.codec_tag_string;Frames=$v.nb_frames;AudioCodec=$a.codec_name;AudioRate=$a.sample_rate;Channels=$a.channels}
}
$results | ConvertTo-Json -Depth 5 | Set-Content "$folder/manifest.json"
& $ff -version | Set-Content "$folder/ffmpeg-version.txt"
$results | ConvertTo-Json -Depth 5

