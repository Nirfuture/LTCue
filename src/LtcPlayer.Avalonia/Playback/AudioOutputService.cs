namespace LtcPlayer.App;

public enum SharedAudioState { Off,Waiting,Preparing,Playing,Muted,Resyncing,NoAudio,Error }
public sealed record SharedAudioSnapshot(SharedAudioState State,string Notice,string DeviceId,Guid SinkSession,Guid CueId,int Generation,double TargetPts,double? PlayedPts,double? ErrorSeconds,int PreparationStarts,int Reseeks,int DroppedBlocks,int BufferedBlocks,uint HardwarePadding,float RateRatio,float AppliedGain)
{
    public double HostSeconds{get;init;}=MasterClock.Timestamp();
    public static SharedAudioSnapshot Off{get;}=new(SharedAudioState.Off,"","",Guid.Empty,Guid.Empty,0,0,null,null,0,0,0,0,0,1,0);
}

// This dedicated, non-real-time worker is the only owner of WASAPI. Video only publishes state.
public sealed class AudioOutputService : IAsyncDisposable
{
    sealed record Configuration(long Version,bool Enabled,string DeviceId,float Volume,bool Muted,TaskCompletionSource Applied);
    readonly Func<AudioPlayhead> readPlayhead;
    readonly CancellationTokenSource stop=new();
    readonly Task worker;
    readonly bool hardwareSilence;
    readonly int preparationDelayMs;
    Configuration configuration=new(0,false,"",.5f,false,new(TaskCreationOptions.RunContinuationsAsynchronously));
    SharedAudioSnapshot snapshot=SharedAudioSnapshot.Off;
    long version;
    int disposed;
    public SharedAudioSnapshot Snapshot=>Volatile.Read(ref snapshot);
    public AudioOutputService(Func<AudioPlayhead> readPlayhead,bool silenceForVerification=false,int preparationDelayMs=0)
    {
        this.readPlayhead=readPlayhead;hardwareSilence=silenceForVerification;this.preparationDelayMs=preparationDelayMs;
        worker=Task.Factory.StartNew(Run,CancellationToken.None,TaskCreationOptions.LongRunning,TaskScheduler.Default);
    }
    public Task ConfigureAsync(bool enabled,string deviceId,float volume,bool muted)
    {
        ObjectDisposedException.ThrowIf(disposed!=0,this);
        if(!float.IsFinite(volume)||volume<0||volume>1)throw new ArgumentOutOfRangeException(nameof(volume));
        var next=new Configuration(Interlocked.Increment(ref version),enabled,deviceId,volume,muted,new(TaskCreationOptions.RunContinuationsAsynchronously));
        var old=Interlocked.Exchange(ref configuration,next);old.Applied.TrySetCanceled();return next.Applied.Task;
    }
    public void SetVolume(float volume,bool muted)
    {
        if(!float.IsFinite(volume)||volume<0||volume>1)throw new ArgumentOutOfRangeException(nameof(volume));
        Configuration before,after;
        do{before=Volatile.Read(ref configuration);after=before with{Volume=volume,Muted=muted};}while(!ReferenceEquals(Interlocked.CompareExchange(ref configuration,after,before),before));
    }
    void Run()
    {
        AudioOutputSink? sink=null;AudioDecodeSession? decode=null;
        CancellationTokenSource? inspectionCancel=null;Task<AudioMediaInfo>? inspection=null;AudioMediaInfo? info=null;
        var cache=new Dictionary<(string Path,long Length,DateTime Modified),AudioMediaInfo>();
        (Guid Session,Guid Cue,int Generation,string Path)? identity=null;
        AudioPcmBlock? pending=null;int offset=0;bool epochStarted=false,running=false,suspended=true;
        double origin=0,prepareAt=0,lead=.15,nextRate=0;float lastRate=1;
        long applied=-1,faulted=-1;Guid sinkSession=Guid.Empty;
        int preparations=0,reseeks=0,dropped=0;
        AudioSynchronizationPolicy policy=new();
        void DisposeDecode()
        {
            if(decode!=null){decode.DisposeAsync().AsTask().GetAwaiter().GetResult();decode=null;}
            pending=null;offset=0;epochStarted=false;running=false;
        }
        void CancelInspection()
        {
            inspectionCancel?.Cancel();
            if(inspection!=null)try{inspection.GetAwaiter().GetResult();}catch{}
            inspection=null;inspectionCancel?.Dispose();inspectionCancel=null;
        }
        void ResetSource()
        {
            if(sink!=null&&!suspended){sink.Suspend();suspended=true;}
            DisposeDecode();CancelInspection();info=null;identity=null;
        }
        void Close()
        {
            try{ResetSource();}finally{sink?.Dispose();sink=null;sinkSession=Guid.Empty;}
        }
        void Publish(SharedAudioState state,string notice,AudioPlayhead? p=null,AudioDeviceSnapshot? stats=null,double? error=null)
        {
            var c=Volatile.Read(ref configuration);var s=stats;
            Volatile.Write(ref snapshot,new(state,notice,sink?.DeviceId??"",sinkSession,p?.CueId??Guid.Empty,p?.Generation??0,p?.Estimate(MasterClock.Timestamp())??0,s is {} a&&epochStarted?origin+a.ConsumedSeconds:null,error,preparations,reseeks,dropped,decode?.BufferedBlocks??0,s?.Padding??0,s?.RateRatio??1,c.Muted?0:c.Volume));
        }
        void BeginDecode(AudioPlayhead p,double target,bool recovering=false)
        {
            DisposeDecode();sink!.Suspend();suspended=true;lastRate=1;
            // Startup follows current time immediately. Future targeting is only for
            // recovery from a large drift; it must not delay every manual start.
            double at=Math.Min(target+(recovering?lead:0),p.Duration-1.0/48000);
            if(at<0||info==null||at>=info.Duration)return;
            prepareAt=MasterClock.Timestamp();decode=new(info,p.CueId,p.Generation,at,stop.Token,preparationDelayMs);preparations++;
        }
        try
        {
            while(!stop.IsCancellationRequested)
            {
                var config=Volatile.Read(ref configuration);
                try
                {
                    if(config.Version!=applied)
                    {
                        Close();applied=config.Version;faulted=-1;policy=new();
                        if(config.Enabled){StartupMeasurementTrace.Mark("audio_device_init_begin");sink=new(config.DeviceId);sinkSession=Guid.NewGuid();sink.SetStreamGain(hardwareSilence?0:config.Muted?0:config.Volume);StartupMeasurementTrace.Mark("audio_device_init_complete");}
                        Publish(config.Enabled?SharedAudioState.Waiting:SharedAudioState.Off,"");config.Applied.TrySetResult();
                    }
                    if(!config.Enabled||faulted==config.Version){stop.Token.WaitHandle.WaitOne(3);continue;}
                    var source=readPlayhead();double now=MasterClock.Timestamp(),target=source.Estimate(now);
                    sink!.SetStreamGain(hardwareSilence?0:config.Muted?0:config.Volume);
                    if(!source.CanPrepare(now)||target>=source.Duration)
                    {
                        ResetSource();Publish(SharedAudioState.Waiting,source.Notice,source);stop.Token.WaitHandle.WaitOne(3);continue;
                    }
                    var key=(source.SessionId,source.CueId,source.Generation,source.Path);
                    if(identity!=key)
                    {
                        ResetSource();identity=key;policy=new();
                        var file=new FileInfo(source.Path);var fileKey=(file.FullName,file.Length,file.LastWriteTimeUtc);
                        StartupMeasurementTrace.Mark("audio_analysis_begin",note:cache.ContainsKey(fileKey)?"cache hit":"cache miss");
                        if(cache.TryGetValue(fileKey,out info))inspection=Task.FromResult(info);
                        else{inspectionCancel=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);inspection=AudioMediaInfo.InspectAsync(source.Path,inspectionCancel.Token);}
                    }
                    if(info==null)
                    {
                        if(!inspection!.IsCompleted){Publish(SharedAudioState.Preparing,"音声を解析中",source);stop.Token.WaitHandle.WaitOne(3);continue;}
                        info=inspection.GetAwaiter().GetResult();StartupMeasurementTrace.Mark("audio_analysis_complete",note:"worker observed completion");CancelInspection();
                        var file=new FileInfo(source.Path);if(cache.Count>=2)cache.Remove(cache.Keys.First());cache[(file.FullName,file.Length,file.LastWriteTimeUtc)]=info;
                    }
                    StartupMeasurementTrace.Once("audio_analysis_available",note:"cached results bypass completion branch");
                    if(!info.Supported){Publish(SharedAudioState.NoAudio,info.Notice,source);stop.Token.WaitHandle.WaitOne(4);continue;}
                    if(decode==null)BeginDecode(source,target);
                    if(decode==null){Publish(SharedAudioState.Waiting,"音声終端",source);stop.Token.WaitHandle.WaitOne(3);continue;}
                    if(!decode.Ready.IsCompleted){Publish(SharedAudioState.Preparing,"音声を準備中",source);stop.Token.WaitHandle.WaitOne(3);continue;}
                    decode.Ready.GetAwaiter().GetResult();
                    // Recheck the supplier before any staged samples reach WASAPI.
                    var latest=readPlayhead();now=MasterClock.Timestamp();target=latest.Estimate(now);
                    if(!latest.CanPrepare(now)||(latest.SessionId,latest.CueId,latest.Generation,latest.Path)!=key){ResetSource();continue;}
                    source=latest;
                    // Prepared PCM stays in the bounded decode queue until the clock advances.
                    if(!source.Usable(now)){Publish(SharedAudioState.Preparing,"再生開始を待機中",source);stop.Token.WaitHandle.WaitOne(3);continue;}
                    if(!epochStarted)
                    {
                        int budget=16;
                        while(budget-->0)
                        {
                            if(pending==null&&!decode.TryRead(out pending))break;
                            double end=pending!.Pts+pending.Frames/48000.0;
                            if(end<=target){dropped++;pending=null;continue;}
                            offset=(int)Math.Clamp(Math.Ceiling((target-pending.Pts)*48000),0,pending.Frames-1);
                            origin=pending.Pts+offset/48000.0;sink.BeginEpoch(source.CueId,source.Generation,origin);suspended=false;epochStarted=true;break;
                        }
                        if(!epochStarted){Publish(SharedAudioState.Preparing,"現在位置の音声を準備中",source);stop.Token.WaitHandle.WaitOne(2);continue;}
                    }
                    var stats=sink.Snapshot();int fillBudget=8;
                    // Target 30ms of hardware queue, independent of the device's full capacity.
                    while(stats.Padding<Math.Min(stats.Capacity,1440)&&fillBudget-->0)
                    {
                        if(pending==null){if(!decode.TryRead(out pending))break;offset=0;}
                        int limit=(int)Math.Min(stats.Capacity,1440)-(int)stats.Padding;
                        var write=sink.Write(pending!,offset,maximumFrames:limit);if(write.Stale)throw new InvalidDataException("旧世代の音声が採用されました。");
                        offset+=write.Frames;if(offset==pending!.Frames){pending=null;offset=0;}
                        stats=sink.Snapshot();if(write.Frames==0)break;
                    }
                    now=MasterClock.Timestamp();target=source.Estimate(now);
                    if(!running&&stats.Padding>0&&target>=origin)
                    {
                        StartupMeasurementTrace.Mark("audio_start_request",origin+stats.ConsumedSeconds);
                        sink.SetRunning(true);running=true;
                        StartupMeasurementTrace.Mark("audio_start_complete",origin+stats.ConsumedSeconds,"WASAPI Start returned");
                        lead=Math.Clamp((MasterClock.Timestamp()-prepareAt)+.025,.1,.5);
                    }
                    if(running)
                    {
                        stats=sink.Snapshot();double played=origin+stats.ConsumedSeconds;
                        var decision=policy.Evaluate(target,played,now,true);
                        if(decision.Action==AudioSyncAction.Reseek){reseeks++;BeginDecode(source,target,recovering:true);Publish(SharedAudioState.Resyncing,"現在位置へ再同期中",source);continue;}
                        if(now>=nextRate&&Math.Abs(decision.RateRatio-lastRate)>.00001){sink.SetRateRatio(decision.RateRatio);lastRate=decision.RateRatio;nextRate=now+.05;}
                        Publish(config.Muted?SharedAudioState.Muted:SharedAudioState.Playing,info.Notice,source,stats,decision.ErrorSeconds);
                    }
                    else Publish(SharedAudioState.Preparing,"音声の開始時刻を待機中",source,stats);
                }
                catch(OperationCanceledException)when(stop.IsCancellationRequested){break;}
                catch(Exception e)
                {
                    try{Close();}catch{}faulted=config.Version;Publish(SharedAudioState.Error,e.Message);config.Applied.TrySetException(e);
                }
                stop.Token.WaitHandle.WaitOne(3);
            }
        }
        finally{try{Close();}finally{Volatile.Read(ref configuration).Applied.TrySetCanceled();Publish(SharedAudioState.Off,"");}}
    }
    public async ValueTask DisposeAsync()
    {
        if(Interlocked.Exchange(ref disposed,1)!=0)return;
        stop.Cancel();await worker;stop.Dispose();
    }
}
