# Shared validation functions. These never launch the supplied application.
function Read-VerificationCatalog([string]$AssemblyPath){
 $stream=[IO.File]::OpenRead($AssemblyPath)
 try{
  $pe=[Reflection.PortableExecutable.PEReader]::new($stream)
  try{
   $reader=[Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
   foreach($handle in $reader.ManifestResources){
    $resource=$reader.GetManifestResource($handle)
    if($reader.GetString($resource.Name) -ne 'LtcPlayer.VerificationCatalog'){continue}
    $bytes=[byte[]]$pe.GetSectionData($pe.PEHeaders.CorHeader.ResourcesDirectory.RelativeVirtualAddress).GetContent()
    $offset=[int]$resource.Offset;$length=[BitConverter]::ToInt32($bytes,$offset)
    $content=[byte[]]::new($length);[Array]::Copy($bytes,$offset+4,$content,0,$length)
    return [Text.Encoding]::UTF8.GetString($content)
   }
   throw 'This assembly is not a catalog-bearing verification build. It was not started.'
  }finally{$pe.Dispose()}
 }finally{$stream.Dispose()}
}
function Assert-VerificationEvidence($Suite,[string]$Folder){
 if(Test-Path -LiteralPath (Join-Path $Folder 'fatal.txt')){throw 'Suite reported a fatal error.'}
 $file=Join-Path $Folder $Suite.Evidence
 if(-not(Test-Path -LiteralPath $file -PathType Leaf)){throw "Missing evidence: $($Suite.Evidence)"}
 $data=Get-Content -LiteralPath $file -Raw | ConvertFrom-Json -AsHashtable
 $count=0
 switch($Suite.Kind){
  'Status' {
   $items=@($data);$count=$items.Count
   if($count -eq 0 -or @($items | Where-Object { $_.status -cne 'PASS' -or [string]::IsNullOrWhiteSpace($_.test) }).Count){throw 'Not all assertions passed, or test names are missing.'}
  }
  'Resize' {
   $items=@($data.Samples)+@($data.WarningSamples);$count=$items.Count
   if($data.Passed -ne $true -or @($data.Samples).Count -ne 24 -or @($data.WarningSamples).Count -ne 16 -or @($data.WarningSamples | Where-Object Passed -ne $true).Count){throw 'Resize/warning assertions failed or are incomplete.'}
  }
  'Passed' {$count=1;if($data.Passed -ne $true -or @($data.Stages).Count -lt 5){throw 'Recovery scenario failed or produced too few stages.'}}
  'Performance' {$count=@($data.Measurements).Count;if($data.Passed -ne $true){throw 'Performance scenario failed.'}}
  'Monitor' {$count=@($data.Colors).Count;if(@($data.Colors | Where-Object Error -ne 0).Count -or @($data.Timings).Count -lt 2){throw 'Monitor measurements incomplete or contain GPU errors.'}}
  'Display' {$count=@($data.Cases).Count;if($data.InputLabelRemoved -ne $true -or @($data.Cases | Where-Object Pass -ne $true).Count){throw 'Display format assertions failed.'}}
  'AudioStart' {$count=@($data).Count;if(@($data | Where-Object { $_.State -ne 'Playing' -or $null -eq $_.ErrorSeconds -or [Math]::Abs($_.ErrorSeconds) -gt .1 }).Count){throw 'Audio startup assertions failed.'}}
  default {throw "Unknown evidence format: $($Suite.Kind)"}
 }
 if($count -lt 1 -or ($Suite.Count -gt 0 -and $count -ne $Suite.Count)){throw "Unexpected assertion count: $count (expected $($Suite.Count))"}
 return $count
}
function Invoke-VerificationProcess([string]$File,[string[]]$Arguments,[string]$Folder,[int]$TimeoutSeconds){
 $start=[Diagnostics.ProcessStartInfo]::new();$start.FileName=$File;$start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
 foreach($argument in $Arguments){$start.ArgumentList.Add($argument)}
 $process=[Diagnostics.Process]::new();$process.StartInfo=$start
 try{
  if(-not $process.Start()){throw 'Process did not start.'}
  $out=$process.StandardOutput.ReadToEndAsync();$err=$process.StandardError.ReadToEndAsync()
  $timedOut=-not $process.WaitForExit($TimeoutSeconds*1000)
  if($timedOut){$process.Kill($true);$null=$process.WaitForExit(5000)}
  foreach($pair in @(@($out,'stdout.log'),@($err,'stderr.log'))){if($pair[0].Wait(5000)){[IO.File]::WriteAllText((Join-Path $Folder $pair[1]),$pair[0].GetAwaiter().GetResult())}}
  if($timedOut){throw "Timeout after $TimeoutSeconds seconds; only this launched process tree was terminated."}
  return $process.ExitCode
 }finally{$process.Dispose()}
}
