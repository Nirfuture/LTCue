using System.Diagnostics;
using LtcPlayer.App;
namespace LtcPlayer.Avalonia;
public enum PreviewState {Empty,Ready,Playing,Paused,Stopped,Ended,Error}
public record PreviewSnapshot(PreviewState State,double Position,double Duration,double? FramePts,string Name,string Notice,int Presented,AudioProgress? Audio);

// Manual source of the shared canvas. LTC ownership suspends this source, not the canvas.
public sealed class PreviewPlayer : IAsyncDisposable
{
    readonly PreviewHost host;
    readonly SemaphoreSlim commands=new(1);
    readonly Stopwatch clock=new();
    readonly MediaDecoder decoder;
    internal MediaAnalysisService Analysis{get;}=new();
    internal HapIndexCache IndexCache=>Analysis.IndexCache;
    CancellationTokenSource? preparation,cancel;
    Task? loop;
    long request;
    bool disposed,syncOwnsSource,audioSuppressed,sharedAudio,preparingPlayback;
    MediaInfo? media;
    PreviewAudioInfo? audioInfo;
    PreviewAudio? audio;
    PreviewState state;
    VideoFrame? last,firstFrame;
    (int Width,int Height) firstFrameSize;
    string path="",errorNotice="";
    double origin,position;
    int presented;
    float volume=.5f;
    bool muted;
    public PreviewSnapshot Snapshot=>new(state,position,media?.Duration??0,last?.Seconds,Path.GetFileName(path),errorNotice!=""?errorNotice:sharedAudio?"":audioInfo?.Notice??"",presented,audio?.Progress);
    public bool IsBusy{get;private set;}
    public string BusyMessage{get;private set;}="";
    public Guid? PlaybackSessionId{get;private set;}
    public Guid MediaSessionId{get;private set;}
    public int SourceGeneration{get;private set;}
    public int DecodeStarts{get;private set;}
    internal bool PreparingPlayback=>preparingPlayback&&cancel is {IsCancellationRequested:false};
    internal CancellationToken PreparationCancellation=>cancel?.Token??new CancellationToken(true);
    public bool IsSyncOwned=>syncOwnsSource;
    internal Func<bool>? ManualAuthority{get;set;}
    bool AuthorityGranted=>ManualAuthority?.Invoke()??true;
    public string LoadedPath=>path;
    internal bool LoadedVersionMatches{get{try{return media?.FileVersion==MediaFileVersion.Capture(path);}catch(Exception e)when(e is IOException or UnauthorizedAccessException or ArgumentException){return false;}}}
    public double FrameRate=>media?.FrameRate??0;
    public (int Width,int Height) MediaSize=>(media?.Width??0,media?.Height??0);
    public event Action<PreviewSnapshot>? Changed;
    public PreviewPlayer(PreviewHost host){this.host=host;decoder=new(reuseAnalysis:true,gpuScaling:true,indexCache:IndexCache,analysis:Analysis);}
    void Notify()=>Changed?.Invoke(Snapshot);
    void Activity(string message){IsBusy=message!="";BusyMessage=message;Notify();}
    void RejectManualUnavailable(){ObjectDisposedException.ThrowIf(disposed,this);if(syncOwnsSource)throw new InvalidOperationException("LTC同期中は操作できません");if(!AuthorityGranted)throw new OperationCanceledException("操作権限の変更により中止しました");}
    public void CancelPreparation(){request++;preparation?.Cancel();if(IsBusy)cancel?.Cancel();}
    public void SetVolume(double value,bool mute){volume=(float)Math.Clamp(value,0,1);muted=mute;if(audio!=null)audio.Gain=muted?0:volume;}
#if VERIFICATION
    internal int LoadRequests{get;private set;}
    internal int StillDecodeStarts{get;private set;}
    internal Func<CancellationToken,Task>? LoadBeforeFrameForTest{get;set;}
    internal Func<CancellationToken,Task>? LoadBeforePlayForTest{get;set;}
    internal Func<FrameBuffer,CancellationToken,Task>? PreparedBufferForTest{get;set;}
    internal VideoFrame? LastFrameForTest=>last;
    internal Func<CancellationToken,Task>? SeekBeforePresentForTest{get;set;}
#endif
    public Task<bool> LoadAsync(string file)=>LoadCoreAsync(file,null);
    // The callback is the UI adoption boundary of this one explicit play request.
    // No prepared buffer escapes this method except by transfer to RunAsync.
    internal Task<bool> LoadAndPlayAsync(string file,Func<bool> adopt)=>LoadCoreAsync(file,adopt);
    async Task<bool> LoadCoreAsync(string file,Func<bool>? adopt)
    {
#if VERIFICATION
        LoadRequests++;
#endif
        StartupMeasurementTrace.Mark("video_load_begin");
        RejectManualUnavailable();CancelPreparation();long version=request;
        using var operation=new CancellationTokenSource();preparation=operation;bool entered=false;
        FrameBuffer? preparedBuffer=null;
        bool combined=adopt!=null&&Path.GetExtension(file).Equals(".mp4",StringComparison.OrdinalIgnoreCase);
        try
        {
            await commands.WaitAsync(operation.Token);entered=true;RejectManualUnavailable();Activity("素材を確認中 "+Path.GetFileName(file));
            if(media!=null)position=CurrentPosition();
            await HaltAsync();state=media==null?PreviewState.Empty:PreviewState.Paused;
            var video=decoder.InspectAsync(file,operation.Token);
            var sound=PreviewAudio.InspectAsync(file,operation.Token,Analysis);
            await Task.WhenAll(video,sound);
            StartupMeasurementTrace.Mark("selection_probes_complete");
#if VERIFICATION
            if(LoadBeforeFrameForTest is {} beforeFrame)await beforeFrame(operation.Token);
#endif
            operation.Token.ThrowIfCancellationRequested();
            var size=host.CanvasSize;
            VideoFrame frame;
            if(combined){
                cancel=new();
                preparedBuffer=new FrameBuffer(file,size.Width,size.Height,0,cancel.Token,decoder);
                if(!await preparedBuffer.WaitForDataAsync(operation.Token)||!preparedBuffer.TryRead(out var first)||first==null)throw new InvalidDataException("表示できるフレームなし");
                frame=first;
            }else frame=await FirstFrameAsync(file,0,operation.Token);
            operation.Token.ThrowIfCancellationRequested();RejectManualUnavailable();if(version!=request)return false;
            if(combined&&((await video).FileVersion!=MediaFileVersion.Capture(file)||size!=host.CanvasSize))return false;
            MediaSessionId=Guid.NewGuid();SourceGeneration++;media=await video;audioInfo=await sound;path=file;position=origin=0;errorNotice="";state=PreviewState.Ready;
            host.Present(frame);StartupMeasurementTrace.Once("video_first_present",frame.Seconds,"selection still; API returned");
            firstFrame=last=frame;firstFrameSize=size;presented++;Notify();
            if(!combined)return true;
#if VERIFICATION
            if(PreparedBufferForTest is {} prepared)await prepared(preparedBuffer!,operation.Token);
            if(LoadBeforePlayForTest is {} beforePlay)await beforePlay(operation.Token);
#endif
            operation.Token.ThrowIfCancellationRequested();RejectManualUnavailable();
            if(version!=request||!adopt!())return false;
            operation.Token.ThrowIfCancellationRequested();RejectManualUnavailable();
            if(version!=request||!LoadedVersionMatches||size!=host.CanvasSize)return false;
            PlaybackSessionId=Guid.NewGuid();preparingPlayback=true;Notify();
            if(audioInfo!.Supported&&!audioSuppressed){audio=new(path,0,media.Duration,muted?0:volume);await audio.ReadyAsync(cancel!.Token);}
            operation.Token.ThrowIfCancellationRequested();cancel!.Token.ThrowIfCancellationRequested();RejectManualUnavailable();
            if(version!=request||!LoadedVersionMatches||size!=host.CanvasSize)return false;
            state=PreviewState.Playing;clock.Restart();StartupMeasurementTrace.Mark("video_clock_start");
            if(audio!=null)await audio.SetPausedAsync(false,cancel.Token);
            DecodeStarts++;var readyBuffer=preparedBuffer!;preparedBuffer=null;loop=RunAsync(cancel.Token,readyBuffer);Notify();return true;
        }
        catch(OperationCanceledException)when(operation.IsCancellationRequested||!AuthorityGranted){return false;}
        catch(Exception e){if(version==request){errorNotice=e.Message;Notify();}throw;}
        finally{
            if(combined&&loop==null&&entered){await HaltAsync();if(state==PreviewState.Playing)state=PreviewState.Paused;}
            if(preparedBuffer!=null)await preparedBuffer.DisposeAsync();
            if(combined)preparingPlayback=false;
            if(ReferenceEquals(preparation,operation)){preparation=null;Activity("");}if(entered)commands.Release();
        }
    }
    public async Task PlayAsync()
    {
        StartupMeasurementTrace.Mark("video_play_prepare_begin");
        FrameBuffer? preparedBuffer=null;
        await commands.WaitAsync();
        try
        {
            RejectManualUnavailable();if(media==null)throw new InvalidOperationException("動画が未読込です");
            if(state==PreviewState.Playing)return;
            if(state==PreviewState.Paused&&loop is {IsCompleted:false})
            {
                if(audio!=null)await audio.SetPausedAsync(false,CancellationToken.None);
                clock.Start();state=PreviewState.Playing;Notify();return;
            }
            bool restart=state is PreviewState.Stopped or PreviewState.Ended;
            Activity("再生準備中");await HaltAsync();if(restart||position>=media.Duration-.001)position=0;
            errorNotice="";cancel=new();origin=position;
            PlaybackSessionId=Guid.NewGuid();preparingPlayback=true;Notify();
            if(audioInfo!.Supported&&!audioSuppressed)
            {
                audio=new(path,position,media.Duration,muted?0:volume);await audio.ReadyAsync(cancel.Token);
            }
            var size=host.CanvasSize;
            StartupMeasurementTrace.Mark("video_stream_prepare_begin");
            preparedBuffer=new FrameBuffer(path,size.Width,size.Height,origin,cancel.Token,decoder);
            if(!await preparedBuffer.WaitForDataAsync(cancel.Token))throw new InvalidDataException("表示できるフレームなし");
            StartupMeasurementTrace.Mark("video_stream_first_frame_ready");
            cancel.Token.ThrowIfCancellationRequested();RejectManualUnavailable();
            // Present the playback buffer's first frame, then keep consuming that buffer.
            if(!preparedBuffer.TryRead(out var first)||first==null)throw new InvalidDataException("表示できるフレームなし");
            host.Present(first);StartupMeasurementTrace.Once("video_first_present",first.Seconds,"playback buffer first frame; API returned");last=first;presented++;
            cancel.Token.ThrowIfCancellationRequested();RejectManualUnavailable();state=PreviewState.Playing;clock.Restart();
            StartupMeasurementTrace.Mark("video_clock_start");
            if(audio!=null)await audio.SetPausedAsync(false,cancel.Token);
            DecodeStarts++;var readyBuffer=preparedBuffer;preparedBuffer=null;loop=RunAsync(cancel.Token,readyBuffer);Notify();
        }
        catch(OperationCanceledException)when(syncOwnsSource||disposed||!AuthorityGranted||cancel?.IsCancellationRequested==true){await HaltAsync();state=media==null?PreviewState.Empty:PreviewState.Paused;}
        catch(InvalidOperationException)when(syncOwnsSource||disposed){throw;}
        catch(Exception e){errorNotice=e.Message;state=PreviewState.Error;await HaltAsync();if(host.Ready&&host.Canvas.SharedDeviceError>=0){host.Black();last=null;}Notify();throw;}
        finally{preparingPlayback=false;if(preparedBuffer!=null)await preparedBuffer.DisposeAsync();Activity("");commands.Release();}
    }
    public async Task PauseAsync()
    {
        await commands.WaitAsync();try
        {
            RejectManualUnavailable();if(state!=PreviewState.Playing)return;
            if(audio!=null)await audio.SetPausedAsync(true,CancellationToken.None);
            position=CurrentPosition();clock.Stop();state=PreviewState.Paused;Notify();
        }finally{commands.Release();}
    }
    public async Task StopAsync()
    {
        CancelPreparation();cancel?.Cancel();long version=request;
        using var operation=new CancellationTokenSource();preparation=operation;bool entered=false;
        try
        {
            await commands.WaitAsync(operation.Token);entered=true;
            state=media==null?PreviewState.Empty:PreviewState.Stopped;position=origin=0;
            SourceGeneration++;Notify();await HaltAsync();
            operation.Token.ThrowIfCancellationRequested();
            // A manual stop must never replace an LTC-owned image or redraw a failed GPU.
            if(media==null||syncOwnsSource||!AuthorityGranted||disposed||!host.Ready||host.Canvas.SharedDeviceError<0)return;
            var size=host.CanvasSize;
            var frame=firstFrame;
            if(frame==null||firstFrameSize!=size){
                Activity("先頭フレームを準備中");
                frame=await FirstFrameAsync(path,0,operation.Token);
            }
            operation.Token.ThrowIfCancellationRequested();
            if(version!=request||syncOwnsSource||!AuthorityGranted||disposed)return;
            host.Present(frame);firstFrame=last=frame;firstFrameSize=size;presented++;errorNotice="";Notify();
        }
        catch(OperationCanceledException)when(operation.IsCancellationRequested||!AuthorityGranted){}
        catch(Exception e){if(version==request){errorNotice=e.Message;Notify();}throw;}
        finally{if(ReferenceEquals(preparation,operation)){preparation=null;Activity("");}if(entered)commands.Release();}
    }
    public Task ClearAsync()=>ClearSourceAsync(true);
    // Only the coordinator calls this while its source-arbitration gate is held.
    internal Task ClearReleasedSourceAsync()=>ClearSourceAsync(false);
    void CheckClearAuthority(bool manual){if(manual)RejectManualUnavailable();else{ObjectDisposedException.ThrowIf(disposed,this);if(syncOwnsSource)throw new InvalidOperationException("同期中の素材は解除できません");}}
    async Task ClearSourceAsync(bool manual)
    {
        CheckClearAuthority(manual);CancelPreparation();cancel?.Cancel();await commands.WaitAsync();
        try{
            CheckClearAuthority(manual);await HaltAsync();CheckClearAuthority(manual);
            media=null;audioInfo=null;path="";firstFrame=last=null;firstFrameSize=default;
            position=origin=0;errorNotice="";PlaybackSessionId=null;MediaSessionId=Guid.NewGuid();SourceGeneration++;state=PreviewState.Empty;
            host.Black();Notify();
        }finally{commands.Release();}
    }
    public async Task SeekAsync(double seconds)
    {
        RejectManualUnavailable();CancelPreparation();long version=request;
        using var operation=new CancellationTokenSource();preparation=operation;bool entered=false,resume=false;
        try
        {
            await commands.WaitAsync(operation.Token);entered=true;RejectManualUnavailable();if(media==null)return;
            resume=state==PreviewState.Playing;Activity("再生位置を準備中");await HaltAsync();state=PreviewState.Paused;
            double at=Math.Clamp(seconds,0,Math.Max(0,media.Duration-1/media.FrameRate));
            var frame=await FirstFrameAsync(path,at,operation.Token);
#if VERIFICATION
            if(SeekBeforePresentForTest!=null)await SeekBeforePresentForTest(operation.Token);
#endif
            operation.Token.ThrowIfCancellationRequested();RejectManualUnavailable();if(version!=request)return;
            SourceGeneration++;position=origin=at;host.Present(frame);last=frame;presented++;Notify();
        }
        catch(OperationCanceledException)when(operation.IsCancellationRequested||!AuthorityGranted){resume=false;}
        finally{if(ReferenceEquals(preparation,operation)){preparation=null;Activity("");}if(entered)commands.Release();}
        if(resume&&version==request&&!syncOwnsSource&&!disposed&&AuthorityGranted)await PlayAsync();
    }
    public async Task SuppressAudioAsync()
    {
        audioSuppressed=true;await commands.WaitAsync();try
        {
            if(audio==null)return;
            // Snapshot the audio clock and switch to monotonic time before disposal awaits.
            position=CurrentPosition();origin=position;
            if(state==PreviewState.Playing)clock.Restart();else clock.Reset();
            var old=audio;audio=null;await old.DisposeAsync();Notify();
        }finally{commands.Release();}
    }
    public async Task UseSharedAudioAsync(){sharedAudio=true;await SuppressAudioAsync();}
    public void AllowAudioForNextPlay(){if(!sharedAudio)audioSuppressed=false;}
    public async Task SetSyncOwnershipAsync(bool value)
    {
        syncOwnsSource=value;
        if(value){CancelPreparation();cancel?.Cancel();}
        await commands.WaitAsync();try
        {
            if(value){if(media!=null)position=CurrentPosition();await HaltAsync();state=media==null?PreviewState.Empty:PreviewState.Stopped;}
            Notify();
        }finally{commands.Release();}
    }
    internal double PositionForAudio=>state==PreviewState.Playing?CurrentPosition():position;
    double CurrentPosition()=>media==null?position:Math.Min(media.Duration,origin+(audio?.Progress.Seconds??clock.Elapsed.TotalSeconds));
    async Task<VideoFrame> FirstFrameAsync(string file,double at,CancellationToken token)
    {
#if VERIFICATION
        StillDecodeStarts++;
#endif
        var size=host.CanvasSize;long version=request;var owner=preparation;
        void Progress(MediaPreparationStage stage)=>global::Avalonia.Threading.Dispatcher.UIThread.Post(()=>{
            if(owner==null||!ReferenceEquals(preparation,owner)||disposed||token.IsCancellationRequested||version!=request||syncOwnsSource||!AuthorityGranted)return;
            string message=stage switch{MediaPreparationStage.WaitingForIndex=>"HAP準備待ち",MediaPreparationStage.Indexing=>"HAP位置情報を準備中",MediaPreparationStage.ReadingFrame=>"フレームを読込中",_=>"フレームを展開中"};
            Activity(message+" "+Path.GetFileName(file));
        });
        return await Task.Run(async()=>
        {
            StartupMeasurementTrace.Mark("video_still_decode_begin");
            await foreach(var frame in decoder.DecodeAsync(file,size.Width,size.Height,token,at,Progress)){StartupMeasurementTrace.Mark("video_still_first_frame_ready",frame.Seconds);return frame;}
            throw new InvalidDataException("表示できるフレームなし");
        },token);
    }
    async Task RunAsync(CancellationToken token,FrameBuffer preparedBuffer)
    {
        try
        {
            await using var buffer=preparedBuffer;
            VideoFrame? next=null,current=last;
            while(!token.IsCancellationRequested)
            {
                if(state==PreviewState.Playing)
                {
                    if(!AuthorityGranted){position=CurrentPosition();clock.Stop();state=PreviewState.Paused;Notify();return;}
                    if(audio?.Error is {} e)throw e;
                    position=CurrentPosition();
                    if(next==null)buffer.TryRead(out next);
                    while(next!=null&&next.Seconds<=position+.000001){current=next;next=null;buffer.TryRead(out next);}
                    if(current!=null&&!ReferenceEquals(current,last)){host.Present(current);StartupMeasurementTrace.Once("video_playback_first_present",current.Seconds,"API returned");last=current;presented++;}
                    if(position>=media!.Duration){state=PreviewState.Ended;clock.Stop();if(audio!=null){await audio.DisposeAsync();audio=null;}Notify();return;}
                    Notify();
                }
                await Task.Delay(8,token);
            }
        }
        catch(OperationCanceledException)when(token.IsCancellationRequested){}
        catch(Exception e){Analysis.Invalidate(path);state=PreviewState.Error;errorNotice=e.Message;clock.Stop();if(audio!=null){await audio.DisposeAsync();audio=null;}if(host.Ready&&host.Canvas.SharedDeviceError>=0){host.Black();last=null;}Notify();}
    }
    async Task HaltAsync()
    {
        cancel?.Cancel();if(loop!=null){await loop;loop=null;}
        if(audio!=null){await audio.DisposeAsync();audio=null;}
        cancel?.Dispose();cancel=null;clock.Reset();
    }
    public async ValueTask DisposeAsync()
    {
        disposed=true;CancelPreparation();cancel?.Cancel();await commands.WaitAsync();try{await HaltAsync();}finally{commands.Release();}
        await Analysis.DisposeAsync();
    }
}
