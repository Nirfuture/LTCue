using System.Text.Json;
using Avalonia.Controls;
using LtcPlayer.App;
namespace LtcPlayer.Avalonia;

public static class PlaybackIntegrationVerification
{
    public static async Task RunAsync(MainWindow w,string folder)
    {
        Directory.CreateDirectory(folder);var results=new List<object>();bool failed=false;int receiving=0;int frames=40;
        void Check(bool value,string message){if(!value)throw new IOException(message);}
        async Task Until(Func<bool> condition){for(int i=0;i<900;i++){if(condition())return;await Task.Delay(10);}throw new TimeoutException(JsonSerializer.Serialize(new{w.Preview.Snapshot,Sync=w.PreviewObservation,Output=w.LatestObservation}));}
        async Task Test(string name,Func<Task> body){try{await body();results.Add(new{test=name,status="PASS"});}catch(Exception e){failed=true;results.Add(new{test=name,status="FAIL",detail=e.ToString()});}File.WriteAllText(Path.Combine(folder,"results.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));}
        try
        {
            var display=VideoRenderer.Displays().First(d=>d.Primary!=0);
            await w.ApplySettingsAsync(new(new(Rate:"29.97 DF",OutputMode:OutputPresentationMode.Compatibility),display));
            var row=new CueRow{FilePath=Program.Option("--media")??Path.Combine(AppContext.BaseDirectory,"TestMedia","HAP_1_1080p_2997_10s.mov")};
            await ShowProgramService.RefreshAsync(row,CancellationToken.None);w.Editor.Add(row);
            var grid=w.FindControl<DataGrid>("CueGrid")!;grid.SelectedItem=row;
            await Until(()=>w.Preview.Snapshot.State==PreviewState.Ready);
            var canvas=w.Surface.Canvas;var session=canvas.SessionId;
            await Test("manual output toggle preserves canvas, decoder and clock",async()=>{
                await w.Preview.PlayAsync();await Until(()=>w.Preview.Snapshot.Position>.25);
                var decode=w.Preview.PlaybackSessionId;int starts=w.Preview.DecodeStarts;double before=w.Preview.Snapshot.Position;
                await w.ArmAsync();Check(canvas.OutputAttached&&w.IsArmed,"not attached");Check(w.Surface.Canvas.SessionId==session&&w.Preview.PlaybackSessionId==decode&&w.Preview.DecodeStarts==starts,"output restarted source");
                await Task.Delay(150);await w.DisarmAsync();Check(!canvas.OutputAttached&&w.Preview.Snapshot.State==PreviewState.Playing,"detach stopped source");
                Check(w.Preview.Snapshot.Position>=before&&w.Preview.PlaybackSessionId==decode,"clock reset");
            });
            await Test("paused frame survives repeated output toggles byte for byte",async()=>{
                await w.Preview.PauseAsync();byte[] image=canvas.Readback();long id=canvas.FrameId;double at=w.Preview.Snapshot.Position;
                for(int i=0;i<10;i++){await w.ArmAsync();await w.DisarmAsync();}
                Check(canvas.FrameId==id&&canvas.Readback().AsSpan().SequenceEqual(image),"held frame recomposed");Check(w.Preview.Snapshot.Position==at,"paused clock changed");
            });
            await Test("failed output creation releases lock and keeps preview",async()=>{
                var image=canvas.Readback();w.BeforeOutputAttachForTest=()=>throw new IOException("Synthetic attach failure");bool rejected=false;
                try{await w.ArmAsync();}catch(IOException){rejected=true;}finally{w.BeforeOutputAttachForTest=null;}
                Check(rejected&&!w.IsArmed&&!w.Editor.Locked&&!canvas.OutputAttached,"failed attach left locked");Check(canvas.Readback().AsSpan().SequenceEqual(image),"failed attach changed image");
            });
            await Test("manual stop holds image and explicit play restarts at zero",async()=>{
                await w.Preview.SeekAsync(2);var image=canvas.Readback();await w.Preview.StopAsync();await Task.Delay(100);
                Check(canvas.Readback().AsSpan().SequenceEqual(image),"stop cleared image");await w.Preview.PlayAsync();Check(w.Preview.Snapshot.Position<.5,"stop did not restart");await w.Preview.PauseAsync();
            });
            await Test("cover fade continues through output attach and detach",async()=>{
                w.Cover.Set(true,1);await Task.Delay(220);double amount=w.Cover.Amount();await w.ArmAsync();await Task.Delay(120);await w.DisarmAsync();
                Check(w.Cover.Amount()>amount&&w.Surface.Canvas.SessionId==session,"fade restarted");await Until(()=>!w.Cover.Transitioning);Check(w.Cover.Amount()==1,"fade endpoint");w.Cover.Set(false,0);await Task.Delay(40);
            });
            await w.Monitor.ConfigureAsync(new(Rate:"29.97 DF"),async(clock,token)=>{
                while(!token.IsCancellationRequested){if(Volatile.Read(ref receiving)!=0){int f=Interlocked.Increment(ref frames);clock.Accept(new TcFrame{Seconds=f/30,Frame=f%30,Drop=1},MasterClock.Timestamp());}await Task.Delay(33,token);}
            });
            await Test("LTC preempts manual playback without replacing canvas",async()=>{
                await w.Preview.PlayAsync();Volatile.Write(ref receiving,1);await Until(()=>w.PreviewObservation is {FramePts:>0,State:SyncState.LOCKED});
                Check(w.Preview.Armed&&PreviewAudio.ActiveStreams==0,"manual source survived LTC");Check(w.Surface.Canvas.SessionId==session,"LTC replaced canvas");
            });
            await Test("LTC session and input remain alive across output toggles",async()=>{
                var sync=w.SynchronizationSession;var clock=w.Monitor.Clock;int starts=w.Monitor.Starts;double pts=w.PreviewObservation!.FramePts??0;
                for(int i=0;i<5;i++){await w.ArmAsync();await Task.Delay(35);await w.DisarmAsync();}
                await Until(()=>w.PreviewObservation!.FramePts>pts);
                Check(w.SynchronizationSession==sync&&ReferenceEquals(clock,w.Monitor.Clock)&&w.Monitor.Starts==starts,"output replaced clock or sync source");
            });
            await Test("stop confirmation cancel continues output and LTC",async()=>{
                await w.ArmAsync();w.ConfirmStopForTest=async _=>{await Task.Delay(130);return false;};
                var sync=w.SynchronizationSession;double pts=w.PreviewObservation!.FramePts??0;
                Check(!await w.RequestStopAsync(false)&&w.IsArmed,"cancel stopped output");Check(w.SynchronizationSession==sync,"confirmation reset session");await Until(()=>w.PreviewObservation!.FramePts>pts);
                w.ConfirmStopForTest=_=>Task.FromResult(true);Check(await w.RequestStopAsync(false)&&!w.IsArmed,"confirmed stop failed");w.ConfirmStopForTest=null;
            });
            await Test("LTC loss holds; manual row starts in the same canvas; LTC resumes",async()=>{
                Volatile.Write(ref receiving,0);await Until(()=>w.PreviewObservation?.State==SyncState.LOST);double? held=w.PreviewObservation!.FramePts;await Task.Delay(140);Check(w.PreviewObservation.FramePts==held,"lost advanced");
                await w.ArmAsync();w.PlayManual(row);await Until(()=>w.LatestObservation is {Manual:true,FramePts:>.1});
                Check(w.Surface.Canvas.SessionId==session,"manual takeover replaced canvas");Volatile.Write(ref receiving,1);await Until(()=>w.LatestObservation is {Manual:false,State:SyncState.LOCKED});await w.DisarmAsync();
            });
            Volatile.Write(ref receiving,0);await Until(()=>w.PreviewObservation?.State==SyncState.LOST);await w.StopSyncPreviewAsync();
            await Test("latest load request wins; cancelled load cannot overwrite it",async()=>{
                string latest=Program.Option("--fixtures") is {} f?Path.Combine(f,"h264-aac.mp4"):row.FilePath;var a=w.Preview.LoadAsync(row.FilePath);var b=w.Preview.LoadAsync(latest);await Task.WhenAll(a,b);Check(w.Preview.LoadedPath==latest,"stale request overwrote latest file");
                Check(w.Preview.Snapshot.State==PreviewState.Ready&&!w.Preview.IsBusy&&!w.Preview.Armed,"latest request did not commit");
            });
            await Test("stop cancels the whole load-and-play request",async()=>{
                await w.Preview.LoadAsync(row.FilePath);await w.Preview.StopAsync();int starts=w.Preview.DecodeStarts;
                var next=new CueRow{FilePath=row.FilePath,Enabled=false};await ShowProgramService.RefreshAsync(next,CancellationToken.None);w.Editor.Add(next);
                var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                w.Preview.LoadBeforeFrameForTest=token=>{entered.TrySetResult();return Task.Delay(Timeout.Infinite,token);};
                try{
                    var request=w.MeasureManualStartAsync(next);await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    await w.Preview.StopAsync();await request;await Task.Delay(100);
                    Check(w.Preview.Snapshot.State==PreviewState.Stopped&&w.Preview.DecodeStarts==starts&&!w.Preview.IsBusy,"cancelled request restarted previous media");
                }finally{w.Preview.LoadBeforeFrameForTest=null;w.Editor.Delete(next);}
            });
            string? fixture=Program.Option("--fixtures");
            if(fixture!=null)
            {
                await Test("AAC audio suppression keeps video clock and decoder continuous",async()=>{
                    await w.Preview.LoadAsync(Path.Combine(fixture,"h264-aac.mp4"));w.Preview.AllowAudioForNextPlay();await w.Preview.PlayAsync();await Until(()=>w.Preview.Snapshot.Audio is {Consumed:>0});
                    var decode=w.Preview.PlaybackSessionId;double at=w.Preview.Snapshot.Position;int starts=w.Preview.DecodeStarts;
                    await w.ArmAsync();Check(PreviewAudio.ActiveStreams==0&&w.Preview.Snapshot.Audio==null,"output leaked audio");await Until(()=>w.Preview.Snapshot.Position>at+.2);
                    Check(w.Preview.PlaybackSessionId==decode&&w.Preview.DecodeStarts==starts,"audio stop restarted video");await w.DisarmAsync();Check(w.Preview.Snapshot.State==PreviewState.Playing&&PreviewAudio.ActiveStreams==0,"detach restarted audio");
                });
                await Test("HAP Q uses the shared canvas",async()=>{
                    await w.Preview.LoadAsync(Path.Combine(fixture,"hap-q-silent.mov"));await w.Preview.PlayAsync();await Until(()=>w.Preview.Snapshot.Position>.1);
                    Check(w.Surface.Canvas.SessionId==session&&canvas.Readback().Where((x,i)=>i%4!=3).Any(x=>x>30),"HAP Q image missing");await w.Preview.PauseAsync();
                });
            }
            await Test("preview stays within quarter pixels",async()=>{
                await Task.Delay(100);var s=canvas.PreviewStatus();Check(s.Presented>0&&s.Width<=canvas.Width/2&&s.Height<=canvas.Height/2,"preview size/updates");
            });
        }
        catch(Exception e){failed=true;File.WriteAllText(Path.Combine(folder,"fatal.txt"),e.ToString());}
        finally{Volatile.Write(ref receiving,0);w.BeforeOutputAttachForTest=null;w.ConfirmStopForTest=null;await w.DisarmAsync();await w.StopSyncPreviewAsync();await w.Preview.DisposeAsync();await w.Monitor.DisposeAsync();MainWindow.Shutdown(failed?1:0);}
    }
}
