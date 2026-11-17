using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Moments;
internal static class MatchReelChecks
{
    static void Check(bool ok,string why){if(!ok)throw new Exception(why);}

    public static async Task Run(Action<string> note){
        var capture=new Capture(new Target(0,"fixture",new System.Drawing.Rectangle(0,0,64,64)),Profile.All[0],true,false,true,5,"fixture");
        string ring=capture.Folder,assets=Home.Temp("match-test");
        string? result=null;
        try{
            var events=new[]{new ReplayEvent("a","My kills",70,1,1),new ReplayEvent("b","Double kill",72,3,3),new ReplayEvent("c","Triple kill",429,360,360)};
            capture.Highlights.Observe(events,false,false,0,360);
            capture.Highlights.Observe(events,false,false,0,360);
            foreach(string color in new[]{"red","blue"})
                await Proc.Run("ffmpeg",["-v","error","-y","-f","lavfi","-i",$"color={color}:s=64x64:r=10","-t","2","-c:v","libx264","-bf","0",Path.Combine(assets,color+".mkv")]);
            for(int n=0;n<=182;n++)
                File.Copy(Path.Combine(assets,(n<165?"red":"blue")+".mkv"),Path.Combine(ring,n.ToString("D8")+".mkv"));
            var audio=AppAudioArchive.Create(ring,false,"Game");
            var tone=Enumerable.Range(0,960).Select(i=>(int)(6000*Math.Sin((i/2)*2*Math.PI*440/48000))).ToArray();
            audio.Write("Game.exe",0,tone);audio.Write("Chat.exe",180L*96000,tone);audio.Flush(true);
            capture.Prune();
            Check(File.Exists(Path.Combine(ring,"00000000.mkv")) && !File.Exists(Path.Combine(ring,"00000010.mkv")),"pruning lost a pinned segment or retained dead footage");
            string journal=Path.Combine(ring,"match-highlights.json");
            string snapshot=File.ReadAllText(journal);
            var existing=Directory.GetFiles(Home.Videos,"*.mp4").ToHashSet();
            await MatchReels.Recover(ring);
            result=Directory.GetFiles(Home.Videos,"*.mp4").Single(f=>!existing.Contains(f));
            var chapters=ClipEvents.Read(result)!.Events;
            Check(chapters.Select(c=>c.Seconds).SequenceEqual(new double[]{1,3,48}),"compressed video chapters");
            var info=await Media.Probe(result);
            Check(Math.Abs(info.Duration-54)<.12 && info.Tracks.Select(t=>t.Name).SequenceEqual(new[]{"Game","Chat"}),"wrong duration or separated tracks");
            foreach(var (time,channel) in new[]{(3,0),(48,2)}){
                string raw=Path.Combine(assets,"pixel.raw");
                await Proc.Run("ffmpeg",["-v","error","-y","-ss",time.ToString(),"-i",result,"-map","0:v:0","-frames:v","1","-vf","scale=1:1,format=rgb24","-f","rawvideo",raw]);
                Check(File.ReadAllBytes(raw)[channel]>200,"chapter did not seek to the expected fight");
            }
            // Crash after the rename but before the journal was retired.
            File.WriteAllText(journal,snapshot);await MatchReels.Recover(ring);
            Check(!File.Exists(journal) && Directory.GetFiles(Home.Videos,"*.mp4").Count(f=>!existing.Contains(f))==1,"retry created a second reel");
            var successive=new MatchReels(ring);
            successive.Observe([events[0]],false,true,0,4);
            successive.Observe([],true,false,320,321);
            successive.Observe([events[0] with{RecordingTime=330}],false,false,320,331);
            var first=await successive.Publish(null,false);
            var second=await successive.Publish(null,true);
            Check(first!=null && second!=null && first!=second && ClipEvents.Read(first)!.Events.Single().Seconds==1 &&
                ClipEvents.Read(second)!.Events.Single().Seconds==10,"consecutive matches shared a reel or chapter");
            foreach(var file in new[]{first!,second!}){File.Delete(file);File.Delete(file+".events.json");}
            note("Match reel: overlapping highlights, ring pruning, retained audio tracks, decoded chapter positions and idempotent recovery passed.");
        }finally{
            Home.Clear(ring);Home.Clear(assets);
            if(result!=null){File.Delete(result);File.Delete(result+".events.json");}
        }
    }
}
