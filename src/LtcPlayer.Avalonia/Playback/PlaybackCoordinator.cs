using LtcPlayer.Avalonia;
namespace LtcPlayer.App;

// Owns source arbitration, the LTC run and the audio follower. No controls/settings/file IO.
// Lifecycle commands run on the UI dispatcher; observations and audio reads may cross threads.
public sealed class PlaybackCoordinator : IAsyncDisposable
{
 sealed class SyncRun(int revision,double offset){public readonly PlaybackTimeline Timeline=new();public readonly double Offset=offset;public readonly Guid Id=Guid.NewGuid();public readonly int Revision=revision;public readonly CancellationTokenSource Cancel=new();public volatile Task? Task;public ProgramObservation? Observation;}
 readonly PreviewPlayer manual;
 readonly PreviewHost surface;
 readonly Func<MasterClock> clock;
 readonly CoverController cover;
 readonly SemaphoreSlim commands=new(1);
 readonly SemaphoreSlim audioCommands=new(1);
 readonly object state=new();
 readonly Dictionary<string,MediaDecoder> analyses=new(StringComparer.OrdinalIgnoreCase);
 SyncRun? run;
 AudioPlayhead playhead=AudioPlayhead.Empty;
 AudioOutputService? audio;
 double configuredOffset;
 volatile bool disposed;
 public event Action<Exception>? Failed;
 public event Action<TimelineFailure>? MaterialFailed;
 public event Action<AudioPlayhead,string>? AudioMaterialFailed;
 public double RetryAt{get;private set;}
 public bool OwnsSurface{get{lock(state)return run!=null;}}
 public Guid? SynchronizationSession{get{lock(state)return run?.Id;}}
 public ProgramObservation? Observation{get{lock(state)return run?.Observation;}}
 public bool ManualAvailable=>clock().Read().State is SyncState.WAITING or SyncState.LOST;
 public void DeferRetry()=>RetryAt=MasterClock.Timestamp()+2;
 public bool LtcHasPriority=>HasPriority(clock().Read().State);
 public bool UsesSharedAudio=>audio!=null;
 public SharedAudioSnapshot Audio=>audio?.Snapshot??SharedAudioSnapshot.Off;
 public PlaybackCoordinator(PreviewPlayer manual,PreviewHost surface,Func<MasterClock> clock,CoverController cover)
 {this.manual=manual;this.surface=surface;this.clock=clock;this.cover=cover;manual.ManualAuthority=()=>!disposed&&ManualAvailable;manual.Changed+=PublishManual;}
 static bool HasPriority(SyncState value)=>value is SyncState.LOCKED or SyncState.FREEWHEEL or SyncState.RESYNC;
 void PublishManual(PreviewSnapshot p)
 {
  lock(state){if(disposed||run!=null||LtcHasPriority)return;
   bool advancing=p.State==PreviewState.Playing&&!manual.IsBusy;
   // Read the manual monotonic clock after presentation, rather than anchoring an
   // older frame position to the end of a potentially slow UI/native operation.
   playhead=new(manual.PlaybackSessionId??manual.MediaSessionId,manual.MediaSessionId,manual.SourceGeneration,manual.LoadedPath,advancing?manual.PositionForAudio:p.Position,p.Duration,advancing,MasterClock.Timestamp(),SourceOwnsClock:true){PreparationRequested=manual.PreparingPlayback,PreparationCancellation=manual.PreparationCancellation};}
 }
 // A run owns an immutable offset. Block its stale output immediately after a
 // new setting is committed, while Reconcile prepares the replacement run.
 public void SetSynchronizationOffset(double offset){if(!double.IsFinite(offset))throw new ArgumentOutOfRangeException(nameof(offset));lock(state)configuredOffset=offset;}
 bool MayPresent(SyncRun owner){lock(state)return !disposed&&ReferenceEquals(run,owner)&&!owner.Cancel.IsCancellationRequested&&(!LtcHasPriority||owner.Offset==configuredOffset);}
 void Observe(SyncRun owner,ProgramObservation p)
 {
  lock(state){
   var current=clock().Read();
   if(disposed||!ReferenceEquals(run,owner)||owner.Cancel.IsCancellationRequested||
      (HasPriority(current.State)&&owner.Offset!=configuredOffset)||
      !current.MatchesPlaybackAuthority(p.State,p.InputGeneration))return;
   owner.Observation=p;
   playhead=p.ScheduledCue is {} cue?new(owner.Id,cue.CueId,p.InputGeneration,cue.FilePath,p.Target-cue.Start,cue.Duration,HasPriority(p.State),p.TargetHostSeconds,true){SourceRevision=cue.SourceRevision}:AudioPlayhead.Empty;
  }
 }
 public AudioPlayhead ReadAudioPlayhead()
 {
  if(disposed)return AudioPlayhead.Empty;
  AudioPlayhead p;SyncRun? active;double offset;lock(state){p=playhead;active=run;offset=configuredOffset;}
  double now=MasterClock.Timestamp();var current=clock().Read(now);bool priority=HasPriority(current.State);
  if(current.State==SyncState.ERROR)return p with{Advancing=false};
  if(priority){
   // Audio and video share the immutable program and LTC authority. A blocked
   // presentation must not invalidate audio; current clock/cue/generation do.
   if(active==null||active.Offset!=offset||active.Cancel.IsCancellationRequested||active.Task==null||active.Task.IsCompleted)return AudioPlayhead.Empty;
   double target=current.Seconds+active.Offset;var cue=active.Timeline.Select(target,current.Generation);
   return cue==null?AudioPlayhead.Empty:new(active.Id,cue.CueId,current.Generation,cue.FilePath,target-cue.Start,cue.Duration,true,now,true){SourceRevision=cue.SourceRevision};
  }
  if(p.LtcDriven)return p with{Advancing=false};
  return p;
 }
 public async Task ConfigureAudioAsync(bool enabled,string device,float volume,bool muted,bool silenceForVerification=false)
 {
  await audioCommands.WaitAsync();
  try{
   ObjectDisposedException.ThrowIf(disposed,this);
   if(audio==null){await manual.UseSharedAudioAsync();ObjectDisposedException.ThrowIf(disposed,this);audio=new(ReadAudioPlayhead,silenceForVerification,analysis:manual.Analysis);audio.SourceFailed+=(source,error)=>AudioMaterialFailed?.Invoke(source,error);PublishManual(manual.Snapshot);}
   await audio.ConfigureAsync(enabled,device,volume,muted);
  }finally{audioCommands.Release();}
 }
 public void SetVolume(float value,bool muted)=>audio?.SetVolume(value,muted);
 public async Task ReconcileAsync(int revision,bool valid,Func<List<ScheduledCue>> cues,OutputDisplay display,double offset)
 {
  if(disposed||MasterClock.Timestamp()<RetryAt||!await commands.WaitAsync(0))return;
  try{
   if(disposed)return;SetSynchronizationOffset(offset);
   if(run is {} changed&&changed.Offset!=offset&&LtcHasPriority)await StopCoreAsync();
   if(run is {} old&&(!valid||old.Task?.IsCompleted==true))await StopCoreAsync();
   if(run is {} existing){existing.Timeline.Update(cues());return;}
   if(run!=null||!LtcHasPriority||!valid)return;
   var program=cues();
   var next=new SyncRun(revision,offset);next.Timeline.Update(program);next.Timeline.Failed+=failure=>MaterialFailed?.Invoke(failure with{SessionId=next.Id});lock(state){run=next;playhead=AudioPlayhead.Empty;}
   try{await manual.SetSyncOwnershipAsync(true);next.Task=RunAsync(next,program,display,offset);}
   catch{await StopCoreAsync();throw;}
  }finally{commands.Release();}
 }
 async Task RunAsync(SyncRun owner,List<ScheduledCue> cues,OutputDisplay display,double offset)
 {
  var token=owner.Cancel.Token;
  try{await Task.Run(()=>new ProgramPlayback(new UiDispatcher()).RunAsync(cues,display,clock(),offset,p=>Observe(owner,p),token,presentation:new PreviewProgramPresentation(surface,()=>MayPresent(owner)),cover:cover,analysisCache:analyses,sourceTimeline:owner.Timeline,indexCache:manual.IndexCache,mediaAnalysis:manual.Analysis));}
  catch(OperationCanceledException)when(token.IsCancellationRequested){}
  catch(Exception e){DeferRetry();lock(state){if(ReferenceEquals(run,owner))playhead=AudioPlayhead.Empty;}Failed?.Invoke(e);}
 }
 async Task StopCoreAsync()
 {
  SyncRun? old;lock(state){old=run;run=null;playhead=AudioPlayhead.Empty;}
  if(old!=null){old.Cancel.Cancel();try{if(old.Task!=null)await old.Task;}finally{old.Cancel.Dispose();}}
  await manual.SetSyncOwnershipAsync(false);
 }
#if VERIFICATION
 internal void FailMaterialForTest(ScheduledCue cue,Exception error){PlaybackTimeline? t;lock(state)t=run?.Timeline;t?.Fail(cue,error);}
#endif
 // Serialize releasing a held image with starting a new synchronization run.
 // A clock change during the awaits must not clear the next LTC-owned frame.
 public async Task<bool> ClearHeldSourceAsync(Guid? expectedCueId=null)
 {
  await commands.WaitAsync();
  try{
   if(disposed||!ManualAvailable)return false;
   if(run==null)return false;
   if(expectedCueId.HasValue&&run.Observation?.CueId!=expectedCueId)return false;
   await StopCoreAsync();
#if VERIFICATION
   if(BeforeHeldClearForTest!=null)await BeforeHeldClearForTest();
#endif
   if(!ManualAvailable)return false;
   try{await manual.ClearAsync();}catch(OperationCanceledException){return false;}
   return ManualAvailable;
  }finally{commands.Release();}
 }
#if VERIFICATION
 internal Func<Task>? BeforeHeldClearForTest{get;set;}
#endif
 public async Task StopForInputChangeAsync()
 {
  await commands.WaitAsync();try{bool owned=run!=null;await StopCoreAsync();if(owned)await manual.ClearReleasedSourceAsync();}finally{commands.Release();}
 }
 public async Task StopSynchronizationAsync(){await commands.WaitAsync();try{await StopCoreAsync();}finally{commands.Release();}}
 public async Task DisposeAudioAsync(){await audioCommands.WaitAsync();try{lock(state)playhead=AudioPlayhead.Empty;if(audio is {} old){audio=null;await old.DisposeAsync();}}finally{audioCommands.Release();}}
 public async ValueTask DisposeAsync(){if(disposed)return;disposed=true;await StopSynchronizationAsync();manual.Changed-=PublishManual;await DisposeAudioAsync();}
}
