using Dispatcher = LtcPlayer.Avalonia.UiDispatcher;
namespace LtcPlayer.App;
public record ProgramObservation(SyncState State,string Ltc,Guid? CueId,Guid? PreloadId,double Target,double? FramePts,int Presented,int Skipped,int Repeated,int Seeks,string Warning)
{
    // Actionable failures for the preview. Warning also includes timing diagnostics.
    public string Notice{get;init;}="";
    public double HostSeconds{get;init;}=MasterClock.Timestamp();
    public uint VerificationPixelBgra{get;init;}
    public ProgramMonitorStatus? Monitor{get;init;}
    public double? LagSeconds{get;init;}
    public bool Recovering{get;init;}
    public int RecoveryAttempts{get;init;}
    public ScheduledCue? ScheduledCue{get;init;}
    public double TargetHostSeconds{get;init;}
    public int InputGeneration{get;init;}
    public OutputPresentationMode OutputMode{get;init;}
    public bool Manual{get;init;}
    public bool Holding{get;init;}
}
public sealed class ManualOutputControl
{
    public sealed record Command(long Version,Guid? CueId,bool Stop,double Position=0,bool Hold=false);
    Command current=new(0,null,false);
    public Command Read()=>Volatile.Read(ref current);
    public void Play(Guid cue,double position=0,bool hold=false)=>Interlocked.Exchange(ref current,new(Read().Version+1,cue,false,position,hold));
    public void Stop()=>Interlocked.Exchange(ref current,new(Read().Version+1,null,true));
}
public sealed class ProgramPlayback
{
    static bool IsMaterialFailure(Exception e)=>e is IOException or InvalidDataException or UnauthorizedAccessException or OperationCanceledException or System.Text.Json.JsonException or System.Threading.Channels.ChannelClosedException;
    internal static Action<VideoRenderer>? ProbeObserver {get;set;}
    private readonly Dispatcher dispatcher;
    public ProgramPlayback(Dispatcher dispatcher)=>this.dispatcher=dispatcher;
    private sealed class CueFrames : IAsyncDisposable
    {
        public ScheduledCue Cue{get;}
        private readonly FrameBuffer buffer;
        private VideoFrame? current,next;
        private readonly double created=MasterClock.Timestamp();
        public double? ReadyAfter{get;private set;}
        public CueFrames(ScheduledCue cue,OutputDisplay display,double seek,CancellationToken token,MediaDecoder decoder){Cue=cue;buffer=new(cue.FilePath,display.Width,display.Height,seek,token,decoder);}
        public void Prime(){if(next==null)buffer.TryRead(out next);if(next!=null)ReadyAfter??=MasterClock.Timestamp()-created;}
        public VideoFrame? Select(double seconds,out int skipped)
        {
            int advanced=0;Prime();
            while(next!=null && next.Seconds<=seconds+0.000001){current=next;next=null;++advanced;Prime();}
            skipped=Math.Max(0,advanced-1);return current;
        }
        public ValueTask DisposeAsync()=>buffer.DisposeAsync();
    }
    public async Task RunAsync(IReadOnlyList<ScheduledCue> cues,OutputDisplay display,MasterClock clock,double offset,Action<ProgramObservation> observe,CancellationToken token,bool verify=false,nint monitorWindow=0,Action? stopRequested=null,Action<int>? monitorError=null,OutputPresentationMode outputMode=OutputPresentationMode.Standard,ManualOutputControl? manualControl=null,IProgramPresentation? presentation=null,CoverController? cover=null,Dictionary<string,MediaDecoder>? analysisCache=null,CueScheduler? timeline=null,PlaybackTimeline? sourceTimeline=null,HapIndexCache? indexCache=null)
    {
        OutputPresentation.Validate(outputMode);
        var source=sourceTimeline??new PlaybackTimeline();if(sourceTimeline==null)source.Update(cues);VideoRenderer? renderer=null;CueFrames? active=null,preload=null,recovery=null;bool monitorFaultNotified=false;
        var black=new byte[checked(display.Width*display.Height*4)];int epoch=-1,presented=0,skipped=0,repeated=0,seeks=0;
        double? lastPts=null;Guid? lastId=null,displayedId=null;double? displayedPts=null;uint verificationPixel=0;
        ProgramMonitorStatus? monitorStatus=null;double nextMonitorSample=0;
        long commandVersion=0;ScheduledCue? manualCue=null;double manualStarted=0,manualPosition=0;bool manualReady=false,manualHolding=false,wasManual=false;
        double nextRecovery=0;int recoveryAttempts=0;double? lag=null;
        var analyses=analysisCache??new Dictionary<string,MediaDecoder>(StringComparer.OrdinalIgnoreCase);
        var coverBinding=new CoverBinding();
        MediaDecoder DecoderFor(ScheduledCue cue){
            string key=Path.GetFullPath(cue.FilePath);
            if(analyses.TryGetValue(key,out var decoder))return decoder;
            // Two decoded streams at most; retain only their recent file analyses.
            if(analyses.Count>=2){
                var pinned=new[]{active?.Cue.FilePath,preload?.Cue.FilePath,recovery?.Cue.FilePath}.Where(p=>p!=null).Select(p=>Path.GetFullPath(p!)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var unused=analyses.Keys.FirstOrDefault(p=>!pinned.Contains(p));
                if(unused!=null)analyses.Remove(unused);
                else return new MediaDecoder(reuseAnalysis:true,gpuScaling:true,indexCache:indexCache);
            }
            decoder=new MediaDecoder(reuseAnalysis:true,gpuScaling:true,indexCache:indexCache);analyses.Add(key,decoder);return decoder;
        }
        try
        {
            await dispatcher.InvokeAsync(()=>{if(presentation!=null)return;renderer=new VideoRenderer(display,outputMode);if(monitorWindow!=0){renderer.EnableStopConfirmation();renderer.AttachMonitor(monitorWindow);}});
            while(!token.IsCancellationRequested)
            {
                // A held output still needs monitor presentation (e.g. after occlusion).
                // GPU completion remains polled without waiting for the monitor.
                if(renderer!=null&&monitorWindow!=0)await dispatcher.InvokeAsync(()=>{
                    if(renderer!.PollStopRequest())stopRequested?.Invoke();renderer.UpdateMonitor();
                    ProbeObserver?.Invoke(renderer);
                    double stamp=MasterClock.Timestamp();if(stamp>=nextMonitorSample){monitorStatus=renderer.MonitorStatus();nextMonitorSample=stamp+1;}
                    int error=monitorStatus?.Error??0;if(error!=0&&!monitorFaultNotified){monitorFaultNotified=true;monitorError?.Invoke(error);}
                });
                double targetHost=MasterClock.Timestamp();var s=clock.Read(targetHost);double now=s.Seconds+offset;
                lag=null;
                string warning="",notice="";byte[]? pixels=null;VideoFrame? texture=null;Guid? candidateId=displayedId;double? candidatePts=displayedPts;
                bool unavailable=s.State is SyncState.WAITING or SyncState.LOST;
                var command=manualControl?.Read();bool restart=false;
                if(command!=null&&command.Version!=commandVersion){
                    commandVersion=command.Version;
                    // Recheck on the playback thread: a click may race with LTC resumption.
                    if(unavailable){
                        if(command.Stop){manualHolding=manualCue!=null;}
                        else if(cues.FirstOrDefault(c=>c.CueId==command.CueId) is {} requested){manualCue=requested;manualPosition=Math.Clamp(command.Position,0,Math.Max(0,requested.Duration-1/requested.FrameRate));manualReady=false;manualHolding=command.Hold;restart=true;}
                    }
                }
                if(!unavailable){manualCue=null;manualHolding=false;}
                bool manual=manualCue!=null;
                if(manual&&manualReady&&!manualHolding)manualPosition=Math.Min(MasterClock.Timestamp()-manualStarted,Math.Max(0,manualCue!.Duration-1/manualCue.FrameRate));
                if(manual&&manualReady&&MasterClock.Timestamp()-manualStarted>=manualCue!.Duration)manualHolding=true;
                if(manual)now=manualCue!.Start+manualPosition;
                // A held image belongs to the opened source, not to a newly edited schedule.
                var selected=manual?manualCue:s.State is SyncState.LOST or SyncState.ERROR?active?.Cue:s.State==SyncState.WAITING?null:source.Select(now,s.Generation);
                var upcoming=manual?null:source.Next(s.State==SyncState.WAITING?double.NegativeInfinity:now);
                bool RefreshPosition()
                {
                    double host=MasterClock.Timestamp();var latest=clock.Read(host);
                    if(!s.MatchesPlaybackAuthority(latest))return false;
                    s=latest;targetHost=host;
                    if(manual){if(manualReady&&!manualHolding)manualPosition=Math.Min(host-manualStarted,Math.Max(0,manualCue!.Duration-1/manualCue.FrameRate));now=manualCue!.Start+manualPosition;}
                    else now=s.Seconds+offset;
                    selected=manual?manualCue:s.State is SyncState.LOST or SyncState.ERROR?active?.Cue:s.State==SyncState.WAITING?null:source.Select(now,s.Generation);
                    upcoming=manual?null:source.Next(s.State==SyncState.WAITING?double.NegativeInfinity:now);
                    return true;
                }
                if(manual||s.State is not (SyncState.LOST or SyncState.ERROR))
                {
                    bool jump=epoch!=s.Generation||restart||manual!=wasManual;wasManual=manual;
                    if(jump)
                    {
                        if(active!=null){await active.DisposeAsync();active=null;}
                        if(preload!=null){await preload.DisposeAsync();preload=null;}
                        if(recovery!=null){await recovery.DisposeAsync();recovery=null;}
                        nextRecovery=0;epoch=s.Generation;
                        if(!RefreshPosition())continue;
                    }
                    if(!Equals(active?.Cue,selected))
                    {
                        if(active!=null){await active.DisposeAsync();active=null;}
                        if(recovery!=null){await recovery.DisposeAsync();recovery=null;}nextRecovery=0;
                        if(!RefreshPosition())continue;
                        if(selected!=null)
                        {
                            double relative=now-selected.Start;
                            if(Equals(preload?.Cue,selected) && relative<0.5){active=preload;preload=null;}
                            // One frame of preroll provides a sample at/before the target.
                            // A fixed 200ms rewind can itself trigger the 200ms lag recovery.
                            else {active=new(selected,display,Math.Max(0,relative-1/selected.FrameRate),token,DecoderFor(selected));++seeks;}
                        }
                    }
                    if(!Equals(upcoming,preload?.Cue))
                    {
                        if(preload!=null){await preload.DisposeAsync();preload=null;}
                        if(upcoming!=null&&recovery==null)preload=new(upcoming,display,0,token,DecoderFor(upcoming));
                    }
                    if(preload!=null){
                        try{preload.Prime();}
                        catch(Exception e)when(!token.IsCancellationRequested&&IsMaterialFailure(e)){
                            var failed=preload;preload=null;source.Fail(failed.Cue,e);await failed.DisposeAsync();
                        }
                    }
                    // Disposal/preload waits may advance LTC or cross a cue boundary.
                    if(!RefreshPosition()||!Equals(active?.Cue,selected))continue;
                    if(selected==null)pixels=black;
                    else
                    {
                        double relative=now-selected.Start;
                        VideoFrame? frame=null;bool materialFailed=false;
                        try{frame=active!.Select(relative,out int dropped);skipped+=dropped;}
                        catch(Exception e)when(!token.IsCancellationRequested&&IsMaterialFailure(e)){
                            source.Fail(selected,e);materialFailed=true;pixels=black;texture=null;candidateId=null;candidatePts=null;
                            // Silence/black is published before waiting for decoder disposal.
                            await dispatcher.InvokeAsync(()=>{if(!s.MatchesPlaybackAuthority(clock.Read()))return;if(presentation!=null)presentation.Present(new VideoFrame(black,now));else renderer!.Present(black);displayedId=null;displayedPts=null;});
                            if(active!=null){await active.DisposeAsync();active=null;}
                            if(recovery!=null){await recovery.DisposeAsync();recovery=null;}
                            selected=null;
                        }
                        // Keep the current decoder while its replacement prepares. Never
                        // cancel a slow preparation repeatedly just because LTC advances.
                        if(!materialFailed&&recovery!=null){
                            try{
                                var replacement=recovery.Select(relative,out int recoveryDropped);skipped+=recoveryDropped;
                                if(replacement!=null){await active!.DisposeAsync();active=recovery;recovery=null;frame=replacement;}
                            }catch(Exception e)when(!token.IsCancellationRequested&&IsMaterialFailure(e)){
                                notice=warning="再同期準備失敗："+e.Message;await recovery!.DisposeAsync();recovery=null;nextRecovery=MasterClock.Timestamp()+2;
                            }
                        }
                        double limit=Math.Max(.2,3/(selected?.FrameRate??25));
                        if(!materialFailed&&frame!=null){
                            lag=Math.Max(0,relative-frame.Seconds);
                            if(!manual&&lag>limit){
                                warning+=(warning==""?"":" ")+$"映像が{lag:F2}秒遅延";
                                double stamp=MasterClock.Timestamp();
                                if(recovery==null&&stamp>=nextRecovery){
                                    // Include observed preparation cost in the target. A
                                    // future first frame waits until its own TC is reached.
                                    double lead=Math.Clamp((active!.ReadyAfter??0)+.15,.25,2);
                                    if(relative+lead<selected!.Duration){
                                        // Reuse the next-cue decoder's resource budget.
                                        if(preload!=null){await preload.DisposeAsync();preload=null;}
                                        recovery=new(selected,display,relative+lead,token,DecoderFor(selected));nextRecovery=stamp+1;recoveryAttempts++;seeks++;
                                    }
                                }
                            }
                        }
                        if(recovery!=null)warning+=(warning==""?"":" ")+"映像を再同期中";
                        if(materialFailed)notice=warning="素材の読込失敗（復旧・次キュー待ち）";
                        else if(frame==null)warning="映像準備中（前の映像を保持）";
                        else {if(manual&&!manualReady){manualStarted=MasterClock.Timestamp()-manualPosition;manualReady=true;}pixels=frame.Pixels;texture=frame;candidateId=selected!.CueId;candidatePts=frame.Seconds;}
                    }
                    if(pixels==black){candidateId=null;candidatePts=null;}
                }
                if(pixels!=null)
                {
                    byte[]? verifiedPixels=null;bool adopted=false;
                    await dispatcher.InvokeAsync(()=>{
                        token.ThrowIfCancellationRequested();
                        if(!s.MatchesPlaybackAuthority(clock.Read()))return;
                        if(presentation!=null)presentation.Present(texture??new VideoFrame(pixels,now));
                        else{
                            if(cover!=null)coverBinding.Apply(cover,renderer!);
                            if(texture!=null)renderer!.Present(texture);else renderer!.Present(pixels);
                        }
                        if(verify){
                            if(texture?.TextureFormat is >0)verifiedPixels=renderer!.Readback();
                            else{renderer!.Verify(pixels);verifiedPixels=pixels;}
                        }
                        adopted=true;
                    });
                    if(!adopted)continue;
                    displayedId=candidateId;displayedPts=candidatePts;
                    if(texture!=null&&lastId==displayedId&&lastPts==displayedPts)++repeated;
                    lastId=displayedId;lastPts=displayedPts;++presented;
                    if(verify)verificationPixel=BitConverter.ToUInt32(verifiedPixels!,(verifiedPixels!.Length/8)*4);
                }
                else await dispatcher.InvokeAsync(()=>{if(presentation!=null)presentation.Refresh();else if(cover!=null&&coverBinding.Apply(cover,renderer!))renderer!.Redraw();});
                if(!s.MatchesPlaybackAuthority(clock.Read()))continue;
                observe(new(s.State,s.Label,displayedId,preload?.Cue.CueId,now,displayedPts,presented,skipped,repeated,seeks,warning){Notice=notice,ScheduledCue=selected,TargetHostSeconds=targetHost,VerificationPixelBgra=verificationPixel,Monitor=monitorStatus,LagSeconds=lag,Recovering=recovery!=null,RecoveryAttempts=recoveryAttempts,InputGeneration=s.Generation,OutputMode=renderer?.OutputMode??outputMode,Manual=manual,Holding=manualHolding});
                await Task.Delay(8,token).ConfigureAwait(false);
            }
        }
        finally{if(active!=null)await active.DisposeAsync();if(preload!=null)await preload.DisposeAsync();if(recovery!=null)await recovery.DisposeAsync();if(renderer!=null)await dispatcher.InvokeAsync(renderer.Dispose);if(presentation!=null)await dispatcher.InvokeAsync(presentation.Dispose);}
    }
}

