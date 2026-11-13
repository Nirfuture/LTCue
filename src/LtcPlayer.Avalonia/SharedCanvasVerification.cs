using System.Text.Json;
using LtcPlayer.App;
namespace LtcPlayer.Avalonia;
public static class SharedCanvasVerification
{
    public static async Task RunAsync(MainWindow w,string folder)
    {
        Directory.CreateDirectory(folder); var results=new List<object>(); bool failed=false;
        void Check(bool ok,string message){if(!ok)throw new IOException(message);}
        async Task Test(string name,Func<Task> body){try{await body();results.Add(new{test=name,status="PASS"});}catch(Exception e){failed=true;results.Add(new{test=name,status="FAIL",detail=e.ToString()});}}
        RenderSession? canvas=null;
        try
        {
            var display=VideoRenderer.Displays().First(d=>d.Primary!=0);
            var target=w.Surface.BeginProgram();canvas=new(display.Width,display.Height);canvas.AttachPreview(target);
            var pixels=new byte[display.Width*display.Height*4];for(int i=0;i<pixels.Length;i+=4){pixels[i+2]=pixels[i+3]=255;}
            var coverPixels=new byte[64*64*4];for(int i=0;i<coverPixels.Length;i+=4)coverPixels[i]=coverPixels[i+3]=255;
            canvas.Present(new(pixels,2.5));var id=canvas.SessionId;var frame=canvas.FrameId;
            await Test("BGRA canvas supports non-block-aligned display dimensions",()=>{
                using var odd=new RenderSession(1366,768);var image=new byte[1366*768*4];for(int i=0;i<image.Length;i+=4)image[i+1]=image[i+3]=255;
                odd.Present(new(image,0));Check(odd.Readback().AsSpan().SequenceEqual(image),"BGRA alignment");return Task.CompletedTask;
            });
            await Test("composition without external window",()=>{Check(canvas.Readback().AsSpan().SequenceEqual(pixels),"offscreen pixels");return Task.CompletedTask;});
            foreach(var mode in Enum.GetValues<OutputPresentationMode>())
            await Test("attach/detach preserves session and image / "+mode,async()=>{
                for(int i=0;i<50;i++){
                    canvas.AttachOutput(display,mode,true);Check(canvas.SessionId==id&&canvas.FrameId==frame&&canvas.FramePts==2.5,"playback changed");
                    Check(canvas.Readback().AsSpan().SequenceEqual(pixels),"attach changed composition");
                    canvas.DetachOutput();Check(!canvas.OutputAttached&&canvas.SessionId==id,"detach changed session");
                    Check(canvas.Readback().AsSpan().SequenceEqual(pixels),"detach changed image");await Task.Delay(10);
                }
            });
            await Test("preview is bounded to quarter pixels and shows same composition",async()=>{
                for(int i=0;i<30;i++){canvas.UpdatePreview();await Task.Delay(10);}
                var s=canvas.PreviewStatus();Check(s.Presented>0,"preview not presented");Check(s.Width<=display.Width/2&&s.Height<=display.Height/2,"preview too large");
                var image=canvas.PreviewReadback();int at=(s.Height/2*s.Width+s.Width/2)*4;Check(image[at+2]==255&&image[at]==0,"preview differs");
            });
            await Test("HAP GPU frame uses the same canvas",()=>{
                var bc1=new byte[64/4*64/4*8];for(int i=0;i<bc1.Length;i+=8){bc1[i+1]=bc1[i+3]=248;}
                canvas.Present(new(bc1,3,1,64,64));var image=canvas.Readback();int at=(display.Height/2*display.Width+display.Width/2)*4;
                Check(image[at+2]==255&&image[at]==0,"HAP canvas differs");canvas.Present(new(pixels,2.5));return Task.CompletedTask;
            });
            await Test("cover midpoint retained across output toggle",()=>{
                canvas.SetCover(new(coverPixels,64,64,"fixture"));canvas.SetCoverMix(.5f);canvas.Redraw();var before=canvas.Readback();
                int at=(display.Height/2*display.Width+display.Width/2)*4;Check(Math.Abs(before[at]-128)<=1&&Math.Abs(before[at+2]-128)<=1,"blend midpoint");
                var composition=canvas.CompositionId;canvas.AttachOutput(display,OutputPresentationMode.Compatibility,true);canvas.DetachOutput();
                Check(canvas.CompositionId==composition&&canvas.Readback().AsSpan().SequenceEqual(before),"toggle recomposed");return Task.CompletedTask;
            });
            await Test("failed attach leaves composition usable",()=>{
                var absent=display;absent.Name="MISSING-TEST-DISPLAY";bool rejected=false;
                try{canvas.AttachOutput(absent,OutputPresentationMode.Compatibility,true);}catch(IOException){rejected=true;}
                Check(rejected&&!canvas.OutputAttached,"missing display accepted");canvas.SetCoverMix(0);canvas.Redraw();Check(canvas.Readback().AsSpan().SequenceEqual(pixels),"canvas damaged");return Task.CompletedTask;
            });
        }
        finally
        {
            canvas?.Dispose();w.Surface.EndProgram();await w.Monitor.DisposeAsync();
            File.WriteAllText(Path.Combine(folder,"results.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));MainWindow.Shutdown(failed?1:0);
        }
    }
}
