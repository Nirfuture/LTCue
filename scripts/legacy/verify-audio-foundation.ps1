# Legacy focused runner. Prefer scripts/test.ps1 for complete validation.
param([string]$ExecutablePath,[string]$ResultPath)
$ErrorActionPreference='Stop'
$repo=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
if(-not $ExecutablePath){$ExecutablePath=Join-Path $repo 'artifacts/verification/LTCue.exe'}
if(-not $ResultPath){$ResultPath=Join-Path $repo ('artifacts/audio-v06-'+(Get-Date -Format yyyyMMdd-HHmmss-fff))}
if(-not(Test-Path -LiteralPath $ExecutablePath)){throw 'Build with scripts/build.ps1 -Verification first.'}
$resultFile=Join-Path $ResultPath 'results.json'
if(Test-Path -LiteralPath $resultFile){throw 'Use a new result directory so prior evidence cannot be mistaken for this run.'}
New-Item -ItemType Directory -Force $ResultPath | Out-Null
& $ExecutablePath --verify-audio-foundation $ResultPath --settings (Join-Path $ResultPath 'settings.json') --probe-output | Out-Null
if($LASTEXITCODE -ne 0){throw "Audio foundation verification failed: $ResultPath"}
if(-not(Test-Path -LiteralPath $resultFile)){throw 'No verification evidence. Ensure this is a verification build and no other verification instance is running.'}
$results=Get-Content -LiteralPath $resultFile -Raw | ConvertFrom-Json
if(@($results | Where-Object status -ne PASS).Count -ne 0){throw "Audio foundation tests did not all pass: $ResultPath"}
Write-Output $ResultPath
