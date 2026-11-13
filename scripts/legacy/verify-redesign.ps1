# Legacy focused runner. Prefer scripts/test.ps1 for complete validation.
param(
 [string]$DotnetPath='dotnet',
 [string]$AssemblyPath,
 [string]$ResultPath,
 [string]$FixturesPath
)
$ErrorActionPreference='Stop'
$repo=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
if(-not $AssemblyPath){$AssemblyPath=Join-Path $repo 'artifacts/verification/LTCue.dll'}
if(-not $ResultPath){$ResultPath=Join-Path $repo ('artifacts/redesign-'+(Get-Date -Format yyyyMMdd-HHmmss))}
if(-not(Test-Path -LiteralPath $AssemblyPath)){throw 'Build with -p:EnableVerification=true before running this script.'}
New-Item -ItemType Directory -Force $ResultPath | Out-Null
foreach($name in @('ui-corrections','ui-design','coordinator','preview-ui','playback-integration','shared-canvas','output-mode','presentation','manual-output','responsive','monitor')){
 $folder=Join-Path $ResultPath $name
 $extra=if($FixturesPath){@('--fixtures',$FixturesPath)}else{@()}
 & $DotnetPath $AssemblyPath ('--verify-'+$name) $folder @extra --media (Join-Path $repo 'TestMedia/HAP_1_1080p_2997_10s.mov') --settings (Join-Path $ResultPath ($name+'-settings.json')) --probe-output
 if($LASTEXITCODE -ne 0){throw "Verification failed: $name"}
 # A regular product build ignores verification flags; require proof of actual execution.
 $evidence=switch($name){'redesign-baseline'{'observations.json'} 'monitor'{'exit-code.txt'} default {'results.json'}}
 if(-not(Test-Path -LiteralPath (Join-Path $folder $evidence))){throw "Verification did not produce evidence: $name"}
}
Write-Output $ResultPath
