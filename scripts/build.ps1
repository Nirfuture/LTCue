param([switch]$Verification,[string]$DotnetPath='dotnet',[string]$OutputPath)
$ErrorActionPreference='Stop'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE='false'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$repo=Split-Path $PSScriptRoot -Parent
Push-Location $repo
try {
 & ./native/build-phase2.cmd
 if($LASTEXITCODE -ne 0){throw 'Audio native build failed'}
 & ./native/phase5/build.cmd
 if($LASTEXITCODE -ne 0){throw 'Player native build failed'}
 & $DotnetPath restore src/LtcPlayer.Avalonia -r win-x64 --locked-mode --configfile NuGet.Config
 if($LASTEXITCODE -ne 0){throw 'Restore failed'}
 $mode=if($Verification){'true'}else{'false'}
 $name=if($Verification){'verification'}else{'win-x64'}
 if(-not $OutputPath){$OutputPath="artifacts/$name"}
 & $DotnetPath publish src/LtcPlayer.Avalonia --no-restore -c Release -r win-x64 --self-contained true '-p:UsedAvaloniaProducts=' "-p:EnableVerification=$mode" -o $OutputPath
 if($LASTEXITCODE -ne 0){throw 'Publish failed'}
} finally {Pop-Location}
