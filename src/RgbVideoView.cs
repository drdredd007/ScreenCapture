using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Moments;

// Render display-referred SDR pixels through WPF's normal bitmap path. The Windows
// video presenter may apply an additional driver-controlled video range conversion.
public sealed class RgbVideoView : Image
{
    CancellationTokenSource? stop;
    Task worker=Task.CompletedTask;
    string? file;
    bool running;
    double desired;
    int revision;
    WriteableBitmap? bitmap;
    public event Action<string>? Failed;

    internal void Display(string path,ClipInfo info,double time,bool play,bool seek=false)
    {
        double frameDuration=1/Math.Clamp(info.Fps>0?info.Fps:30,1,240);
        time=Math.Clamp(time,0,Math.Max(0,info.Duration-frameDuration));
        bool same=string.Equals(file,path,StringComparison.OrdinalIgnoreCase);
        double previous=Volatile.Read(ref desired);Volatile.Write(ref desired,time);
        if(same && play==running && ((!seek && play) || Math.Abs(previous-time)<.0001))return;
        bool switched=!same;
        file=path;running=play;int version=++revision;
        stop?.Cancel();var cancel=new CancellationTokenSource();stop=cancel;
        var prior=worker;
        worker=Task.Run(async()=>{
            try{
                await prior.ConfigureAwait(false);
                var token=cancel.Token;token.ThrowIfCancellationRequested();
                // Coalesce rapid scrubbing before starting a new decoder.
                if(!play && !switched)await Task.Delay(30,token).ConfigureAwait(false);
                await Decode(path,info,time,play,version,token).ConfigureAwait(false);
            }catch(OperationCanceledException){}catch(Exception ex){
                if(!cancel.IsCancellationRequested)await Dispatcher.InvokeAsync(()=>{if(revision==version)Failed?.Invoke(ex.Message);});
            }finally{await Dispatcher.InvokeAsync(()=>{if(stop==cancel)stop=null;});cancel.Dispose();}
        });
    }
    async Task Decode(string path,ClipInfo info,double start,bool play,int version,CancellationToken token)
    {
        double scale=Math.Min(1,Math.Min(1280d/info.Width,720d/info.Height));
        int w=Math.Max(1,(int)Math.Round(info.Width*scale)),h=Math.Max(1,(int)Math.Round(info.Height*scale));
        double rate=Math.Clamp(info.Fps>0?info.Fps:30,1,30);
        string filter=(info.LegacyRgbTags?Media.DesktopYuv+",":"")+(play?$"fps={Home.N(rate)},":"")+
            $"scale={w}:{h}:flags=bilinear:out_range=pc,format=bgr0";
        List<string> args=["-v","error","-nostdin","-filter_threads","2","-threads","4","-ss",Home.N(start),"-i",path,
            "-map","0:v:0","-an","-sn","-dn","-vf",filter];
        if(!play)args.AddRange(["-frames:v","1"]);
        args.AddRange(["-c:v","rawvideo","-threads:v","1","-f","rawvideo","pipe:1"]);
        using var p=Process.Start(Proc.Info("ffmpeg",args))??throw new IOException("Could not open the preview.");
        ChildJob.Track(p);
        using var reg=token.Register(()=>{try{p.Kill(true);}catch(InvalidOperationException){}});
        var error=p.StandardError.ReadToEndAsync();byte[] pixels=new byte[checked(w*h*4)];int count=0;
        try{
            while(true){
                int size=0;
                while(size<pixels.Length){
                    int n=await p.StandardOutput.BaseStream.ReadAsync(pixels.AsMemory(size),token).ConfigureAwait(false);
                    if(n==0)break;size+=n;
                }
                if(size!=pixels.Length)break;
                double at=start+count/rate;count++;
                if(play){
                    double ahead;
                    while((ahead=at-Volatile.Read(ref desired))>.5/rate)
                        await Task.Delay(Math.Clamp((int)(ahead*1000),2,18),token).ConfigureAwait(false);
                    if(count>1 && at<Volatile.Read(ref desired)-.08)continue;
                }
                await Dispatcher.InvokeAsync(()=>{
                    if(token.IsCancellationRequested || revision!=version)return;
                    if(bitmap==null || bitmap.PixelWidth!=w || bitmap.PixelHeight!=h)bitmap=new WriteableBitmap(w,h,96,96,PixelFormats.Bgr32,null);
                    bitmap.WritePixels(new Int32Rect(0,0,w,h),pixels,w*4,0);
                    if(Source!=bitmap)Source=bitmap;
                },DispatcherPriority.Render,token);
            }
            await p.WaitForExitAsync(token).ConfigureAwait(false);string err=await error.ConfigureAwait(false);
            if(p.ExitCode!=0 || count==0)throw new IOException(string.IsNullOrWhiteSpace(err)?"The decoder returned no frame.":err.Trim());
        }finally{
            if(!p.HasExited){try{p.Kill(true);}catch(InvalidOperationException){}await p.WaitForExitAsync().ConfigureAwait(false);}
        }
    }
    internal void PauseFrames(){
        if(running){file=null;revision++;stop?.Cancel();running=false;}
    }
    internal Task ClearVideo(){
        if(file!=null){file=null;revision++;stop?.Cancel();running=false;}
        Source=null;bitmap=null;
        return worker;
    }
}
