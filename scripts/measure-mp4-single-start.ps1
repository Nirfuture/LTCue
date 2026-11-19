param([Parameter(Mandatory)][string]$Executable,[Parameter(Mandatory)][string]$ResultPath)
$ErrorActionPreference='Stop'
if(Test-Path $ResultPath){throw 'Result path exists'}
New-Item -ItemType Directory $ResultPath | Out-Null
$repo=Split-Path $PSScriptRoot -Parent
$bin='C:/Users/izumi/AppData/Local/Programs/FFmpeg/8.1.2-full/ffmpeg-8.1.2-full_build/bin'
$env:PATH="$bin;$env:PATH"
$hd=Join-Path $repo 'TestMedia/LongTest-FullHD-20261119-234052/test-4min-fullhd-h264-aac.mp4'
$sd=Join-Path $repo 'TestMedia/LongTest-20261119-233910/test-4min-h264-aac.mp4'
$mov=Join-Path $repo 'TestMedia/LongTest-FullHD-20261119-234052/test-4min-fullhd-hap-pcm24.mov'
Get-FileHash @($hd,$sd,$mov,"$bin/ffmpeg.exe","$bin/ffprobe.exe") | ConvertTo-Json | Set-Content "$ResultPath/hashes.json"
& "$bin/ffmpeg.exe" -version > "$ResultPath/ffmpeg-version.txt"
$cases=foreach($audio in 'off','on'){foreach($name in 'fullhd','720p','mov','switch'){[pscustomobject]@{Name="$name-$audio";Audio=$audio;Media=$(if($name -eq '720p'){$sd}elseif($name -eq 'mov'){$mov}else{$hd});Switch=$name -eq 'switch'}}}
foreach($case in $cases){
 $folder=Join-Path $ResultPath $case.Name
 New-Item -ItemType Directory $folder | Out-Null
 $arguments=@('--verify-startup-measure',"`"$folder`"",'--media',"`"$($case.Media)`"",'--startup-audio',$case.Audio,'--settings',"`"$folder/settings.json`"")
 if($case.Switch){@($hd,$sd) | Set-Content "$folder/materials.txt"; $arguments+=@('--startup-switch','on','--startup-materials',"`"$folder/materials.txt`"")}
 $p=Start-Process -FilePath $Executable -ArgumentList $arguments -PassThru -WindowStyle Hidden -RedirectStandardOutput "$folder/stdout.log" -RedirectStandardError "$folder/stderr.log"
 @{Pid=$p.Id;StartTime=$p.StartTime;Executable=$Executable;Arguments=$arguments} | ConvertTo-Json | Set-Content "$folder/process.json"
 if(!$p.WaitForExit(180000)){$p.Kill($true);$null=$p.WaitForExit(5000);throw "Timeout: $($case.Name)"}
 if($p.ExitCode -ne 0){throw "Failed: $($case.Name) exit $($p.ExitCode)"}
 Write-Output "PASS $($case.Name)"
}
