using System.Runtime.InteropServices;
using System.Text;
namespace Moments;
internal sealed class Capture
{
    public readonly string Label,Folder;
    public readonly bool Replay;
    public readonly int Minutes;
    public readonly Target Source;
    public readonly Stopwatch Clock=new();
    readonly Profile profile;
    readonly bool system,mic,separateApps;
    AppAudioArchive? apps;
    public string AudioWarning=>apps?.Warning??"";
    Process? process;
    AudioFeed? audio;
    readonly TaskCompletionSource firstFrame=new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly StringBuilder errors=new();
    public string? Failure=>audio?.Completion.IsFaulted==true ? "Audio capture failed. "+audio.Completion.Exception!.GetBaseException().Message : process?.HasExited==true ? ErrorText : null;
    string ErrorText{get{lock(errors)return errors.Length==0?"Capture ended unexpectedly. Check the recording source.":errors.ToString().Trim();}}
    public Capture(Target source,Profile profile,bool system,bool mic,bool replay,int minutes,string label,bool separateApps=false)
    {
        Source=source;this.profile=profile;this.system=system;this.mic=mic;this.separateApps=system && separateApps;Replay=replay;Minutes=minutes;Label=label;
        Folder=Home.Temp(replay?"ring":"take");
    }
    static Task<bool>? gpu;
    static async Task<bool> TestGpu()
    {
        try{await Proc.Run("ffmpeg",["-v","error","-f","lavfi","-i","color=size=640x360","-frames:v","1","-c:v","h264_nvenc","-f","null","-"]);return true;}
        catch(IOException ex){Home.Log("NVENC: "+ex.Message);return false;}
    }
    public const int Segment=2;
    public async Task Start()
    {
        try{
            if(Home.Free<200_000_000)throw new IOException("Free up at least 200 MB to start recording.");
            if(separateApps){
                if(!OperatingSystem.IsWindowsVersionAtLeast(10,0,20348))throw new IOException("Separate app tracks require Windows 10 build 20348 or later. Turn off Separate app tracks to record a system mix.");
                string preferred=Label;
                if(Source.IsWindow){Native.GetWindowThreadProcessId(Source.Handle,out int pid);preferred=Path.GetFileNameWithoutExtension(Native.ImagePath(pid))??Label;}
                apps=AppAudioArchive.Create(Folder,mic,preferred);
            }
            bool nvenc=await (gpu??=TestGpu());
            List<string> args=["-hide_banner","-loglevel","warning","-nostats","-stats_period","0.25","-progress","pipe:1","-y"];
            if(system||mic){
                audio=new AudioFeed(system,mic,apps);await audio.Ready.WaitAsync(TimeSpan.FromSeconds(15));
                args.AddRange(["-f","s16le","-ar","48000","-ac","4","-probesize","32","-analyzeduration","0","-i",audio.PathName]);
            }
            var full=Source.Bounds;var area=Source.Region??full;
            double scale=Math.Min(1,Math.Min((double)profile.Width/area.Width,(double)profile.Height/area.Height));
            int w=Math.Max(2,(int)(area.Width*scale)/2*2),h=Math.Max(2,(int)(area.Height*scale)/2*2);
            nvenc=nvenc && w>=160 && h>=96;
            string src=Source.IsWindow?$"hwnd={(long)Source.Handle}":$"hmonitor={(long)Source.Handle}";
            if(Source.Region!=null)src+=$":crop_left={area.Left-full.Left}:crop_top={area.Top-full.Top}:crop_right={full.Right-area.Right}:crop_bottom={full.Bottom-area.Bottom}";
            string graph=$"gfxcapture={src}:width={w}:height={h}:resize_mode=scale_aspect:dup_frames=1:max_framerate={profile.Fps}";
            if(!nvenc)graph+=",hwdownload,format=bgra,scale=in_range=pc:out_range=tv:out_color_matrix=bt601,format=yuv420p";
            // NVENC converts BGRA to limited-range YUV with the 601 matrix. The
            // incoming WGC frame carries full-range RGB tags, which would otherwise survive encoding.
            graph+=","+Media.DesktopYuv;
            graph+="[video]";
            bool mix=system && !separateApps;
            if(mix&&mic)graph+=";[0:a]asplit=2[s][m];[s]pan=stereo|c0=c0|c1=c1[sys];[m]pan=stereo|c0=c2|c1=c3[mic]";
            else if(mix)graph+=";[0:a]pan=stereo|c0=c0|c1=c1[sys]";
            else if(mic)graph+=";[0:a]pan=stereo|c0=c2|c1=c3[mic]";
            else if(separateApps)graph+=";[0:a]pan=stereo|c0=c0|c1=c1[clock]";
            args.AddRange(["-filter_complex",graph,"-map","[video]"]);
            if(mix)args.AddRange(["-map","[sys]"]);if(mic)args.AddRange(["-map","[mic]"]);
            if(separateApps&&!mic)args.AddRange(["-map","[clock]"]);
            args.AddRange(["-c:v",nvenc?"h264_nvenc":"libx264","-preset",nvenc?"p3":"veryfast","-profile:v","high",
                "-b:v",profile.Mbps+"M","-maxrate",profile.Mbps+"M","-bufsize",profile.Mbps*2+"M",
                "-bf","0","-g",(profile.Fps*Segment).ToString(),"-r",profile.Fps.ToString(),"-fps_mode","cfr","-c:a","aac","-b:a","160k"]);
            if(mix)args.AddRange(["-metadata:s:a:0","title=System","-metadata:s:a:0","handler_name=System"]);
            if(mic){int i=mix?1:0;args.AddRange([$"-metadata:s:a:{i}","title=Microphone",$"-metadata:s:a:{i}","handler_name=Microphone"]);}
            if(Replay)args.AddRange(["-force_key_frames","expr:gte(t,n_forced*2)","-f","segment","-segment_time","2","-reset_timestamps","1",Path.Combine(Folder,"%08d.mkv")]);
            else args.Add(Path.Combine(Folder,"take.mkv"));
            var si=Proc.Info("ffmpeg",args);si.RedirectStandardInput=true;
            process=new Process{StartInfo=si,EnableRaisingEvents=true};
            process.ErrorDataReceived+=(_,e)=>{if(e.Data!=null){lock(errors){errors.AppendLine(e.Data);if(errors.Length>3000)errors.Remove(0,errors.Length-3000);}}};
            process.OutputDataReceived+=(_,e)=>{if(e.Data?.StartsWith("frame=")==true && int.TryParse(e.Data.AsSpan(6),out int n)&&n>0)firstFrame.TrySetResult();};
            process.Exited+=(_,_)=>firstFrame.TrySetException(new IOException("Could not start video capture. "+ErrorText));
            process.Start();ChildJob.Track(process);process.BeginErrorReadLine();process.BeginOutputReadLine();
            await firstFrame.Task.WaitAsync(TimeSpan.FromSeconds(14));
            Clock.Start();Home.Log($"Capture started: {(Replay?"replay":"recording")}, {w}x{h}, {profile.Fps} FPS, {(nvenc?"NVENC":"x264")}");
        }catch{await EndProcess();Home.Clear(Folder);throw;}
    }
    string[] Segments()=>Directory.GetFiles(Folder,"*.mkv").Order(StringComparer.Ordinal).ToArray();
    public void Prune()
    {
        if(!Replay)return;
        var files=Segments();
        int remove=Math.Max(0,files.Length-Minutes*60/Segment-2);
        foreach(var f in files.Take(remove))File.Delete(f);
        if(remove>0)apps?.PruneBefore(int.Parse(Path.GetFileNameWithoutExtension(files[remove])));
    }
    public async Task<string> SaveReplay()=>(await SaveReplayDetails()).File;
    public sealed record ReplayResult(string File,double Start,double Duration);
    public async Task<ReplayResult> SaveReplayDetails(string? title=null)
    {
        if(!Replay)throw new InvalidOperationException();
        var files=Segments().TakeLast(Minutes*60/Segment+1).ToArray();
        if(files.Length==0)throw new IOException("The replay buffer is empty.");
        string active=files[^1];
        double tail=process==null || process.HasExited ? Segment :
            Math.Clamp((DateTime.UtcNow-File.GetCreationTimeUtc(active)).TotalSeconds,0,Segment);
        for(int i=0;i<50 && process?.HasExited==false && Segments().LastOrDefault()==active;i++)await Task.Delay(100);
        if(process?.HasExited==false && Segments().LastOrDefault()==active){files=files[..^1];tail=Segment;}
        if(files.Length==0)throw new IOException("The first segment is still recording.");
        double end=(files.Length-1)*Segment+tail, length=Math.Min(end,Minutes*60);
        if(length<.05)throw new IOException("The replay buffer is filling up.");
        if(Home.Free<files.Sum(f=>new FileInfo(f).Length)+96_000_000)throw new IOException("There is not enough disk space to save the replay.");
        string dest=Home.Unique(Home.Videos,Label+" · "+(title??"replay"),"mp4");
        string tmp=Path.Combine(Folder,"save.mp4"),list=Path.Combine(Folder,"save.ffconcat");
        // All list entries are private, generated numeric basenames.
        File.WriteAllLines(list,files.Select(f=>"file '"+Path.GetFileName(f)+"'"+Environment.NewLine+"duration "+Segment),new UTF8Encoding(false));
        try{
            string[] video=["-f","concat","-safe","1","-ss",Home.N(end-length),"-i",list];
            if(apps==null)await Media.Remux(video,tmp,duration:length);
            else await apps.Mux(video,tmp,int.Parse(Path.GetFileNameWithoutExtension(files[0]))*Segment+end-length,length);
            File.Move(tmp,dest);return new(dest,int.Parse(Path.GetFileNameWithoutExtension(files[0]))*Segment+end-length,length);
        }
        finally{File.Delete(tmp);File.Delete(list);}
    }
    async Task EndProcess()
    {
        if(process!=null){
            try{
                if(!process.HasExited){await process.StandardInput.WriteLineAsync("q");await process.StandardInput.FlushAsync();
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));}
            }catch(Exception ex) when(ex is TimeoutException or IOException or InvalidOperationException){try{if(!process.HasExited)process.Kill(true);}catch(InvalidOperationException){}}
            await process.WaitForExitAsync();process.WaitForExit();process.Dispose();process=null;
        }
        if(audio!=null){var a=audio;audio=null;await Task.Run(a.Dispose);}
    }
    public async Task<string?> Stop(bool preserveReplay=false)
    {
        await EndProcess();Clock.Stop();
        string raw=Path.Combine(Folder,"take.mkv");
        if(Replay || !File.Exists(raw)||new FileInfo(raw).Length==0){
            if(Replay && preserveReplay)File.WriteAllText(Path.Combine(Folder,"keep-replay.txt"),"An automatic replay could not be saved. These source segments are retained for recovery.");
            else Home.Clear(Folder);return null;
        }
        string dest=Home.Unique(Home.Videos,Label,"mp4");
        string tmp=Path.Combine(Folder,"final.mp4");
        try{
            if(apps==null)await Media.Remux(["-i",raw],tmp);
            else await apps.Mux(["-i",raw],tmp,0,(await Media.Probe(raw)).Duration);
            File.Move(tmp,dest);
        }catch(Exception ex){
            if(apps!=null){File.Delete(tmp);throw new IOException("Recording and app tracks kept for recovery in "+Folder+". "+ex.Message,ex);}
            Home.Log("Saved as MKV: "+ex.Message);dest=Path.ChangeExtension(dest,"mkv");File.Move(raw,dest);
        }
        Home.Clear(Folder);return dest;
    }
}
internal static class ChildJob
{
    [DllImport("kernel32.dll")]static extern nint CreateJobObject(nint attrs,string? name);
    [DllImport("kernel32.dll")]static extern bool SetInformationJobObject(nint h,int cls,nint info,uint size);
    [DllImport("kernel32.dll")]static extern bool AssignProcessToJobObject(nint job,nint process);
    static readonly nint job=Create();
    static nint Create()
    {
        var h=CreateJobObject(0,null);
        // JOBOBJECT_EXTENDED_LIMIT_INFORMATION, BasicLimitInformation.LimitFlags at byte 16.
        int size=IntPtr.Size==8?144:112;var p=Marshal.AllocHGlobal(size);
        try{Marshal.Copy(new byte[size],0,p,size);Marshal.WriteInt32(p,16,0x2000);SetInformationJobObject(h,9,p,(uint)size);}finally{Marshal.FreeHGlobal(p);}
        return h;
    }
    public static void Track(Process p){try{AssignProcessToJobObject(job,p.Handle);}catch(InvalidOperationException){}}
}

