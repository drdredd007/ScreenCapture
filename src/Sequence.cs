using System.Text.Json;
using System.Text.Json.Serialization;
namespace Moments;

internal sealed record TimelineClip(Guid Id, Guid? Link, string File, int Lane, int Stream,
    int Start, int In, int Length, int SourceLength,string? AudioName=null)
{
    [JsonIgnore] public int End => checked(Start+Length);
    public TimelineClip Slice(int from,int to) => this with {
        Id=from==Start?Id:Guid.NewGuid(), Start=from, In=In+from-Start, Length=to-from
    };
}
internal enum ClipEdit { Move, TrimLeft, TrimRight, Delete }

internal sealed record Sequence
{
    public int Version {get;init;}=1;
    public int Fps {get;init;}=30;
    public int Width {get;init;}=1920;
    public int Height {get;init;}=1080;
    public int VideoLanes {get;init;}=2;
    public int AudioLanes {get;init;}=2;
    public string Title {get;init;}="My sequence";
    public string[] Imports {get;init;}=[];
    public int[] Muted {get;init;}=[];
    public TimelineClip[] Clips {get;init;}=[];
    [JsonIgnore] public int End => Clips.Length==0?0:Clips.Max(c=>c.End);
    public IEnumerable<int> Lanes()=>Enumerable.Range(1,VideoLanes).Reverse().Concat(Enumerable.Range(1,AudioLanes).Select(n=>-n));
    public TimelineClip? At(int lane,int frame)=>Clips.FirstOrDefault(c=>c.Lane==lane && c.Start<=frame && frame<c.End);
    public static string LaneName(int lane)=>lane>0?"V"+lane:"A"+(-lane);
    public int Frame(double seconds)=>checked((int)Math.Round(seconds*Fps,MidpointRounding.AwayFromZero));
    public string Timecode(int frame){
        var time=TimeSpan.FromSeconds(frame/Fps);
        return $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}:{frame%Fps:00}";
    }
    public Sequence Add(string path,ClipInfo media,int lane,int position)
    {
        if(lane==0 || lane>VideoLanes || -lane>AudioLanes)throw new ArgumentException("Choose an existing track.");
        var sequence=this;
        if(Clips.Length==0){
            var fps=double.IsFinite(media.Fps)&&media.Fps>0?Math.Clamp((int)Math.Round(media.Fps),1,120):30;
            sequence=this with {Fps=fps,Width=Math.Max(2,media.Width/2*2),Height=Math.Max(2,media.Height/2*2)};
            position=sequence.Frame((double)position/Fps);
        }
        int length=Math.Max(1,sequence.Frame(media.Duration));
        int FreeStart(int track,int start){
            foreach(var c in Clips.Where(c=>c.Lane==track).OrderBy(c=>c.Start)){
                if(c.End<=start)continue;
                if((long)start+length<=c.Start)break;
                start=c.End;
            }
            return start;
        }
        position=FreeStart(lane,Math.Max(0,position));
        var add=new List<TimelineClip>();Guid link=Guid.NewGuid();int audioCount=AudioLanes;
        void AddStream(int target,int stream,string? name=null)=>add.Add(new(Guid.NewGuid(),link,path,target,stream,position,0,length,length,name));
        if(lane>0)AddStream(lane,-1);
        if(lane<0 && media.Tracks.Count==0)throw new InvalidOperationException("This file has no audio tracks.");
        int audioLane=lane>0?-1:lane;
        foreach(var t in media.Tracks){
            bool Free(int n)=>add.All(c=>c.Lane!=n) && FreeStart(n,position)==position;
            if(lane>0){
                var lanes=Enumerable.Range(1,audioCount).Select(n=>-n).Where(Free).ToArray();
                // Prefer a track already dedicated to this app, even when another
                // recording contains a different set or order of audio streams.
                int matching=lanes.FirstOrDefault(n=>Clips.Any(c=>c.Lane==n && c.AudioName==t.Name) &&
                    Clips.All(c=>c.Lane!=n || c.AudioName==t.Name));
                int empty=lanes.FirstOrDefault(n=>Clips.All(c=>c.Lane!=n || c.AudioName==null));
                audioLane=matching!=0?matching:empty!=0?empty:-(audioCount+1);
            }else while(!Free(audioLane))audioLane--;
            AddStream(audioLane,t.Index,t.Name);audioCount=Math.Max(audioCount,-audioLane);audioLane--;
        }
        if(add.Count==1)add[0]=add[0] with {Link=null};
        return sequence with {AudioLanes=audioCount,Clips=[..Clips,..add]};
    }

    public Sequence Split(int lane,int frame)
    {
        var c=At(lane,frame);
        if(c==null || c.Start==frame)return this;
        return this with {Clips=[..Clips.Where(x=>x.Id!=c.Id),c.Slice(c.Start,frame),c.Slice(frame,c.End)]};
    }
    public Sequence Unlink(Guid id)
    {
        var link=Clips.FirstOrDefault(c=>c.Id==id)?.Link;
        return link.HasValue ? this with {Clips=Clips.Select(c=>c.Link==link?c with {Link=null}:c).ToArray()} : this;
    }

    public Sequence Edit(Guid id,ClipEdit edit,int delta=0,int? destination=null)
        => Place(id,edit,delta,destination,0,out _);

    // Compare distances before rounding the mouse position to a frame. An edit can
    // only land on an integer frame, but its magnetic radius is a screen distance.
    static IEnumerable<(int Delta,int Target)> NearbyEdges(IEnumerable<TimelineClip> clips,int[] edges,
        double desired,double radius,int lane)
    {
        var candidates=from c in clips
            from target in new[]{c.Start,c.End}
            from edge in edges
            let delta=target-edge
            let distance=Math.Abs(delta-desired)
            where distance<=radius
            orderby distance, c.Lane==lane descending, target
            select (Delta:delta,Target:target);
        return candidates.DistinctBy(p=>p.Delta);
    }

    internal int DropStart(int lane,double position,int length,double radius,out int? snap)
    {
        snap=null;
        if(radius>0 && length>0){
            foreach(var p in NearbyEdges(Clips,[0,length],position,radius,lane)){
                int start=p.Delta;
                if(start<0 || Clips.Any(c=>c.Lane==lane && c.Start<(long)start+length && c.End>start))continue;
                snap=p.Target;return start;
            }
        }
        return Math.Max(0,(int)Math.Round(position));
    }

    internal Sequence Place(Guid id,ClipEdit edit,double desired,int? destination,double radius,out int? snap)
    {
        snap=null;
        var focus=Clips.First(c=>c.Id==id);int lane=destination??focus.Lane;
        if(edit!=ClipEdit.Delete && desired==0 && lane==focus.Lane)return this;
        if(Math.Sign(lane)!=Math.Sign(focus.Lane))throw new InvalidOperationException("Move the clip to a track of the same type.");
        List<TimelineClip> stationary=[], moving=[];
        // A single-lane split leaves its peers intact. Isolate their matching source
        // interval only when this particular piece is subsequently edited.
        foreach(var c in Clips){
            bool linked=c.Id==id || (focus.Link.HasValue && c.Link==focus.Link);
            int from=Math.Max(c.Start,focus.Start),to=Math.Min(c.End,focus.End);
            if(!linked || from>=to){stationary.Add(c);continue;}
            if(c.Start<from)stationary.Add(c.Slice(c.Start,from));
            moving.Add(c.Slice(from,to));
            if(c.End>to)stationary.Add(c.Slice(to,c.End));
        }
        if(edit==ClipEdit.Delete)return this with {Clips=stationary.ToArray()};
        if(radius>0){
            int[] edges=edit switch{ClipEdit.TrimLeft=>[focus.Start],ClipEdit.TrimRight=>[focus.End],_=>[focus.Start,focus.End]};
            foreach(var p in NearbyEdges(stationary,edges,desired,radius,lane)){
                Sequence candidate;
                try{candidate=ApplyEdit(focus,edit,p.Delta,lane,stationary,moving);}
                catch(InvalidOperationException){continue;} // A closer edge can be blocked by a linked track.
                var placed=candidate.Clips.First(c=>c.Id==id);
                int actual=edit==ClipEdit.TrimRight?placed.End-focus.End:placed.Start-focus.Start;
                if(actual!=p.Delta)continue; // The source limit prevented reaching this edge.
                snap=p.Target;return candidate;
            }
        }
        return ApplyEdit(focus,edit,(int)Math.Round(desired),lane,stationary,moving);
    }

    Sequence ApplyEdit(TimelineClip focus,ClipEdit edit,int delta,int lane,List<TimelineClip> stationary,List<TimelineClip> moving)
    {
        if(edit==ClipEdit.TrimLeft)
            delta=Math.Clamp(delta,moving.Where(c=>c.Start==focus.Start).Max(c=>-Math.Min(c.Start,c.In)),focus.Length-1);
        if(edit==ClipEdit.TrimRight)
            delta=Math.Clamp(delta,1-focus.Length,moving.Where(c=>c.End==focus.End).Min(c=>c.SourceLength-c.In-c.Length));
        if(edit==ClipEdit.Move)delta=Math.Max(delta,-focus.Start);
        if(delta==0 && lane==focus.Lane)return this;
        Guid? group=moving.Count>1?Guid.NewGuid():null;
        var result=new List<TimelineClip>(stationary);
        foreach(var c in moving){
            TimelineClip changed=c;
            switch(edit){
                case ClipEdit.Move:
                    int target=Math.Sign(c.Lane)==Math.Sign(focus.Lane)?c.Lane+lane-focus.Lane:c.Lane;
                    if(target==0 || Math.Sign(target)!=Math.Sign(c.Lane) || target>VideoLanes || -target>AudioLanes)
                        throw new InvalidOperationException("There are not enough tracks in that direction for the linked audio.");
                    changed=c with {Start=checked(c.Start+delta),Lane=target};
                    break;
                case ClipEdit.TrimLeft:
                    int left=c.Start==focus.Start?focus.Start+delta:Math.Max(c.Start,focus.Start+delta);
                    if(left>=c.End)continue;
                    changed=c with {Start=left,In=c.In+left-c.Start,Length=c.End-left};break;
                case ClipEdit.TrimRight:
                    int right=c.End==focus.End?focus.End+delta:Math.Min(c.End,focus.End+delta);
                    if(right<=c.Start)continue;
                    changed=c with {Length=right-c.Start};break;
            }
            if(result.Any(other=>other.Lane==changed.Lane && other.End>changed.Start && other.Start<changed.End))
                throw new InvalidOperationException("That position is occupied. Choose a free spot or another track.");
            result.Add(changed with {Link=group});
        }
        return this with {Clips=result.ToArray()};
    }
    public void Validate()
    {
        if(Version!=1 || Width<2 || Height<2 || Fps is <1 or >120 || VideoLanes<1 || AudioLanes<1 ||
           Clips==null || Imports==null || Muted==null)throw new InvalidDataException("Invalid project format.");
        var ids=new HashSet<Guid>();
        foreach(var track in Clips.GroupBy(c=>c.Lane)){
            int end=0;
            foreach(var c in track.OrderBy(c=>c.Start)){
                if(c.Lane==0 || c.Lane>VideoLanes || -c.Lane>AudioLanes || !ids.Add(c.Id) ||
                   string.IsNullOrWhiteSpace(c.File) || c.Start<end || c.Length<=0 || c.In<0 ||
                   (long)c.In+c.Length>c.SourceLength || (c.Lane<0 && c.Stream<0))throw new InvalidDataException("The project contains an invalid clip.");
                end=c.End;
            }
        }
    }
    public static Sequence Load(string file){
        if(!System.IO.File.Exists(file))return new();
        var result=JsonSerializer.Deserialize<Sequence>(System.IO.File.ReadAllText(file))??throw new InvalidDataException("The project file is empty.");
        result.Validate();return result;
    }
    public void Save(string file){
        Validate();Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        string tmp=file+".tmp";System.IO.File.WriteAllText(tmp,JsonSerializer.Serialize(this));System.IO.File.Move(tmp,file,true);
    }
}
