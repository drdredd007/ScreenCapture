using System.Text.Json;
namespace Moments;

internal sealed record ReplayEvent(string Id,string Label,double GameTime,double RecordingTime,double DetectedAt);
internal sealed class AutoReplay
{
    public const double Delay=30;
    readonly List<ReplayEvent> pending=[];
    double retryAt;
    public bool Pending=>pending.Count>0;
    public void Reset(){pending.Clear();retryAt=0;}
    public void Add(ReplayEvent item){if(!pending.Any(e=>e.Id==item.Id))pending.Add(item);}
    public double DueAt(int minutes)=>!Pending?double.PositiveInfinity:Math.Max(retryAt,Math.Min(
        pending.Max(e=>e.DetectedAt)+Delay,
        // Leave 30 seconds for segment finalization/export before the first event expires.
        pending.Min(e=>e.RecordingTime)+minutes*60-30));
    // Scheduler and recording clocks both use elapsed seconds for this capture.
    public bool Due(double now,int minutes)=>Pending && now>=DueAt(minutes);
    public ReplayEvent[] Snapshot()=>pending.ToArray();
    public void Saved(IEnumerable<ReplayEvent> batch){var ids=batch.Select(e=>e.Id).ToHashSet();pending.RemoveAll(e=>ids.Contains(e.Id));retryAt=0;}
    public void Failed(double now){retryAt=now+5;}
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
        string path=video+".events.json";
        File.WriteAllText(path+".tmp",JsonSerializer.Serialize(new ClipEvents("League of Legends",markers),new JsonSerializerOptions{WriteIndented=true}));
        File.Move(path+".tmp",path,true);
    }
}
