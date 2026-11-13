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
    readonly MediaDecoder decoder=new(reuseAnalysis:true,gpuScaling:true);
    CancellationTokenSource? preparation,cancel;
    Task? loop;
    long request;
    bool disposed,armed,audioSuppressed,sharedAudio,preparingPlayback;
    MediaInfo? media;
    PreviewAudioInfo? audioInfo;
    PreviewAudio? audio;
    PreviewState state;
    VideoFrame? last;
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
    public bool Armed=>armed;
    public string LoadedPath=>path;
    public double FrameRate=>media?.FrameRate??0;
    public (int Width,int Height) MediaSize=>(media?.Width??0,media?.Height??0);
    public event Action<PreviewSnapshot>? Changed;
    public PreviewPlayer(PreviewHost host)=>this.host=host;
    void Notify()=>Changed?.Invoke(Snapshot);
    void Activity(string message){IsBusy=message!="";BusyMessage=message;Notify();}
    void RejectArmed(){ObjectDisposedException.ThrowIf(disposed,this);if(armed)throw new InvalidOperationException("LTC同期中は手動再生を変更できません。");}
    public void CancelPreparation(){request++;preparation?.Cancel();if(IsBusy)cancel?.Cancel();}
    public void SetVolume(double value,bool mute){volume=(float)Math.Clamp(value,0,1);muted=mute;if(audio!=null)audio.Gain=muted?0:volume;}
#if VERIFICATION
    internal Func<CancellationToken,Task>? LoadBeforeFrameForTest{get;set;}
