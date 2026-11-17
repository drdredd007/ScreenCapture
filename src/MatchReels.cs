using System.Text;
using System.Text.Json;

namespace Moments;

// The ring contains both the rolling manual buffer and the keyframe-aligned
// segments reserved for match highlights. Only the small edit list is journaled.
internal sealed class MatchReels(string folder)
{
    const string Journal="match-highlights.json";
    readonly List<MatchHighlights> matches=[];
    MatchHighlights? active;
    int firstSegment;
    bool ended,dirty;
    public int EventCount=>active?.Events.Length??0;
    public bool Pending=>matches.Count>0;
    public IReadOnlySet<int> Reserved {get;private set;}=new HashSet<int>();
    public static bool HasJournal(string folder)=>File.Exists(Path.Combine(folder,Journal));

    public void Observe(IEnumerable<ReplayEvent> incoming,bool newMatch,bool gameOver,double matchStart,double now){
        if(newMatch){
            int boundary=Math.Max(0,(int)Math.Floor(matchStart/Capture.Segment));
            if(active!=null)Replace(active with{EndSegment=boundary});
            active=null;firstSegment=boundary;ended=false;
        }
        if(!ended){
            var batch=incoming.ToArray();
            if(batch.Length>0){
                if(active==null){
                    string name=Path.GetFileName(Home.Unique(Home.Videos,"LoL · Match highlights","mp4"));
                    active=new(name,firstSegment,[]);matches.Add(active);
                }
                Replace(active with{Events=active.Events.Concat(batch).DistinctBy(e=>e.Id).ToArray()});
            }
            if(gameOver){
                if(active!=null)Replace(active with{EndSegment=(int)Math.Ceiling((now+MatchHighlights.After)/Capture.Segment)});
                active=null;ended=true;
            }
        }
        Checkpoint();
    }

    void Replace(MatchHighlights next){
        matches[matches.IndexOf(active!)]=next;active=next;dirty=true;
    }

    public void Checkpoint(){
        if(!dirty)return;
        Reserved=matches.SelectMany(match=>match.SelectedSegments()).ToHashSet();
        string path=Path.Combine(folder,Journal);
        if(matches.Count==0)File.Delete(path);
        else{
            File.WriteAllText(path+".tmp",JsonSerializer.Serialize(matches));
            File.Move(path+".tmp",path,true);
        }
        dirty=false;
    }

    public static async Task Recover(string folder){
        var book=new MatchReels(folder);
        var pending=JsonSerializer.Deserialize<MatchHighlights[]>(File.ReadAllText(Path.Combine(folder,Journal)))
            ??throw new InvalidDataException("Unreadable highlight journal.");
        book.matches.AddRange(pending);
        var apps=Directory.Exists(AppAudioArchive.DirectoryFor(folder))?AppAudioArchive.Reopen(folder):null;
        await book.Publish(apps,true);
    }

    public async Task<string?> Publish(AppAudioArchive? apps,bool stopped){
        Checkpoint();
        var ready=matches.Where(m=>stopped || m.EndSegment.HasValue).ToArray();
        if(ready.Length==0)return null;
        int[] available=Directory.EnumerateFiles(folder,"*.mkv")
            .Select(path=>Path.GetFileNameWithoutExtension(path))
            .Where(name=>int.TryParse(name,out _)).Select(int.Parse).Order().ToArray();
        if(available.Length==0)throw new IOException("No recording segments available for the match highlights.");
        string? last=null;
        foreach(var match in ready){
            int[] wanted=match.SelectedSegments();
            if(wanted.Length==0)throw new InvalidDataException("Match has no selected video segments.");
            if(!stopped && available[^1]<=wanted[^1])continue; // The last segment is still being written.
            if(Path.GetFileName(match.Output)!=match.Output || !match.Output.EndsWith(".mp4",StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Invalid match output name.");
            string output=Path.Combine(Home.Videos,match.Output);
            if(!File.Exists(output) || !File.Exists(output+".events.json"))
                await Render(match,wanted,available,apps,output);
            matches.Remove(match);dirty=true;Checkpoint();last=output;
        }
        return last;
    }

    async Task Render(MatchHighlights match,int[] wanted,int[] available,AppAudioArchive? apps,string output){
        int[] selected=wanted.Where(n=>n>=available[0] && n<=available[^1]).ToArray();
        if(selected.Length==0 || selected.Except(available).Any())throw new IOException("Some highlight segments are unavailable.");
        string SegmentName(int number)=>number.ToString("D8")+".mkv";
        var tail=await Media.Probe(Path.Combine(folder,SegmentName(selected[^1])));
        double duration=(selected.Length-1)*Capture.Segment+Math.Min(Capture.Segment,tail.Duration);
        var chapters=match.Chapters(selected,duration);
        if(chapters.Length==0)throw new IOException("No event is inside the available recording.");
        string manifest=Path.Combine(folder,"highlights.ffconcat"),temp=Path.Combine(folder,"highlights.mp4");
        try{
            File.WriteAllLines(manifest,selected.Select(n=>$"file '{SegmentName(n)}'"+Environment.NewLine+"duration "+Capture.Segment),new UTF8Encoding(false));
            string[] input=["-f","concat","-safe","1","-i",manifest];
            if(apps==null)await Media.Remux(input,temp,duration:duration);
            else await apps.Mux(input,temp,0,duration,selected);
            // Publish metadata first. The library sees the MP4 only once both files are ready.
            new ClipEvents("League of Legends",chapters).Save(output);
            File.Move(temp,output,true);
        }finally{File.Delete(temp);File.Delete(manifest);}
    }
}
