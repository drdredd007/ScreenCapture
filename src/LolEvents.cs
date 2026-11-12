using System.Text.Json;
using System.Text.Json.Serialization;
namespace Moments;

[Flags]
[JsonConverter(typeof(JsonStringEnumConverter<LolEventKind>))]
internal enum LolEventKind
{
    None=0,Kill=1,Death=2,Assist=4,DoubleKill=8,TripleKill=16,QuadraKill=32,Pentakill=64,
    Dragon=128,Baron=256,Herald=512,DragonSteal=1024,BaronSteal=2048,HeraldSteal=4096
}
internal sealed class LolAutoSaveOptions
{
    public bool Enabled {get;set;}=true;
    public LolEventKind Events {get;set;}=LolEventKind.Kill|LolEventKind.DoubleKill|LolEventKind.TripleKill|LolEventKind.QuadraKill|LolEventKind.Pentakill|LolEventKind.DragonSteal|LolEventKind.BaronSteal|LolEventKind.HeraldSteal;
    public static readonly (LolEventKind Kind,string Label)[] Choices=[
        (LolEventKind.Kill,"My kills"),(LolEventKind.Death,"My deaths"),(LolEventKind.Assist,"My assists"),
        (LolEventKind.DoubleKill,"Double kill"),(LolEventKind.TripleKill,"Triple kill"),(LolEventKind.QuadraKill,"Quadra kill"),(LolEventKind.Pentakill,"Pentakill"),
        (LolEventKind.Dragon,"Dragon takedown"),(LolEventKind.Baron,"Baron takedown"),(LolEventKind.Herald,"Herald takedown"),
        (LolEventKind.DragonSteal,"My dragon steals"),(LolEventKind.BaronSteal,"My Baron steals"),(LolEventKind.HeraldSteal,"My Herald steals")];
    public static string Label(LolEventKind kind)=>Choices.FirstOrDefault(c=>c.Kind==kind).Label??"LoL event";
}
internal sealed record LolEvent(string Id,double GameTime,LolEventKind Kind,string Label);
internal sealed record LolSnapshot(double GameTime,string Player,LolEvent[] Events);
internal static class LolEvents
{
    static string Text(JsonElement e,string key)=>e.ValueKind==JsonValueKind.Object && e.TryGetProperty(key,out var v) && v.ValueKind==JsonValueKind.String ? v.GetString()??"" : "";
    static IEnumerable<string> Names(JsonElement e)=>new[]{Text(e,"riotId"),Text(e,"summonerName"),Text(e,"riotIdGameName")}.Where(s=>s.Length>0);
    static bool Number(JsonElement e,string key,out double value){value=0;return e.ValueKind==JsonValueKind.Object && e.TryGetProperty(key,out var v) && v.ValueKind==JsonValueKind.Number && v.TryGetDouble(out value) && double.IsFinite(value);}
    public static LolSnapshot Parse(string json)
    {
        using var doc=JsonDocument.Parse(json);var root=doc.RootElement;
        if(root.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("Invalid LoL response.");
        if(!root.TryGetProperty("gameData",out var game) || !Number(game,"gameTime",out double time) || time<0 ||
            !root.TryGetProperty("activePlayer",out var active) || !root.TryGetProperty("allPlayers",out var players) || players.ValueKind!=JsonValueKind.Array)
            throw new InvalidDataException("Waiting for an active LoL player.");
        var aliases=Names(active).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string player=Text(active,"riotId");if(player.Length==0)player=Text(active,"summonerName");
        if(player.Length==0)throw new InvalidDataException("LoL did not identify the active player.");
        var list=players.EnumerateArray().ToArray();
        // Prefer a full Riot ID; legacy name matching is accepted only when unique.
        var own=list.Where(p=>Names(p).Contains(player,StringComparer.OrdinalIgnoreCase)).ToArray();
        if(own.Length!=1)throw new InvalidDataException("Waiting for an unambiguous LoL player identity.");
        aliases.UnionWith(Names(own[0]));
        aliases.RemoveWhere(name=>list.Count(p=>Names(p).Contains(name,StringComparer.OrdinalIgnoreCase))>1);
        bool IsMe(string name)=>name.Length>0 && aliases.Contains(name);
        List<LolEvent> result=[];
        if(root.TryGetProperty("events",out var events) && events.ValueKind==JsonValueKind.Object && events.TryGetProperty("Events",out var array) && array.ValueKind==JsonValueKind.Array)
        foreach(var e in array.EnumerateArray()){
            if(!Number(e,"EventTime",out double at) || at<0 || at>time+2 || !e.TryGetProperty("EventID",out var id))continue;
            var name=Text(e,"EventName");bool killer=IsMe(Text(e,"KillerName"));
            bool assist=e.TryGetProperty("Assisters",out var helpers) && helpers.ValueKind==JsonValueKind.Array && helpers.EnumerateArray().Any(p=>p.ValueKind==JsonValueKind.String && IsMe(p.GetString()??""));
            bool stolen=e.TryGetProperty("Stolen",out var theft) && (theft.ValueKind==JsonValueKind.True || theft.ValueKind==JsonValueKind.String && bool.TryParse(theft.GetString(),out bool yes) && yes);
            LolEventKind kind=name switch{
                "ChampionKill" when killer=>LolEventKind.Kill,
                "ChampionKill" when IsMe(Text(e,"VictimName"))=>LolEventKind.Death,
                "ChampionKill" when assist=>LolEventKind.Assist,
                "Multikill" when killer && Number(e,"KillStreak",out double streak)=>streak switch{2=>LolEventKind.DoubleKill,3=>LolEventKind.TripleKill,4=>LolEventKind.QuadraKill,5=>LolEventKind.Pentakill,_=>LolEventKind.None},
                "DragonKill" when killer||assist=>stolen && killer?LolEventKind.DragonSteal:LolEventKind.Dragon,
                "BaronKill" when killer||assist=>stolen && killer?LolEventKind.BaronSteal:LolEventKind.Baron,
                "HeraldKill" when killer||assist=>stolen && killer?LolEventKind.HeraldSteal:LolEventKind.Herald,
                _=>LolEventKind.None};
            // Keep unselected/enemy events in the stream so changing filters cannot replay history.
            result.Add(new(id.GetRawText()+":"+name+":"+at.ToString("R",System.Globalization.CultureInfo.InvariantCulture),at,kind,LolAutoSaveOptions.Label(kind)));
        }
        return new(time,player,result.ToArray());
    }
}
internal sealed class LolEventTracker
{
    readonly HashSet<string> seen=[];
    string? player;double lastTime;int generation;
    public bool NewMatch {get;private set;}
    public void Reset(){seen.Clear();player=null;lastTime=0;}
    public LolEvent[] Update(LolSnapshot snapshot,LolEventKind selected)
    {
        NewMatch=player!=null && (snapshot.GameTime<lastTime-2 || !string.Equals(player,snapshot.Player,StringComparison.OrdinalIgnoreCase));
        bool baseline=player==null || NewMatch;
        if(baseline){seen.Clear();generation++;}
        player=snapshot.Player;lastTime=snapshot.GameTime;
        var added=snapshot.Events.Where(e=>seen.Add(e.Id)).ToArray();
        return baseline?[]:added.Where(e=>(selected&e.Kind)!=0).OrderBy(e=>e.GameTime).Select(e=>e with{Id=generation+":"+e.Id}).ToArray();
    }
}
