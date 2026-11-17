using Moments;
using System.Text.Json;

static class Tests
{
    static int count;
    static void Check(bool value,string name){if(!value)throw new Exception(name);count++;}
    static Dictionary<string,object> Event(int id,string name,double time,string killer="Player#EUW",string victim="Enemy#EUW",int streak=0,object? stolen=null,string[]? assists=null)=>new(){
        ["EventID"]=id,["EventName"]=name,["EventTime"]=time,["KillerName"]=killer,["VictimName"]=victim,["KillStreak"]=streak,["Stolen"]=stolen??false,["Assisters"]=assists??[]};
    static string Data(double time,IEnumerable<Dictionary<string,object>> events,bool ambiguous=false)=>JsonSerializer.Serialize(new{
        gameData=new{gameTime=time},activePlayer=new{riotId="Player#EUW",riotIdGameName="Player",summonerName="LegacyName"},
        allPlayers=new[]{new{riotId="Player#EUW",riotIdGameName="Player",summonerName="LegacyName"},new{riotId="Enemy#EUW",riotIdGameName=ambiguous?"Player":"Enemy",summonerName="OtherName"}},
        events=new{Events=events}});
    static ReplayEvent Mark(string id,double time,double? detected=null)=>new(id,"Kill",time,time,detected??time);
    static void ParserAndTracker(){
        var input=new[]{Event(1,"ChampionKill",10),Event(2,"ChampionKill",11,"LegacyName"),Event(3,"ChampionKill",12,"Enemy#EUW","Player#EUW"),
            Event(4,"ChampionKill",13,"Ally#EUW",assists:["Player"]),Event(5,"ChampionKill",14,"Enemy#EUW"),Event(6,"Multikill",15,streak:5),
            Event(7,"DragonKill",16,stolen:"True"),Event(8,"BaronKill",17,stolen:true),Event(9,"HeraldKill",18,stolen:false),
            Event(10,"DragonKill",19,"Ally#EUW",assists:["Player#EUW"]),Event(11,"BaronKill",20,"Enemy#EUW",stolen:true),Event(12,"Unsupported",21)};
        var snapshot=LolEvents.Parse(Data(22,input));
        Check(snapshot.Events.Select(e=>e.Kind).SequenceEqual(new[]{LolEventKind.Kill,LolEventKind.Kill,LolEventKind.Death,LolEventKind.Assist,LolEventKind.None,LolEventKind.Pentakill,LolEventKind.DragonSteal,LolEventKind.BaronSteal,LolEventKind.Herald,LolEventKind.Dragon,LolEventKind.None,LolEventKind.None}),"event classification / player filtering / boolean compatibility");
        var ambiguous=LolEvents.Parse(Data(23,[Event(20,"ChampionKill",22,"Player")],true));
        Check(ambiguous.Events.Single().Kind==LolEventKind.None,"ambiguous untagged name must not identify us");
        foreach(int streak in new[]{2,3,4,5})Check(LolEvents.Parse(Data(30,[Event(25,"Multikill",29,streak:streak)])).Events.Single().Kind!=LolEventKind.None,"multikill "+streak);
        var tracker=new LolEventTracker();
        Check(tracker.Update(snapshot,(LolEventKind)8191).Length==0,"first connection replays history");
        var fresh=input.Append(Event(13,"ChampionKill",23)).Append(Event(14,"ChampionKill",24,"Enemy#EUW","Player#EUW")).ToArray();
        Check(tracker.Update(LolEvents.Parse(Data(25,fresh)),LolEventKind.Kill).Length==1,"only selected new events fire");
        Check(tracker.Update(LolEvents.Parse(Data(26,fresh)),LolEventKind.Death).Length==0,"filter change fires old events");
        var reconnect=fresh.Append(Event(15,"ChampionKill",28)).ToArray();
        Check(tracker.Update(LolEvents.Parse(Data(50,reconnect)),LolEventKind.Kill).Length==1,"reconnection loses new events");
        Check(tracker.Update(LolEvents.Parse(Data(2,[Event(1,"GameStart",0)])),LolEventKind.Kill).Length==0 && tracker.NewMatch,"new match reset");
        Check(tracker.Update(LolEvents.Parse(Data(11,[Event(1,"ChampionKill",10)])),LolEventKind.Kill).Length==1,"reused event ID in new match");
        tracker.Reset();Check(tracker.Update(LolEvents.Parse(Data(12,[Event(1,"ChampionKill",10)])),LolEventKind.Kill).Length==0,"reenable must baseline history");
        var missing=Data(2,[]).Replace("Player#EUW","",StringComparison.Ordinal).Replace("LegacyName","",StringComparison.Ordinal);
        bool rejected=false;try{LolEvents.Parse(missing);}catch(InvalidDataException){rejected=true;}Check(rejected,"missing identity accepted");
        var options=new LolAutoSaveOptions{Enabled=false,Events=LolEventKind.Death|LolEventKind.Assist};
        var restored=JsonSerializer.Deserialize<LolAutoSaveOptions>(JsonSerializer.Serialize(options))!;
        Check(!restored.Enabled && restored.Events==options.Events,"event settings roundtrip");
    }
    static void MatchWindows(){
        // Kills at 60 and 70 share footage, the later fight at 180 is separate.
        var reel=new MatchHighlights("test.mp4",0,[Mark("1",60),Mark("2",70),Mark("2",70),Mark("3",180)]);
        int[] segments=reel.SelectedSegments();
        Check(segments.SequenceEqual(Enumerable.Range(15,28).Concat(Enumerable.Range(75,23))),"overlap/gap or keyframe rounding");
        var chapters=reel.Chapters(segments,segments.Length*2);
        Check(chapters.Select(e=>e.Seconds).SequenceEqual(new double[]{30,40,86}),"chapter offsets or duplicate marker");
        var clipped=reel with{FirstSegment=26,EndSegment=40};
        Check(clipped.SelectedSegments().SequenceEqual(Enumerable.Range(26,14)),"match bounds");
        Check(clipped.Chapters(clipped.SelectedSegments(),12).Single().GameTime==60,"partial final segment");
        Check(LolEvents.Parse(Data(101,[Event(9,"GameEnd",100)])).GameOver,"end of match ignored");
        Check(!LolEvents.Parse(Data(101,[])).GameOver,"ordinary snapshot closed the match");
    }
    static void MetadataAndLayout(){
        var tags=LibraryLayout.EventTags(new ClipEvents("League of Legends",[new("My kills",0,0),new("My kills",1,1),new("Double kill",2,2),new("Triple kill",3,3),new("",4,4)]));
        Check(tags.Select(t=>t.Label).SequenceEqual(new[]{"Kill","Double kill","Triple kill"}) && tags[0].Count==2,"event badges/counts or legacy labels");
        Check(LibraryLayout.EventTags(null).Length==0 && LibraryLayout.EventTags(new ClipEvents("LoL",[])).Length==0,"ordinary recording received invented event tags");
        string folder=Path.Combine(Path.GetTempPath(),"moments_autosave_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try{
            string file=Path.Combine(folder,"clip.mp4");
            ClipEvents.Write(file,100,300,[Mark("old",99),Mark("start",100),Mark("middle",250),Mark("end",400)]);
            var data=ClipEvents.Read(file)!;Check(data.Events.Select(e=>e.Seconds).SequenceEqual(new[]{0d,150d}),"markers outside actual saved range or wrong offset");
            File.WriteAllText(file+".events.json","broken");Check(ClipEvents.Read(file)==null,"corrupt optional metadata breaks library");
        }finally{Directory.Delete(folder,true);}
        foreach(double width in new[]{500d,600,800,1200,1700}){
            double tile=LibraryLayout.TileWidth(width);int columns=Math.Max(1,(int)(width/270));
            Check(tile>0 && columns*(tile+10)<=width+.01,"tile rows overflow at "+width);
        }
    }
    public static void Main(){ParserAndTracker();MatchWindows();MetadataAndLayout();Console.WriteLine($"PASS: {count} LoL parser, identity, deduplication, settings, match windows, partial tails, compressed chapters, metadata and tile-layout assertions.");}
}
