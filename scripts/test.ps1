#requires -Version 7.2
[CmdletBinding()]
param([string]$DotnetPath='dotnet',[string]$ExecutablePath,[string]$SyncRecoveryAssemblyPath,[string]$HapPreparationAssemblyPath,[string]$JumpRecoveryAssemblyPath,[string]$TimecodeEditingAssemblyPath,[string]$ResultPath,[string]$FixturesPath,[string]$AudioFixturesPath,[switch]$SkipBuild)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'verification-common.ps1')
if(-not $ResultPath){$ResultPath=Join-Path $repo ('artifacts/tests-'+(Get-Date -Format yyyyMMdd-HHmmss-fff))}
$ResultPath=[IO.Path]::GetFullPath($ResultPath)
if(Test-Path -LiteralPath $ResultPath){throw 'Use a new results directory; previous evidence must not be reused.'}
if($ExecutablePath -and -not $SkipBuild){throw 'Use -SkipBuild when specifying an existing executable.'}
$null=Get-Command ffmpeg -CommandType Application;$null=Get-Command ffprobe -CommandType Application
New-Item -ItemType Directory -Path $ResultPath | Out-Null
$summary=[Collections.Generic.List[object]]::new()
try{
 if(-not $SkipBuild){
  & (Join-Path $PSScriptRoot 'build.ps1') -Verification -DotnetPath $DotnetPath -OutputPath (Join-Path $ResultPath 'app')
  if($LASTEXITCODE -ne 0){throw 'Verification build failed.'}
  $ExecutablePath=Join-Path $ResultPath 'app/LTCue.exe'
  Push-Location $repo
  try{& $DotnetPath build tests/SyncRecovery/SyncRecovery.csproj -c Release;if($LASTEXITCODE -ne 0){throw 'Sync recovery build failed.'};& $DotnetPath build tests/HapPreparation/HapPreparation.csproj -c Release;if($LASTEXITCODE -ne 0){throw 'HAP preparation build failed.'};& $DotnetPath build tests/JumpRecovery/JumpRecovery.csproj -c Release;if($LASTEXITCODE -ne 0){throw 'Jump recovery build failed.'};& $DotnetPath build tests/TimecodeEditing/TimecodeEditing.csproj -c Release;if($LASTEXITCODE -ne 0){throw 'Timecode editing build failed.'}}finally{Pop-Location}
 }
 if(-not $ExecutablePath){$ExecutablePath=Join-Path $repo 'artifacts/verification/LTCue.exe'}
 $ExecutablePath=(Resolve-Path -LiteralPath $ExecutablePath).Path
 $app=Split-Path $ExecutablePath -Parent;$assembly=Join-Path $app 'LTCue.dll'
 $catalogText=Read-VerificationCatalog $assembly
 $expected=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'verification-suites.json') -Raw
 if($catalogText -cne $expected){throw 'Verification binary and suite catalog differ. Rebuild before running.'}
 $catalog=$catalogText | ConvertFrom-Json
 foreach($file in @('coreclr.dll','hostfxr.dll','PlayerNative.dll','AudioNative.dll','LTCue.runtimeconfig.json','TestMedia/HAP_1_1080p_2997_10s.mov','TestMedia/HAP_2_1080p_2997_10s.mov','TestMedia/HAP_3_1080p_2997_10s.mov','TestMedia/HAP_4_1080p_2997_10s.mov','TestMedia/LTC_29.97DF_from-zero.wav')){if(-not(Test-Path -LiteralPath (Join-Path $app $file) -PathType Leaf)){throw "Missing verification application file: $file"}}
 $runtime=Get-Content -LiteralPath (Join-Path $app 'LTCue.runtimeconfig.json') -Raw | ConvertFrom-Json
 if(-not $runtime.runtimeOptions.includedFrameworks){throw 'Use a self-contained verification build to avoid runtime download dialogs.'}
 $sid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value;$session=[Diagnostics.Process]::GetCurrentProcess().SessionId
 try{$existing=[Threading.Mutex]::OpenExisting("LocalLtcPlayer.Instance.$sid.$session.Verification");$existing.Dispose();throw 'Another verification application is running. It was not stopped.'}catch [Threading.WaitHandleCannotBeOpenedException]{}
 if(-not $SyncRecoveryAssemblyPath){$SyncRecoveryAssemblyPath=Join-Path $repo 'tests/SyncRecovery/bin/Release/net10.0/SyncRecovery.dll'}
 $SyncRecoveryAssemblyPath=(Resolve-Path -LiteralPath $SyncRecoveryAssemblyPath).Path
 if(-not $HapPreparationAssemblyPath){$HapPreparationAssemblyPath=Join-Path $repo 'tests/HapPreparation/bin/Release/net10.0/HapPreparation.dll'}
 $HapPreparationAssemblyPath=(Resolve-Path -LiteralPath $HapPreparationAssemblyPath).Path
 if(-not $JumpRecoveryAssemblyPath){$JumpRecoveryAssemblyPath=Join-Path $repo 'tests/JumpRecovery/bin/Release/net10.0/JumpRecovery.dll'}
 $JumpRecoveryAssemblyPath=(Resolve-Path -LiteralPath $JumpRecoveryAssemblyPath).Path
 if(-not $TimecodeEditingAssemblyPath){$TimecodeEditingAssemblyPath=Join-Path $repo 'tests/TimecodeEditing/bin/Release/net10.0/TimecodeEditing.dll'}
 $TimecodeEditingAssemblyPath=(Resolve-Path -LiteralPath $TimecodeEditingAssemblyPath).Path
 $dotnet=(Get-Command $DotnetPath -CommandType Application).Source
 if([bool]$FixturesPath -ne [bool]$AudioFixturesPath){throw 'Specify both fixture paths, or omit both to generate them.'}
 if(-not $FixturesPath){$FixturesPath=Join-Path $ResultPath 'fixtures';& (Join-Path $PSScriptRoot 'new-test-fixtures.ps1') -Path $FixturesPath;$AudioFixturesPath=$FixturesPath}
 $required=@(@($FixturesPath,'h264-aac.mp4'),@($FixturesPath,'h264-aac-sync-30s.mp4'),@($FixturesPath,'hap-pcm.mov'),@($FixturesPath,'hap-q-silent.mov'),@($FixturesPath,'h264-mp3.mp4'),@($FixturesPath,'hap-sync-30s.mov'),@($AudioFixturesPath,'late-pcm.mov'),@($AudioFixturesPath,'early-pcm.mov'),@($AudioFixturesPath,'nonzero-aac.mp4'))
 $hashes=@(foreach($pair in $required){$file=Join-Path $pair[0] $pair[1];if(-not(Test-Path -LiteralPath $file -PathType Leaf) -or (Get-Item -LiteralPath $file).Length -eq 0){throw "Missing/empty fixture: $file"};Get-FileHash -LiteralPath $file -Algorithm SHA256})
 $manifest=[ordered]@{CreatedUtc=[DateTime]::UtcNow.ToString('o');BuildSkipped=[bool]$SkipBuild;Executable=$ExecutablePath;CatalogVersion=$catalog.Version;Files=@(Get-FileHash -LiteralPath $assembly,(Join-Path $app 'PlayerNative.dll'),(Join-Path $app 'AudioNative.dll'),$SyncRecoveryAssemblyPath,$HapPreparationAssemblyPath,$JumpRecoveryAssemblyPath,$TimecodeEditingAssemblyPath -Algorithm SHA256);Fixtures=$hashes;Excluded=@($catalog.Suites | Where-Object Mode -ne Auto);PhysicalValidation='Not performed by this automatic runner.'}
 $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $ResultPath 'manifest.json') -Encoding utf8
 $folder=Join-Path $ResultPath 'sync-calculation';New-Item -ItemType Directory -Path $folder | Out-Null
 $watch=[Diagnostics.Stopwatch]::StartNew()
 $exit=Invoke-VerificationProcess $dotnet @($SyncRecoveryAssemblyPath,(Join-Path $folder 'results.json')) $folder 90
 $items=@(Get-Content -LiteralPath (Join-Path $folder 'results.json') -Raw | ConvertFrom-Json)
 $ok=$exit -eq 0 -and $items.Count -eq 7 -and @($items | Where-Object Passed -ne $true).Count -eq 0
 $summary.Add([ordered]@{Suite='sync-calculation';Status=$(if($ok){'PASS'}else{'FAIL'});Count=$items.Count;ExitCode=$exit;Seconds=$watch.Elapsed.TotalSeconds})
 $folder=Join-Path $ResultPath 'hap-index-calculation';New-Item -ItemType Directory -Path $folder | Out-Null
 $watch=[Diagnostics.Stopwatch]::StartNew()
 $exit=Invoke-VerificationProcess $dotnet @($HapPreparationAssemblyPath,'--test',(Join-Path $folder 'results.json')) $folder 90
 $items=@(Get-Content -LiteralPath (Join-Path $folder 'results.json') -Raw | ConvertFrom-Json)
 $ok=$exit -eq 0 -and $items.Count -eq 23 -and @($items | Where-Object status -ne 'PASS').Count -eq 0
 $summary.Add([ordered]@{Suite='hap-index-calculation';Status=$(if($ok){'PASS'}else{'FAIL'});Count=$items.Count;ExitCode=$exit;Seconds=$watch.Elapsed.TotalSeconds})
 $folder=Join-Path $ResultPath 'jump-recovery-calculation';New-Item -ItemType Directory -Path $folder | Out-Null
 $watch=[Diagnostics.Stopwatch]::StartNew()
 $exit=Invoke-VerificationProcess $dotnet @($JumpRecoveryAssemblyPath,(Join-Path $folder 'results.json')) $folder 90
 $items=@(Get-Content -LiteralPath (Join-Path $folder 'results.json') -Raw | ConvertFrom-Json)
 $ok=$exit -eq 0 -and $items.Count -eq 10 -and @($items | Where-Object status -ne 'PASS').Count -eq 0
 $summary.Add([ordered]@{Suite='jump-recovery-calculation';Status=$(if($ok){'PASS'}else{'FAIL'});Count=$items.Count;ExitCode=$exit;Seconds=$watch.Elapsed.TotalSeconds})
 $folder=Join-Path $ResultPath 'timecode-editing-calculation';New-Item -ItemType Directory -Path $folder | Out-Null
 $watch=[Diagnostics.Stopwatch]::StartNew()
 $exit=Invoke-VerificationProcess $dotnet @($TimecodeEditingAssemblyPath,(Join-Path $folder 'results.json')) $folder 90
 $items=@(Get-Content -LiteralPath (Join-Path $folder 'results.json') -Raw | ConvertFrom-Json)
 $ok=$exit -eq 0 -and $items.Count -eq 32 -and @($items | Where-Object status -ne 'PASS').Count -eq 0
 $summary.Add([ordered]@{Suite='timecode-editing-calculation';Status=$(if($ok){'PASS'}else{'FAIL'});Count=$items.Count;ExitCode=$exit;Seconds=$watch.Elapsed.TotalSeconds})
 foreach($suite in $catalog.Suites | Where-Object Mode -eq Auto){
  if($suite.Name -notmatch '^[a-z0-9-]+$'){throw 'Invalid suite name.'}
  $folder=Join-Path $ResultPath $suite.Name;New-Item -ItemType Directory -Path $folder | Out-Null
  $settingsFile=Join-Path $folder 'settings.json'
  if($suite.Name -eq 'delivery-operations'){'{"Rate":"29.97 DF","OutputDisplayName":"MISSING-VERIFICATION-DISPLAY"}' | Set-Content -LiteralPath $settingsFile -Encoding utf8}
  if($suite.Name -eq 'phase2-adapter'){Copy-Item -LiteralPath (Join-Path $FixturesPath 'h264-aac.mp4') -Destination (Join-Path $folder 'media.mp4')}
  $media=if($suite.Name -eq 'sync-recovery-output'){Join-Path $FixturesPath 'hap-sync-30s.mov'}else{Join-Path $app 'TestMedia/HAP_1_1080p_2997_10s.mov'}
  $arguments=@(('--verify-'+$suite.Name),$folder,'--settings',$settingsFile,'--fixtures',$FixturesPath,'--audio-fixtures',$AudioFixturesPath,'--media',$media,'--probe-output')
  $watch=[Diagnostics.Stopwatch]::StartNew();$failure=$null;$count=0;$exit=$null
  try{$exit=Invoke-VerificationProcess $ExecutablePath $arguments $folder $suite.TimeoutSeconds;if($exit -ne 0){throw "Process exit code: $exit"};$count=Assert-VerificationEvidence $suite $folder}catch{$failure=$_.Exception.Message}
  $status=if($failure){'FAIL'}else{'PASS'}
  $summary.Add([ordered]@{Suite=$suite.Name;Status=$status;Count=$count;ExitCode=$exit;Seconds=$watch.Elapsed.TotalSeconds;Error=$failure})
  $summary | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $ResultPath 'summary.json') -Encoding utf8
  Write-Output "$($suite.Name): $status ($count)"
 }
 if(@($summary | Where-Object Status -ne PASS).Count){throw 'One or more suites failed. See summary.json; later suites were still executed.'}
 Write-Output "All automatic suites passed: $ResultPath"
}catch{[IO.File]::WriteAllText((Join-Path $ResultPath 'runner-failure.txt'),$_.ToString());throw}
