# Legacy focused runner. Prefer scripts/test.ps1 for complete validation.
param([string]$ExecutablePath,[string]$ResultPath,[string]$Fixtures,[string]$AudioFixtures)
$ErrorActionPreference='Stop'
$repo=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
if(-not $ExecutablePath){$ExecutablePath=Join-Path $repo 'artifacts/verification/LTCue.exe'}
if(-not $ResultPath){$ResultPath=Join-Path $repo ('artifacts/audio-playback-'+(Get-Date -Format yyyyMMdd-HHmmss-fff))}
if(-not $Fixtures){$Fixtures=Join-Path $repo 'artifacts/redesign/fixtures'}
if(-not $AudioFixtures){$AudioFixtures=Join-Path $repo 'artifacts/audio-v06/fixtures'}
foreach($file in @('h264-aac.mp4','hap-pcm.mov','hap-q-silent.mov','h264-mp3.mp4')){if(-not(Test-Path -LiteralPath (Join-Path $Fixtures $file))){throw "Missing fixture: $file"}}
foreach($file in @('late-pcm.mov','early-pcm.mov','nonzero-aac.mp4')){if(-not(Test-Path -LiteralPath (Join-Path $AudioFixtures $file))){throw "Missing audio-offset fixture: $file"}}
if(Test-Path -LiteralPath $ResultPath){throw 'Use a fresh result directory.'}
foreach($case in @(@('decode',8),@('service',8),@('connected',8),@('ui',7))){
 $folder=Join-Path $ResultPath $case[0]
 New-Item -ItemType Directory -Force $folder | Out-Null
 & $ExecutablePath ("--verify-audio-"+$case[0]) $folder --fixtures $Fixtures --audio-fixtures $AudioFixtures --settings (Join-Path $folder 'settings.json') --probe-output | Out-Null
 if($LASTEXITCODE -ne 0){throw "Audio playback verification failed: $folder"}
 $resultFile=Join-Path $folder 'results.json'
 if(-not(Test-Path -LiteralPath $resultFile)){throw "No evidence: $folder"}
 $items=@(Get-Content -LiteralPath $resultFile -Raw | ConvertFrom-Json)
 if($items.Count -ne $case[1] -or @($items | Where-Object status -ne PASS).Count -ne 0){throw "Unexpected verification results: $folder"}
}
Write-Output $ResultPath
