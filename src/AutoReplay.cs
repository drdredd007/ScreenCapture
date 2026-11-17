using System.Text.Json;
namespace Moments;

internal sealed record ReplayEvent(string Id,string Label,double GameTime,double RecordingTime,double DetectedAt);
// Segment numbers are on the capture clock. A sorted set makes both
// overlapping event windows and repeated API snapshots safe to combine.
internal sealed record MatchHighlights(string Output, int FirstSegment, ReplayEvent[] Events, int? EndSegment=null)
{
    internal const int SegmentSeconds=2, Before=30, After=15;

    public int[] SelectedSegments(){
        var selected=new SortedSet<int>();
        foreach(var item in Events){
            int first=Math.Max(FirstSegment,(int)Math.Floor((item.RecordingTime-Before)/SegmentSeconds));
            int end=Math.Min(EndSegment??int.MaxValue,(int)Math.Ceiling((item.RecordingTime+After)/SegmentSeconds));
            for(int n=first;n<end;n++)selected.Add(n);
        }
        return selected.ToArray();
    }

    public ClipEvent[] Chapters(int[] segments,double duration){
        var result=new List<ClipEvent>();
        foreach(var item in Events.DistinctBy(e=>e.Id).OrderBy(e=>e.RecordingTime)){
            int segment=(int)Math.Floor(item.RecordingTime/SegmentSeconds);
            int index=Array.BinarySearch(segments,segment);
            if(index<0)continue;
            double offset=index*SegmentSeconds+(item.RecordingTime-segment*SegmentSeconds);
            if(offset<duration)result.Add(new(item.Label,offset,item.GameTime));
        }
        return result.ToArray();
    }
}
internal sealed record ClipEvent(string Label,double Seconds,double GameTime);
internal sealed record ClipEvents(string Game,ClipEvent[] Events)
{
    public static ClipEvents? Read(string video){
        try{return JsonSerializer.Deserialize<ClipEvents>(File.ReadAllText(video+".events.json"));}
        catch(Exception ex) when(ex is IOException or JsonException or UnauthorizedAccessException){return null;}
    }
    public static void Write(string video,double start,double duration,IEnumerable<ReplayEvent> events){
        var markers=events.Where(e=>e.RecordingTime>=start && e.RecordingTime<start+duration)
            .Select(e=>new ClipEvent(e.Label,e.RecordingTime-start,e.GameTime)).OrderBy(e=>e.Seconds).ToArray();
        if(markers.Length==0)return;
        new ClipEvents("League of Legends",markers).Save(video);
    }
    public void Save(string video){
        string path=video+".events.json";
        File.WriteAllText(path+".tmp",JsonSerializer.Serialize(this,new JsonSerializerOptions{WriteIndented=true}));
        File.Move(path+".tmp",path,true);
    }
}
