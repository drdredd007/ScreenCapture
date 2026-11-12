using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
namespace Moments;
public partial class AssemblyWindow
{
    sealed class Prepared(ClipInfo info)
    {
        public ClipInfo Info {get;}=info;
        public Dictionary<int,(string File,double[] Peaks)> Audio {get;}=[];
    }
    sealed class Channel
    {
        public readonly MediaPlayer Player=new(){ScrubbingEnabled=true};
        public required string Uri;
        public int Offset;
        public TimeSpan Desired;
        public bool Open,Playing;
    }
    readonly Dictionary<string,Prepared> prepared=new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string,Task> warmJobs=new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string,string> failed=new(StringComparer.OrdinalIgnoreCase);
    readonly SemaphoreSlim warmSlots=new(2);
    readonly Dictionary<int,Channel> channels=[];
    readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(16)};
    readonly Stopwatch clock=new();
    bool playing,starting;
    int firstFrame,playRequest;
    int lastVideoOffset;

    void Warm(string path){
        if(!warmJobs.ContainsKey(path))warmJobs[path]=PrepareFile(path);
    }
    async Task PrepareFile(string path)
    {
        try{
            await warmSlots.WaitAsync(life.Token);
            try{
                var info=await Probe(path);string dir=Path.Combine(folder,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
                var cache=new Prepared(info);prepared[path]=cache;
                if(!closing)UpdatePreview(true);
                foreach(var track in info.Tracks){
                    cache.Audio[track.Index]=await Media.PreviewAudio(path,track,dir,info.Duration,life.Token);
                    if(!closing){DrawTimeline();UpdatePreview(true);}
                }
            }finally{warmSlots.Release();}
        }catch(OperationCanceledException){}catch(Exception ex){failed[path]=ex.Message;if(!closing){UpdatePreview(true);Say("Preview: "+ex.Message);}}
    }
    void Pause(){
        playRequest++;starting=false;playing=false;clock.Reset();timer.Stop();Monitor.PauseFrames();
        if(Play!=null)Play.Content="▶";foreach(var ch in channels.Values){ch.Playing=false;ch.Player.Pause();}
    }
    async void PlayClick(object s,RoutedEventArgs e){
        if(playing||starting){Pause();UpdatePreview(true);return;}if(edit.End==0)return;
        int ticket=++playRequest;starting=true;Play.Content="…";
        var files=edit.Clips.Select(c=>c.File).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        await Task.WhenAll(files.Select(p=>warmJobs[p]));
        if(closing||ticket!=playRequest)return;
        starting=false;
        var missing=files.FirstOrDefault(f=>failed.ContainsKey(f));
        if(missing!=null){Pause();Say("Preview: "+failed[missing]);return;}
        if(frame>=edit.End)frame=0;
        firstFrame=frame;playing=true;Play.Content="Ⅱ";clock.Restart();UpdatePreview(true);timer.Start();
    }
    void PlaybackTick(object? s,EventArgs e){
        if(!playing)return;frame=Math.Min(edit.End,firstFrame+(int)(clock.Elapsed.TotalSeconds*edit.Fps));
        if(frame>=edit.End)Pause();UpdatePreview(false);
        double x=frame*pixelsPerFrame;
        if(playing && (x>TrackScroll.HorizontalOffset+TrackScroll.ViewportWidth || x<TrackScroll.HorizontalOffset))TrackScroll.ScrollToHorizontalOffset(x);
    }
    void SetFrame(int value){
        Pause();frame=Math.Clamp(value,0,edit.End);UpdatePreview(true);
    }
    void BackFrame(object s,RoutedEventArgs e)=>SetFrame(frame-1);
    void ForwardFrame(object s,RoutedEventArgs e)=>SetFrame(frame+1);
    void UpdatePreview(bool force){
        if(!loaded||closing)return;
        FrameReadout.Text=edit.Timecode(frame)+"   /   "+edit.Timecode(edit.End);MovePlayhead();
        int at=Math.Min(frame,Math.Max(0,edit.End-1));
        var active=edit.Clips.Where(c=>c.Start<=at&&c.End>at).ToArray();
        var video=active.Where(c=>c.Lane>0).MaxBy(c=>c.Lane);
        var used=new HashSet<int>();
        bool haveVideo=false;
        if(video!=null && prepared.TryGetValue(video.File,out var source)){
            haveVideo=true;int offset=video.In-video.Start;
            Monitor.Display(video.File,source.Info,(double)(at+offset)/edit.Fps,playing,force || offset!=lastVideoOffset);
            lastVideoOffset=offset;
        }else _=Monitor.ClearVideo();
        foreach(var clip in active.Where(c=>c.Lane<0 && !edit.Muted.Contains(c.Lane))){
            if(prepared.TryGetValue(clip.File,out var cache) && cache.Audio.TryGetValue(clip.Stream,out var stem)){
                used.Add(clip.Lane);Sync(clip.Lane,clip,stem.File,at,force);
            }
        }
        foreach(var key in channels.Keys.Where(k=>!used.Contains(k)).ToArray())CloseChannel(key);
        MonitorHint.Text=edit.End==0?"Add your first clip to the timeline":
            video!=null && !haveVideo?failed.GetValueOrDefault(video.File,"Preparing preview…"):"";
    }
    void Sync(int key,TimelineClip clip,string file,int at,bool force)
    {
        var desired=TimeSpan.FromSeconds((double)(clip.In+at-clip.Start)/edit.Fps);
        int offset=clip.In-clip.Start;
        if(!channels.TryGetValue(key,out var channel) || !string.Equals(channel.Uri,file,StringComparison.OrdinalIgnoreCase)){
            CloseChannel(key);channel=new Channel{Uri=file,Desired=desired,Offset=offset,Playing=playing};
            channels[key]=channel;
            channel.Player.MediaOpened+=(_,_)=>{
                if(!channels.TryGetValue(key,out var current)||current!=channel)return;
                channel.Open=true;channel.Player.Position=channel.Desired;
                if(channel.Playing)channel.Player.Play();else channel.Player.Pause();
            };
            channel.Player.MediaFailed+=(_,e)=>{Home.Log("Montage audio preview: "+e.ErrorException);if(!closing && channels.GetValueOrDefault(key)==channel){Pause();Say("Windows preview: "+e.ErrorException.Message);}};
            channel.Player.Open(new Uri(file));return;
        }
        bool transportChanged=playing!=channel.Playing;
        channel.Desired=desired;channel.Playing=playing;
        if(channel.Open && (force || channel.Offset!=offset || (playing && Math.Abs((channel.Player.Position-desired).TotalSeconds)>.2)))
            channel.Player.Position=desired;
        if(channel.Open && transportChanged){if(playing)channel.Player.Play();else channel.Player.Pause();}
        channel.Offset=offset;
    }
    void CloseChannel(int key){if(channels.Remove(key,out var c))c.Player.Close();}
    void ClosePlayers(){foreach(int key in channels.Keys.ToArray())CloseChannel(key);}
}

