using System.Text.Json;
using LtcPlayer.App;
namespace LtcPlayer.Avalonia;

public static class CoverPacingVerification
{
    public static async Task RunAsync(MainWindow w,string folder)
    {
        Directory.CreateDirectory(folder);var results=new List<object>();bool failed=false;
        void Check(bool ok,string message){if(!ok)throw new IOException(message);}
        async Task Test(string name,Func<Task> body){try{await body();results.Add(new{test=name,status="PASS"});}catch(Exception e){failed=true;results.Add(new{test=name,status="FAIL",detail=e.ToString()});}File.WriteAllText(Path.Combine(folder,"results.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));}
        try{
            await w.Monitor.ConfigureAsync(new(),async(_,t)=>await Task.Delay(Timeout.Infinite,t));
            await w.StopSyncPreviewAsync();
            var displays=VideoRenderer.Displays();
            var display=displays.FirstOrDefault(d=>d.Primary==0);
            if(display.Width==0)display=displays.First();
            await w.ApplySettingsAsync(new(new(),display));
            var canvas=w.Surface.Canvas;
            byte[] Pixels(byte b,byte r){var data=new byte[display.Width*display.Height*4];for(int i=0;i<data.Length;i+=4){data[i]=b;data[i+2]=r;data[i+3]=255;}return data;}
            var video=Pixels(0,255);w.Cover.Image=new(Pixels(255,0),display.Width,display.Height,"full-size-test");
            foreach(var mode in new[]{"off","Standard","Compatibility"}){
                if(mode!="off")canvas.AttachOutput(display,Enum.Parse<OutputPresentationMode>(mode),true);
                foreach(double fps in new[]{24,25,29.97,30})await Test($"{mode}: {fps} fps, reversal, stalled-source fallback and endpoints",async()=>{
                    w.Cover.Set(false,0);w.Surface.Present(new(video,0));
                    w.Cover.Set(true,.3);long before=canvas.CompositionId;
                    // A cover request immediately after a video must not spend another slot.
                    w.Surface.RefreshCover();w.Surface.RefreshCover("hold");
                    Check(canvas.CompositionId==before,"duplicate composition after video");
                    double began=MasterClock.Timestamp();int frame=0;bool reversed=false;
                    while(MasterClock.Timestamp()-began<.7){
                        double elapsed=MasterClock.Timestamp()-began;
                        if(!reversed&&elapsed>=.15){double at=MasterClock.Timestamp(),amount=w.Cover.Amount(at);w.Cover.Set(false,.3,at);Check(Math.Abs(w.Cover.Amount(at)-amount)<.00001,"reversal discontinuity");reversed=true;}
                        w.Surface.Present(new(video,frame/fps));frame++;
                        double wait=began+frame/fps-MasterClock.Timestamp();if(wait>0)await Task.Delay(TimeSpan.FromSeconds(wait));
                    }
                    Check(w.Cover.Amount()==0,"zero endpoint");
                    long held=canvas.FrameId;w.Cover.Set(true,.15);await Task.Delay(260);
                    Check(canvas.FrameId==held&&w.Cover.Amount()==1,"held source advanced or fade incomplete");
                    var image=canvas.Readback();int center=(display.Height/2*display.Width+display.Width/2)*4;
                    Check(image[center]==255&&image[center+2]==0,"held fade was not composed");
                    var size=w.Surface.PixelSize;var preview=w.Surface.Readback();int middle=(size.Height/2*size.Width+size.Width/2)*4;
                    Check(preview[middle]==255&&preview[middle+2]==0,"preview differs from canvas");
                    w.Cover.Set(false,0);await Task.Delay(80);image=canvas.Readback();Check(image[center]==0&&image[center+2]==255,"instant uncover did not complete");
                });
                if(canvas.OutputAttached)canvas.DetachOutput();
            }
        }catch(Exception e){failed=true;File.WriteAllText(Path.Combine(folder,"fatal.txt"),e.ToString());}
        finally{if(w.Surface.Canvas.OutputAttached)w.Surface.Canvas.DetachOutput();await w.Monitor.DisposeAsync();MainWindow.Shutdown(failed?1:0);}
    }
}
