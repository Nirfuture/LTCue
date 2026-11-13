#requires -Version 7.2
param([Parameter(Mandatory)][string]$ProductAssemblyPath,[Parameter(Mandatory)][string]$ResultPath)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'verification-common.ps1')
if(Test-Path -LiteralPath $ResultPath){throw 'Use a fresh runner-check directory.'}
New-Item -ItemType Directory -Path $ResultPath | Out-Null
$results=[Collections.Generic.List[object]]::new()
function Check([string]$Name,[scriptblock]$Body){try{& $Body;$results.Add(@{Test=$Name;Status='PASS'})}catch{$results.Add(@{Test=$Name;Status='FAIL';Detail=$_.ToString()})}}
function Reject([scriptblock]$Body){$rejected=$false;try{& $Body | Out-Null}catch{$rejected=$true};if(-not $rejected){throw 'Expected rejection did not occur.'}}
$suite=[pscustomobject]@{Kind='Status';Evidence='results.json';Count=2}
$file=Join-Path $ResultPath 'results.json'
$valid='[{"test":"one","status":"PASS"},{"test":"two","status":"PASS"}]'
Check 'complete evidence accepted' {Set-Content -LiteralPath $file $valid;if((Assert-VerificationEvidence $suite $ResultPath) -ne 2){throw 'Wrong count.'}}
Check 'empty evidence rejected' {Set-Content -LiteralPath $file '[]';Reject {Assert-VerificationEvidence $suite $ResultPath}}
Check 'missing assertion rejected' {Set-Content -LiteralPath $file '[{"test":"one","status":"PASS"}]';Reject {Assert-VerificationEvidence $suite $ResultPath}}
Check 'failed assertion rejected' {Set-Content -LiteralPath $file '[{"test":"one","status":"PASS"},{"test":"two","status":"FAIL"}]';Reject {Assert-VerificationEvidence $suite $ResultPath}}
Check 'missing evidence rejected' {Remove-Item -LiteralPath $file;Reject {Assert-VerificationEvidence $suite $ResultPath}}
Check 'fatal record overrides passing evidence' {Set-Content -LiteralPath $file $valid;Set-Content -LiteralPath (Join-Path $ResultPath 'fatal.txt') 'synthetic';Reject {Assert-VerificationEvidence $suite $ResultPath};Remove-Item -LiteralPath (Join-Path $ResultPath 'fatal.txt')}
Check 'normal product rejected without launching it' {Reject {Read-VerificationCatalog $ProductAssemblyPath}}
Check 'existing result directory rejected before launch' {Reject {& (Join-Path $PSScriptRoot 'test.ps1') -SkipBuild -ResultPath $ResultPath}}
Check 'owned child process timeout enforced' {
 $folder=Join-Path $ResultPath 'timeout';New-Item -ItemType Directory -Path $folder | Out-Null
 $rejected=$false;try{Invoke-VerificationProcess (Get-Process -Id $PID).Path @('-NoProfile','-NonInteractive','-Command','Start-Sleep -Seconds 10') $folder 1 | Out-Null}catch{if($_.Exception.Message -like 'Timeout after*'){$rejected=$true}else{throw}}
 if(-not $rejected){throw 'Timeout was not enforced.'}
}
$results | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $ResultPath 'runner-checks.json') -Encoding utf8
if(@($results | Where-Object Status -ne PASS).Count){throw 'Runner acceptance checks failed.'}
Write-Output "Runner checks passed: $($results.Count)"
