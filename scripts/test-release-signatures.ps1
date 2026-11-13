param(
 [string]$Path = (Join-Path $PSScriptRoot '../artifacts/win-x64'),
 [string]$ReportPath,
 [switch]$ReportOnly
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $Path).Path
$required = @('LTCue.exe', 'AudioNative.dll', 'PlayerNative.dll')
foreach ($name in $required) {
 if (-not (Test-Path -LiteralPath (Join-Path $root $name) -PathType Leaf)) {
  throw "Required release file missing: $name"
 }
}
$files = @(Get-ChildItem -LiteralPath $root -Recurse -File | Where-Object { $_.Extension -in '.exe', '.dll' })
$results = @($files | ForEach-Object {
 $signature = Get-AuthenticodeSignature -LiteralPath $_.FullName
 [pscustomobject]@{
  File = [IO.Path]::GetRelativePath($root, $_.FullName)
  Status = [string]$signature.Status
  Publisher = if ($signature.SignerCertificate) { $signature.SignerCertificate.Subject } else { $null }
  Timestamped = $null -ne $signature.TimeStamperCertificate
 }
})
if ($ReportPath) {
 $results | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath $ReportPath -Encoding utf8
}
$failures = @($results | Where-Object { $_.Status -ne 'Valid' })
if ($failures.Count -gt 0 -and -not $ReportOnly) {
 throw "Release signature verification failed for $($failures.Count) file(s). Do not publish as a signed release."
}
if($failures.Count -gt 0){Write-Warning "$($failures.Count) EXE/DLL files have no valid Authenticode signature. This is an unsigned preview package."}
else{Write-Output 'All EXE/DLL Authenticode signatures are valid on this PC. Verify launch on a clean Smart App Control-enabled PC separately.'}
