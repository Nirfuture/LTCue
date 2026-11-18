param(
 [Parameter(Mandatory)][string]$App,
 [Parameter(Mandatory)][string]$Folder,
 [Parameter(Mandatory)][string[]]$Media,
 [switch]$Reverse,
 [switch]$FadeOnly
)
$ErrorActionPreference='Stop'
. "$PSScriptRoot/verification-common.ps1"
$App=[IO.Path]::GetFullPath($App)
$Folder=[IO.Path]::GetFullPath($Folder)
if(Test-Path -LiteralPath $Folder){throw 'Use a new evidence directory'}
New-Item -ItemType Directory -Path $Folder | Out-Null
$repo=Split-Path $PSScriptRoot -Parent
$sources=@(Get-ChildItem "$repo/src","$repo/native" -Recurse -File | Where-Object { $_.Extension -in '.cs','.inc','.cpp','.csproj' -and $_.FullName -notmatch '\\(bin|obj|build)\\' } | Get-FileHash)
@{Commit=(git -C $repo rev-parse HEAD);Diff=(git -C $repo diff);Reverse=[bool]$Reverse;SourcesAtInvocation=$sources;Binaries=@(Get-FileHash $App,(Join-Path (Split-Path $App) 'LTCue.dll'),(Join-Path (Split-Path $App) 'PlayerNative.dll'));Media=@($Media | Get-FileHash)} | ConvertTo-Json -Depth 5 | Set-Content "$Folder/manifest.json"
$index=0
foreach($file in $Media){
 $index++;$destination=Join-Path $Folder "media-$index"
 New-Item -ItemType Directory -Path $destination | Out-Null
 $arguments=@('--verify-cover-toggle-profile',$destination,'--settings',"$destination/settings.json",'--media',$file)
 if($Reverse){$arguments+='--reverse'}
 if($FadeOnly){$arguments+='--fade-only'}
 $code=Invoke-VerificationProcess $App $arguments $destination 180
 if($code -ne 0 -or -not(Test-Path "$destination/results.json") -or (Test-Path "$destination/fatal.txt")){throw "Diagnostic failed: $destination (exit $code)"}
 Write-Output "Measured: $destination"
}
