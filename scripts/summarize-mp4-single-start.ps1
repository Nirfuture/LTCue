param([Parameter(Mandatory)][string]$Evidence)
$ErrorActionPreference='Stop'
$metrics=foreach($stage in 'before','after'){
 foreach($case in Get-ChildItem "$Evidence/$stage" -Directory){
  $trace=Get-Content "$($case.FullName)/transitions.json" -Raw | ConvertFrom-Json
  $conditions=Get-Content "$($case.FullName)/conditions.json" -Raw | ConvertFrom-Json
  foreach($group in $trace | Group-Object Trial){
   $events=$group.Group;$request=$events | Where-Object Event -eq play_request | Select-Object -First 1
   if(!$request){continue}
   function TimeOf($name){$entry=$events | Where-Object Event -eq $name | Select-Object -First 1;if($entry){return ($entry.Seconds-$request.Seconds)*1000};return $null}
   $first=TimeOf video_first_present;$clock=TimeOf video_clock_start;$continuous=TimeOf video_playback_first_present
   $total=@($events | Where-Object Event -eq decode_ffmpeg_started).Count
   $audio=@($events | Where-Object Event -eq audio_ffmpeg_process_created).Count
   [pscustomobject]@{Stage=$stage;Case=$case.Name;Trial=$group.Name;RegistrationMs=$conditions.RegistrationMs;FirstMs=$first;ClockMs=$clock;ContinuousMs=$continuous;FirstToClockMs=$clock-$first;FirstToContinuousMs=$continuous-$first;VideoProcesses=$total-$audio;AudioProcesses=$audio;InfoProbes=@($events | Where-Object Event -eq info_ffprobe_started).Count;IndexProbes=@($events | Where-Object Event -eq index_ffprobe_started).Count;AudioStartMs=(TimeOf audio_start_complete)}
  }
 }
}
$metrics | ConvertTo-Json -Depth 5 | Set-Content "$Evidence/metrics.json"
function Median($values){$sorted=@($values | Sort-Object);if($sorted.Count%2){$sorted[[int]($sorted.Count/2)]}else{($sorted[$sorted.Count/2-1]+$sorted[$sorted.Count/2])/2}}
$summary=foreach($g in $metrics | Group-Object Stage,Case){$items=@($g.Group);$first=$items[0];$repeat=@($items | Where-Object Trial -match 'visit-0-trial-[2-5]$');[pscustomobject]@{Stage=$first.Stage;Case=$first.Case;FirstMs=$first.FirstMs;ClockMs=$first.ClockMs;ContinuousMs=$first.ContinuousMs;FirstToClockMs=$first.FirstToClockMs;VideoProcesses=$first.VideoProcesses;AudioProcesses=$first.AudioProcesses;RepeatClockMedianMs=Median $repeat.ClockMs;RepeatContinuousMedianMs=Median $repeat.ContinuousMs;Trials=$items.Count}}
$summary | ConvertTo-Json | Set-Content "$Evidence/comparison.json"
$checks=foreach($case in Get-ChildItem "$Evidence/before" -Directory){$a=Get-Content "$Evidence/before/$($case.Name)/conditions.json" -Raw | ConvertFrom-Json;$b=Get-Content "$Evidence/after/$($case.Name)/conditions.json" -Raw | ConvertFrom-Json;foreach($key in 'Material','CanvasWidth','CanvasHeight','AudioEnabled','AudioDevice','HardwareGain','ExternalOutput','LtcInput','Attempts','Switching'){if($a.$key -cne $b.$key){throw "Condition differs: $($case.Name) $key"}};[pscustomobject]@{Case=$case.Name;ConditionsMatch=$true}}
$hashA=Get-Content "$Evidence/before/hashes.json" -Raw | ConvertFrom-Json;$hashB=Get-Content "$Evidence/after/hashes.json" -Raw | ConvertFrom-Json
if(Compare-Object $hashA $hashB -Property Path,Hash){throw 'Material/tool hashes differ'}
$checks | ConvertTo-Json | Set-Content "$Evidence/conditions-check.json"
$summary | Format-Table -AutoSize
