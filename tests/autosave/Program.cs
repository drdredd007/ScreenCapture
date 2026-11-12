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
    static void Scheduling(){
        foreach(int minutes in new[]{5,10}){
            var schedule=new AutoReplay();Check(!schedule.Due(1000,minutes),"empty schedule");
            var first=Mark("1",100);schedule.Add(first);Check(!schedule.Due(129.9,minutes) && schedule.Due(130,minutes),"30 second delay");
            schedule.Add(Mark("2",120));Check(!schedule.Due(149.9,minutes) && schedule.Due(150,minutes),"new event must reset delay");
            var batch=schedule.Snapshot();schedule.Add(Mark("3",151));schedule.Saved(batch);
            Check(schedule.Pending && !schedule.Due(180.9,minutes) && schedule.Due(181,minutes),"save completion discarded event received during export");
            schedule.Failed(181);Check(!schedule.Due(185.9,minutes) && schedule.Due(186,minutes),"retry after export failure");
            schedule.Saved(schedule.Snapshot());Check(!schedule.Pending,"successful save left pending events");
            schedule.Add(first);schedule.Add(first);Check(schedule.Snapshot().Length==1,"duplicate schedule event");
            double deadline=100+minutes*60-30;
            for(double at=120;at<deadline;at+=20)schedule.Add(Mark(at.ToString(),at));
            Check(schedule.DueAt(minutes)==deadline && schedule.Due(deadline,minutes),"long fight must save before oldest event leaves buffer");
            schedule.Reset();Check(!schedule.Pending,"disable/capture change reset");
        }
        var delayed=new AutoReplay();delayed.Add(Mark("late",100,140));Check(delayed.DueAt(5)==170,"delay must start on detection");
    }
    static void MetadataAndLayout(){
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
    public static void Main(){ParserAndTracker();Scheduling();MetadataAndLayout();Console.WriteLine($"PASS: {count} LoL parser, identity, deduplication, settings, delay, long-fight, export-race, retry, metadata and tile-layout assertions.");}
}