#endif
    public async Task<bool> LoadAsync(string file)
    {
        StartupMeasurementTrace.Mark("video_load_begin");
        RejectArmed();CancelPreparation();long version=request;
        using var operation=new CancellationTokenSource();preparation=operation;bool entered=false;
        try
        {
            await commands.WaitAsync(operation.Token);entered=true;RejectArmed();Activity("動画を読み込み中…");
            if(media!=null)position=CurrentPosition();
            await HaltAsync();state=media==null?PreviewState.Empty:PreviewState.Paused;
            var video=decoder.InspectAsync(file,operation.Token);
            var sound=PreviewAudio.InspectAsync(file,operation.Token);
            await Task.WhenAll(video,sound);
            StartupMeasurementTrace.Mark("selection_probes_complete");
#if VERIFICATION
            if(LoadBeforeFrameForTest is {} beforeFrame)await beforeFrame(operation.Token);
#endif
            var frame=await FirstFrameAsync(file,0,operation.Token);
            operation.Token.ThrowIfCancellationRequested();RejectArmed();if(version!=request)return false;
            MediaSessionId=Guid.NewGuid();SourceGeneration++;media=await video;audioInfo=await sound;path=file;position=origin=0;errorNotice="";state=PreviewState.Ready;
            host.Present(frame);StartupMeasurementTrace.Once("video_first_present",frame.Seconds,"selection still; API returned");last=frame;presented++;Notify();return true;
        }
        catch(OperationCanceledException)when(operation.IsCancellationRequested){return false;}
        catch(Exception e){if(version==request){errorNotice=e.Message;Notify();}throw;}
        finally{if(ReferenceEquals(preparation,operation)){preparation=null;Activity("");}if(entered)commands.Release();}
    }
    public async Task PlayAsync()
    {
        StartupMeasurementTrace.Mark("video_play_prepare_begin");
        FrameBuffer? preparedBuffer=null;
        await commands.WaitAsync();
        try
        {
            RejectArmed();if(media==null)throw new InvalidOperationException("キューを選択してください。");
            if(state==PreviewState.Playing)return;
            if(state==PreviewState.Paused&&loop is {IsCompleted:false})
            {
                if(audio!=null)await audio.SetPausedAsync(false,CancellationToken.None);
                clock.Start();state=PreviewState.Playing;Notify();return;
            }
            bool restart=state is PreviewState.Stopped or PreviewState.Ended;
            Activity("再生を準備中…");await HaltAsync();if(restart||position>=media.Duration-.001)position=0;
            errorNotice="";cancel=new();origin=position;
            PlaybackSessionId=Guid.NewGuid();preparingPlayback=true;Notify();
            if(audioInfo!.Supported&&!audioSuppressed)
            {
                audio=new(path,position,media.Duration,muted?0:volume);await audio.ReadyAsync(cancel.Token);
            }
            var size=host.CanvasSize;
            StartupMeasurementTrace.Mark("video_stream_prepare_begin");
            preparedBuffer=new FrameBuffer(path,size.Width,size.Height,origin,cancel.Token,decoder);
            if(!await preparedBuffer.WaitForDataAsync(cancel.Token))throw new InvalidDataException("表示できるフレームがありません。");
            StartupMeasurementTrace.Mark("video_stream_first_frame_ready");
            cancel.Token.ThrowIfCancellationRequested();RejectArmed();
            // Present the playback buffer's first frame, then keep consuming that buffer.
            if(!preparedBuffer.TryRead(out var first)||first==null)throw new InvalidDataException("表示できるフレームがありません。");
            host.Present(first);StartupMeasurementTrace.Once("video_first_present",first.Seconds,"playback buffer first frame; API returned");last=first;presented++;
            cancel.Token.ThrowIfCancellationRequested();RejectArmed();state=PreviewState.Playing;clock.Restart();
            StartupMeasurementTrace.Mark("video_clock_start");
            if(audio!=null)await audio.SetPausedAsync(false,cancel.Token);
            DecodeStarts++;var readyBuffer=preparedBuffer;preparedBuffer=null;loop=RunAsync(cancel.Token,readyBuffer);Notify();
        }
        catch(OperationCanceledException)when(armed||disposed||cancel?.IsCancellationRequested==true){await HaltAsync();state=media==null?PreviewState.Empty:PreviewState.Paused;}
        catch(InvalidOperationException)when(armed||disposed){throw;}
        catch(Exception e){errorNotice=e.Message;state=PreviewState.Error;await HaltAsync();throw;}
        finally{preparingPlayback=false;if(preparedBuffer!=null)await preparedBuffer.DisposeAsync();Activity("");commands.Release();}
    }
    public async Task PauseAsync()
    {
        await commands.WaitAsync();try
        {
            RejectArmed();if(state!=PreviewState.Playing)return;
            if(audio!=null)await audio.SetPausedAsync(true,CancellationToken.None);
            position=CurrentPosition();clock.Stop();state=PreviewState.Paused;Notify();
        }finally{commands.Release();}
    }
    public async Task StopAsync()
    {
        CancelPreparation();cancel?.Cancel();await commands.WaitAsync();try
        {
            if(media!=null)position=CurrentPosition();await HaltAsync();state=media==null?PreviewState.Empty:PreviewState.Stopped;
            // Keep the displayed frame. A following Play explicitly restarts at zero.
            Notify();
        }finally{commands.Release();}
    }
    public async Task SeekAsync(double seconds)
    {
        RejectArmed();CancelPreparation();long version=request;
        using var operation=new CancellationTokenSource();preparation=operation;bool entered=false,resume=false;
        try
        {
            await commands.WaitAsync(operation.Token);entered=true;RejectArmed();if(media==null)return;
            resume=state==PreviewState.Playing;Activity("再生位置を読み込み中…");await HaltAsync();state=PreviewState.Paused;
            double at=Math.Clamp(seconds,0,Math.Max(0,media.Duration-1/media.FrameRate));
            var frame=await FirstFrameAsync(path,at,operation.Token);operation.Token.ThrowIfCancellationRequested();RejectArmed();if(version!=request)return;
            SourceGeneration++;position=origin=at;host.Present(frame);last=frame;presented++;Notify();
        }
        catch(OperationCanceledException)when(operation.IsCancellationRequested){resume=false;}
        finally{if(ReferenceEquals(preparation,operation)){preparation=null;Activity("");}if(entered)commands.Release();}
        if(resume&&version==request&&!armed&&!disposed)await PlayAsync();
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
    public async Task SetArmedAsync(bool value)
    {
        armed=value;
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
        var size=host.CanvasSize;
        return await Task.Run(async()=>
        {
            StartupMeasurementTrace.Mark("video_still_decode_begin");
            await foreach(var frame in decoder.DecodeAsync(file,size.Width,size.Height,token,at)){StartupMeasurementTrace.Mark("video_still_first_frame_ready",frame.Seconds);return frame;}
            throw new InvalidDataException("表示できるフレームがありません。");
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
        catch(Exception e){state=PreviewState.Error;errorNotice=e.Message;clock.Stop();if(audio!=null){await audio.DisposeAsync();audio=null;}Notify();}
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
    }
}
