using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Moments;
internal static class VideoCadenceChecks
{
    public static async Task Run(Action<string> note){
        string folder=Home.Temp("cadence");
        try{
            double old=await Record(folder,false,note),current=await Record(folder,true,note);
            if(current<50 || current<old+4)throw new Exception($"Live video cadence regression: combined={old:F1}, independent={current:F1} changes/s");
        }finally{Home.Clear(folder);}
    }
    static async Task<double> Record(string folder,bool independent,Action<string> note){
        string name="moments_cadence_"+Guid.NewGuid().ToString("N"),file=Path.Combine(folder,independent?"independent.mkv":"combined.mkv");
        using var cancel=new CancellationTokenSource(TimeSpan.FromSeconds(25));
        using var pipe=new NamedPipeServerStream(name,PipeDirection.Out,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly,0,65536);
        var feed=Task.Run(async()=>{
            await pipe.WaitForConnectionAsync(cancel.Token);var clock=Stopwatch.StartNew();byte[] pcm=new byte[3840];
            try{
                if(independent)AudioPipeFormat.WriteHeader(pipe,cancel.Token);
                for(int i=0;!cancel.IsCancellationRequested;i++){
                    await pipe.WriteAsync(pcm,cancel.Token);
                    double delay=(i+1)*10-clock.Elapsed.TotalMilliseconds;
                    if(delay>0)await Task.Delay(TimeSpan.FromMilliseconds(delay),cancel.Token);
                }
            }catch(IOException){}catch(OperationCanceledException){}catch(ObjectDisposedException) when(cancel.IsCancellationRequested){}
        });
        // Encode wall-clock frame identifiers. A nominal 60-fps source stalled
        // by audio catches up in bursts and repeats these IDs; a metadata-only
        // FPS check would miss this. Discard startup/tail when counting changes.
        string video="color=black:size=32x32:rate=60,realtime,geq=lum='mod(floor(time(0)*60),220)+16':cb=128:cr=128[v]";
        string audio="[0:a]pan=stereo|c0=c0|c1=c1[a]";
        List<string> args=["-v","error","-y"];
        if(independent)args.AddRange(AudioPipeFormat.InputArguments(@"\\.\pipe\"+name));
        else args.AddRange(["-f","s16le","-ar","48000","-ac","4","-probesize","32","-analyzeduration","0","-i",@"\\.\pipe\"+name]);
        if(independent)CaptureGraphs.Add(args,video,audio);else args.AddRange(["-filter_complex",video+";"+audio]);
        args.AddRange(["-map","[v]","-map","[a]","-t","5","-c:v","libx264","-crf","0","-preset","ultrafast","-bf","0","-r","60","-fps_mode","cfr","-c:a","aac",file]);
        try{await Proc.Run("ffmpeg",args,cancel.Token);}
        finally{cancel.Cancel();pipe.Dispose();try{await feed;}catch(OperationCanceledException){}}
        double changes=await Changes(file);
        note($"{(independent?"Independent graphs":"Combined graph control")}: {changes:F1} real changes/s in 60 FPS file");
        if(independent){
            string exported=Path.Combine(folder,"saved.mp4");await Media.Remux(["-i",file],exported);
            double saved=await Changes(exported);
            if(Math.Abs(saved-changes)>1)throw new Exception("MP4 export lost moving frames");
            note($"Saved MP4: {saved:F1} changes/s");
        }
        return changes;
    }
    static async Task<double> Changes(string file){
        string raw=file+".gray";
        await Proc.Run("ffmpeg",["-v","error","-y","-i",file,"-map","0:v:0","-vf","scale=1:1,format=gray","-fps_mode","passthrough","-f","rawvideo",raw]);
        var bytes=File.ReadAllBytes(raw);
        if(bytes.Length<290 || bytes.Length>305)throw new Exception("Unexpected 60 FPS frame count: "+bytes.Length);
        return Enumerable.Range(60,180).Count(i=>bytes[i]!=bytes[i+1])/3d;
    }
}
