[CmdletBinding()]
param(
 [Parameter(Mandatory=$true)]
 [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?$')]
 [string]$Version,
 [string]$Revision='HEAD',
 [string]$DotnetPath='dotnet',
 [switch]$WorkingTree,
 [ValidatePattern('^[a-zA-Z0-9-]+$')][string]$CandidateId,
 [switch]$RequireSigned
)
$ErrorActionPreference='Stop'
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$releaseRoot=[IO.Path]::GetFullPath((Join-Path $repo $(if($WorkingTree){'artifacts/candidates'}else{'releases'})))
if($CandidateId -and -not $WorkingTree){throw 'CandidateId is only valid for WorkingTree candidates.'}
$folderName=if($CandidateId){$Version+'-'+$CandidateId}else{$Version}
$target=[IO.Path]::GetFullPath((Join-Path $releaseRoot $folderName))
if((Split-Path $target -Parent) -ne $releaseRoot){throw 'Release path is outside releases/.'}
if(Test-Path -LiteralPath $target){throw "Release directory already exists: $target. Published versions must not be overwritten."}
if((Test-Path -LiteralPath $releaseRoot) -and ((Get-Item -LiteralPath $releaseRoot).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'releases/ must not be a symbolic link or junction.'}
$dotnetExe=(Get-Command $DotnetPath -CommandType Application -ErrorAction Stop).Source
Push-Location $repo
try {
 $commit=(& git rev-parse --verify "$Revision^{commit}").Trim()
 if($LASTEXITCODE -ne 0){throw 'Cannot resolve source revision.'}
 $packagingRoot=[IO.Path]::GetFullPath((Join-Path $repo 'artifacts/packaging'))
 $work=Join-Path $packagingRoot ($Version+'-'+[Guid]::NewGuid().ToString('N'))
 $source=Join-Path $work 'source'
 $stage=Join-Path $work 'release'
 $app=Join-Path $stage 'LTCue'
 New-Item -ItemType Directory -Force $source,$app | Out-Null
 $sourceZip=Join-Path $stage "LTCue-$Version-source.zip"
 if($WorkingTree){
  # Snapshot the actual work, including new source files. Never include ignored
  # outputs, local settings, .git metadata or junction/symlink targets.
  $files=@(& git -c core.quotepath=false ls-files --cached --others --exclude-standard | Sort-Object -Unique)
  if($LASTEXITCODE -ne 0){throw 'Cannot enumerate working-tree source.'}
  $sourceFiles=@(foreach($relative in $files){
   $original=[IO.Path]::GetFullPath((Join-Path $repo $relative))
   if(-not $original.StartsWith($repo+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Snapshot path is outside repository.'}
   if(-not(Test-Path -LiteralPath $original -PathType Leaf)){continue}
   $part=Get-Item -LiteralPath $original
   if($part.Attributes -band [IO.FileAttributes]::ReparsePoint){throw "Snapshot cannot include a file link: $relative"}
   $destination=Join-Path $source $relative
   New-Item -ItemType Directory -Force (Split-Path $destination -Parent) | Out-Null
   Copy-Item -LiteralPath $original -Destination $destination
   [ordered]@{Path=$relative;SHA256=(Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant()}
  })
  [IO.File]::WriteAllText((Join-Path $source 'SOURCE_FILES.json'),($sourceFiles | ConvertTo-Json -Depth 3))
  Add-Type -AssemblyName System.IO.Compression.FileSystem
  [IO.Compression.ZipFile]::CreateFromDirectory($source,$sourceZip,[IO.Compression.CompressionLevel]::Optimal,$false)
 }else{
  & git archive --format=zip "--output=$sourceZip" $commit
  if($LASTEXITCODE -ne 0){throw 'Source archive failed.'}
  Expand-Archive -LiteralPath $sourceZip -DestinationPath $source
 }
 $sourceHash=(Get-FileHash -LiteralPath $sourceZip -Algorithm SHA256).Hash.ToLowerInvariant()
 [xml]$project=Get-Content -LiteralPath (Join-Path $source 'src/LtcPlayer.Avalonia/LtcPlayer.Avalonia.csproj') -Raw
 $sourceVersion=@($project.Project.PropertyGroup.Version | Where-Object {$_})[0]
 $sdkVersion=(Get-Content -LiteralPath (Join-Path $source 'global.json') -Raw | ConvertFrom-Json).sdk.version
 if($sourceVersion -ne $Version){throw "Source version $sourceVersion does not match requested release $Version. Commit the version change first."}
 $notes=Join-Path $source 'RELEASE_NOTES.md'
 if(-not ([IO.File]::ReadAllText($notes).StartsWith("# LTCue $Version"))){throw 'RELEASE_NOTES.md version does not match the release.'}
 if($WorkingTree){Write-Output "Building an UNCOMMITTED working-tree candidate based on $commit ($Version). Source ZIP SHA256: $sourceHash"}
 else{Write-Output "Building committed source $commit ($Version). Uncommitted working-tree files are not included."}
 & (Join-Path $source 'scripts/build.ps1') -DotnetPath $dotnetExe
 if($LASTEXITCODE -ne 0){throw 'Product build failed.'}
 Copy-Item -Path (Join-Path $source 'artifacts/win-x64/*') -Destination $app -Recurse
 foreach($name in @('LICENSE','NOTICE.md','RELEASE_NOTES.md')){Copy-Item -LiteralPath (Join-Path $source $name) -Destination $app}
 foreach($name in @('LICENSES')){$destination=Join-Path $app $name;New-Item -ItemType Directory -Force $destination | Out-Null;Get-ChildItem -LiteralPath (Join-Path $source $name) -Force | ForEach-Object {Copy-Item -LiteralPath $_.FullName -Destination $destination -Recurse -Force}}
 # User documentation is an explicit allowlist. Development history remains in source.zip.
 Copy-Item -LiteralPath (Join-Path $source 'docs/DISTRIBUTION_README.md') -Destination (Join-Path $app 'README.md')
 $distributionDocs=@('QUICKSTART.md','USER_GUIDE.md','COMPATIBILITY.md','TROUBLESHOOTING.md','images/quickstart-display.png','images/quickstart-input.png','images/quickstart-output-start.png','images/quickstart-queue.png','images/reference-cover.png','asset-licenses.json','package-licenses.json','dependencies.json',('releases/'+$Version+'.md'))
 foreach($relative in $distributionDocs){
  $destination=Join-Path $app ('docs/'+$relative)
  New-Item -ItemType Directory -Force (Split-Path $destination -Parent) | Out-Null
  Copy-Item -LiteralPath (Join-Path $source ('docs/'+$relative)) -Destination $destination
 }
 $actualDocs=@(Get-ChildItem -LiteralPath (Join-Path $app 'docs') -File -Recurse | ForEach-Object {[IO.Path]::GetRelativePath((Join-Path $app 'docs'),$_.FullName).Replace('\','/')})
 if(@(Compare-Object ($distributionDocs | Sort-Object) ($actualDocs | Sort-Object)).Count){throw 'Unexpected files in distribution documentation.'}
 $corresponding=Join-Path $app 'CorrespondingSource'
 New-Item -ItemType Directory -Force (Join-Path $corresponding 'native'),(Join-Path $corresponding 'third_party') | Out-Null
 Copy-Item -LiteralPath (Join-Path $source 'native/Audio.cpp'),(Join-Path $source 'native/build-phase2.cmd') -Destination (Join-Path $corresponding 'native')
 Copy-Item -LiteralPath (Join-Path $source 'third_party/libltc') -Destination (Join-Path $corresponding 'third_party') -Recurse
 $sourceIdentity=if($WorkingTree){"working-tree snapshot based on $commit; source ZIP SHA256: $sourceHash"}else{$commit}
 $sourceNotice="# AudioNative.dll corresponding source`n`nSource revision: $sourceIdentity`n`nSee ../NOTICE.md and ../LICENSES/LGPL-3.0.txt. Rebuild AudioNative.dll using native/build-phase2.cmd with MSVC x64 and the Windows SDK. The library may be replaced by an ABI-compatible modified build.`n"
 [IO.File]::WriteAllText((Join-Path $corresponding 'README.md'),$sourceNotice)
 foreach($required in @('LTCue.exe','LTCue.dll','AudioNative.dll','PlayerNative.dll','TestMedia/HAP_1_1080p_2997_10s.mov','TestMedia/LTC_29.97DF_from-zero.wav','LICENSES/LGPL-3.0.txt','NOTICE.md','coreclr.dll','hostfxr.dll','CorrespondingSource/third_party/libltc/COPYING')){
  if(-not(Test-Path -LiteralPath (Join-Path $app $required) -PathType Leaf)){throw "Missing package file: $required"}
 }
 if(Test-Path -LiteralPath (Join-Path $source 'src/LtcPlayer.Avalonia/LucideIcon.cs')){foreach($required in @('LICENSES/Lucide.txt','docs/asset-licenses.json')){if(-not(Test-Path -LiteralPath (Join-Path $app $required))){throw "Missing asset license: $required"}}}
 $runtime=Get-Content -LiteralPath (Join-Path $app 'LTCue.runtimeconfig.json') -Raw | ConvertFrom-Json
 if(-not $runtime.runtimeOptions.includedFrameworks){throw 'Package must be self-contained.'}
 if(Test-Path -LiteralPath (Join-Path $app 'phase2-settings.json')){throw 'Package must not contain local user settings.'}
 . (Join-Path $PSScriptRoot 'verification-common.ps1')
 $verificationBuild=$true
 try{Read-VerificationCatalog (Join-Path $app 'LTCue.dll') | Out-Null}catch{if($_.Exception.Message -like 'This assembly is not a catalog-bearing*'){$verificationBuild=$false}else{throw}}
 if($verificationBuild){throw 'Verification code must not be distributed as a product.'}
 $signatures=Join-Path $stage 'SIGNATURES.json'
 & (Join-Path $source 'scripts/test-release-signatures.ps1') -Path $app -ReportPath $signatures -ReportOnly:(!$RequireSigned)
 $signatureData=Get-Content -LiteralPath $signatures -Raw | ConvertFrom-Json
 $invalid=@($signatureData | Where-Object {$_.Status -ne 'Valid'}).Count
 $manifest=[ordered]@{Version=$Version;SourceMode=$(if($WorkingTree){'WorkingTreeSnapshot'}else{'CommittedRevision'});Commit=$(if($WorkingTree){$null}else{$commit});BaseCommit=$commit;SourceZipSHA256=$sourceHash;SDK=$sdkVersion;Runtime='win-x64';CreatedUtc=[DateTime]::UtcNow.ToString('o');Signed=($invalid -eq 0);FilesWithoutValidSignature=$invalid;Verification='See docs/releases/'+$Version+'.md';ExternalPrerequisites=@('FFmpeg and ffprobe','Visual C++ v14 x64 runtime')}
 $manifestText=$manifest | ConvertTo-Json -Depth 4
 [IO.File]::WriteAllText((Join-Path $stage 'MANIFEST.json'),$manifestText)
 [IO.File]::WriteAllText((Join-Path $app 'MANIFEST.json'),$manifestText)
 Copy-Item -LiteralPath $notes -Destination (Join-Path $stage 'RELEASE_NOTES.md')
 $binaryZip=Join-Path $stage "LTCue-$Version-win-x64.zip"
 Add-Type -AssemblyName System.IO.Compression.FileSystem
 [IO.Compression.ZipFile]::CreateFromDirectory($app,$binaryZip,[IO.Compression.CompressionLevel]::Optimal,$false)
 $hashes=@(Get-ChildItem -LiteralPath $stage -File | Sort-Object Name | ForEach-Object { ((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant())+'  '+$_.Name })
 [IO.File]::WriteAllText((Join-Path $stage 'SHA256SUMS.txt'),(($hashes -join "`n")+"`n"))
 # Directory.Move fails if another process created this version meanwhile.
 # Both absolute paths are constrained to this repository before the move.
 if(-not ([IO.Path]::GetFullPath($stage).StartsWith($packagingRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase))){throw 'Staging path is outside artifacts/packaging/.'}
 if((Split-Path $target -Parent) -ne $releaseRoot){throw 'Unexpected final release path.'}
 New-Item -ItemType Directory -Force $releaseRoot | Out-Null
 [IO.Directory]::Move($stage,$target)
 Write-Output "Release prepared: $target"
} finally {Pop-Location}
