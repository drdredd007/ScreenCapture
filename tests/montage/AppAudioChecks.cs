using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Moments;
internal static class AppAudioChecks
{
    static void Require(bool value,string message){if(!value)throw new Exception(message);}
    static async Task Wait(Func<bool> done,string name,int ms=15000){
        long until=Environment.TickCount64+ms;while(!done()){if(Environment.TickCount64>until)throw new Exception("Timeout: "+name);await Task.Delay(50);}
    }
    static System.Collections.Generic.IEnumerable<CheckBox> Boxes(DependencyObject root){
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){
            var child=VisualTreeHelper.GetChild(root,i);if(child is CheckBox box)yield return box;
            foreach(var nested in Boxes(child))yield return nested;
        }
    }
    static async Task<string> ExportWithoutChat(string source){
        var w=new Editor(source){ShowActivated=false};w.Show();
        try{
            var go=(Button)w.FindName("ExportButton");await Wait(()=>go.IsEnabled,"editor audio preparation");
            var chat=Boxes(w).Single(b=>b.Content as string=="ToneChat");chat.IsChecked=false;chat.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
            var existing=Directory.GetFiles(Home.Exports).ToHashSet();go.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Wait(()=>Directory.GetFiles(Home.Exports).Any(f=>!existing.Contains(f)),"UI export",45000);
            return Directory.GetFiles(Home.Exports).Single(f=>!existing.Contains(f));
        }finally{w.Close();await Wait(()=>!w.IsVisible,"editor closed");}
    }
    static async Task ReplayWindowAndRecovery(string sample,Action<string> note){
        string folder=Home.Temp("audio-window-test");
        try{
            var archive=AppAudioArchive.Create(folder,false,"Game");
            int[] tone=new int[960];
            void Fill(string name,int chunk,int start,int end,int hz){
                for(int n=start;n<end;n+=480){
                    for(int i=0;i<480;i++)tone[i*2]=tone[i*2+1]=(int)(600*Math.Sin(2*Math.PI*(n+i)*hz/48000));
                    archive.Write(name,(long)chunk*96000+n,tone);
                }
            }
            Fill("Expired.exe",0,0,4800,250);
            // Seek 1.25 s into chunk 150. Earlier activity in that same chunk must be omitted.
            Fill("Expired.exe",150,0,4800,250);
            Fill("Game.exe",150,48000,96000,431);Fill("Game.exe",151,0,96000,431);
            Fill("Chat.exe",151,0,96000,991);
            archive.Flush(true);archive.PruneBefore(150);
            Require(!Directory.GetFiles(AppAudioArchive.DirectoryFor(folder),"00000000.wav",SearchOption.AllDirectories).Any(),"expired chunk still on disk");
            string output=Path.Combine(folder,"slice.mp4");await archive.Mux(["-i",sample],output,301.25,2.0);
            var info=await Media.Probe(output);Require(info.Tracks.Select(t=>t.Name).SequenceEqual(new[]{"Game","Chat"}) && Math.Abs(info.Duration-2)<.1,"trimmed replay names or duration");
            Require((await Levels(output,info.Tracks[1],.10)).Chat<2 && (await Levels(output,info.Tracks[1],1.1)).Chat>50,"partial-chunk audio alignment");
            note("Rolled replay: expired chunks pruned, app silent within requested slice omitted, late app aligned after exact subsegment seek.");
            var restored=AppAudioArchive.Reopen(folder);string fromDisk=Path.Combine(folder,"restored.mp4");
            await restored.Mux(["-i",sample],fromDisk,301.25,2);
            Require((await Media.Probe(fromDisk)).Tracks.Select(t=>t.Name).SequenceEqual(new[]{"Game","Chat"}),"recovery layout");
            note("On-disk app stems and track names can be remuxed after reopening the capture archive.");
        }finally{Home.Clear(folder);}
    }
    static async Task<(double Game,double Chat)> Levels(string file,Track track,double time){
        string pcm=Path.Combine(Home.Work,"meter.s16");
        await Proc.Run("ffmpeg",["-v","error","-y","-ss",Home.N(time),"-i",file,"-map","0:"+track.Index,"-t","0.5","-ar","8000","-ac","1","-f","s16le",pcm]);
        var bytes=File.ReadAllBytes(pcm);
        double Level(int hz){
            double x=0,y=0;int count=bytes.Length/2;Require(count>=3000,"short decoded audio block");
            for(int n=0;n<count;n++){short sample=BitConverter.ToInt16(bytes,n*2);double phase=2*Math.PI*n*hz/8000;x+=sample*Math.Cos(phase);y+=sample*Math.Sin(phase);}
            return 2*Math.Sqrt(x*x+y*y)/count;
        }
        return (Level(431),Level(991));
    }
    public static async Task Run(Action<string> note){
        var game=Process.GetProcessesByName("ToneGame").First(p=>string.Equals(p.MainModule?.FileName,Path.Combine(Home.Root,"ToneGame.exe"),StringComparison.OrdinalIgnoreCase));
        Target? window=null;for(int i=0;i<50 && window==null;i++){window=Native.Windows(game.Id).FirstOrDefault();await Task.Delay(80);}
        Require(window!=null,"Fixture window missing");var join=Path.Combine(Home.Root,"join.test");var leave=Path.Combine(Home.Root,"leave.test");
        foreach(bool replay in new[]{false,true}){
            File.Delete(join);File.Delete(leave);
            if(replay)File.WriteAllText(Path.Combine(Home.Root,"siblings.test"),"");
            var capture=new Capture(window!,Profile.All[0],true,replay,replay,5,"QA sound",true);
            string? result=null;
            try{
                await capture.Start();await Task.Delay(1700);File.WriteAllText(join,"");
                await Task.Delay(4300);if(replay)result=await capture.SaveReplay();
                File.WriteAllText(leave,"");await Task.Delay(1800);
            }finally{var normal=await capture.Stop();result??=normal;}
            Require(result!=null,"Missing saved video");var info=await Media.Probe(result!);
            note($"{(replay?"Replay, independent processes":"Recording, nested processes")} {info.Duration:F2}s. Tracks: "+string.Join(", ",info.Tracks.Select(t=>t.Name)));
            var gt=info.Tracks.Single(t=>t.Name=="ToneGame");var ct=info.Tracks.Single(t=>t.Name=="ToneChat");
            Require(info.Tracks.All(t=>t.Name!="System"),"duplicate desktop mix");Require(info.Tracks.Count(t=>t.Name=="Microphone")==(replay?1:0),"microphone layout");
            var g=await Levels(result!,gt,4);var c=await Levels(result!,ct,4);var early=await Levels(result!,ct,.6);
            note($"Simultaneous tones: game stem 431={g.Game:F2}, 991={g.Chat:F2}; chat stem 431={c.Game:F2}, 991={c.Chat:F2}; chat before launch {early.Chat:F2}.");
            Require(g.Game>15 && c.Chat>15 && g.Chat<Math.Max(2,c.Chat*(replay?.012:.04)) && c.Game<Math.Max(2,g.Game*.012) && early.Chat<2,"process isolation/timing failed");
            if(!replay){
                var late=await Levels(result!,ct,info.Duration-.8);Require(late.Chat<2,"chat continued after exit");
            }
            var export=await ExportWithoutChat(result!);var tracks=(await Media.Probe(export)).Tracks;
            Require(tracks.All(t=>t.Name!="ToneChat"),"muted stream in export");
            var kept=tracks.Single(t=>t.Name=="ToneGame");var muted=await Levels(export,kept,4);
            Require(muted.Game>15 && muted.Chat<Math.Max(2,c.Chat*(replay?.012:.04)),"muted export leaked chat");note("Clip Editor: chat unchecked; exported game stream and intended tone verified.");
            if(replay){
                var seq=new Sequence().Add(result!,info with{Tracks=[gt,ct]},1,0);
                int seam=seq.End;seq=seq.Add(result!,info with{Tracks=[ct,gt]},1,seam);
                Require(seq.At(-1,seam)!.AudioName=="ToneGame" && seq.At(-2,seam)!.AudioName=="ToneChat","app lanes changed with stream order");
                seq=seq with{Clips=seq.Clips.Select(c=>c with{Start=c.Start==0?0:seq.Fps,In=4*seq.Fps,Length=seq.Fps}).ToArray(),Muted=[-2]};
                string video=await SequenceRender.Export(seq,"mp4",(_,_)=>{},CancellationToken.None);var mix=(await Media.Probe(video)).Tracks.Single();
                foreach(double at in new[]{.2,1.2}){var levels=await Levels(video,mix,at);Require(levels.Game>15 && levels.Chat<Math.Max(2,c.Chat*.012),"muted app lane leaked in project mix");}
                note("Video Editor: app names keep their lanes across reordered inputs; muting the chat lane leaves the game throughout the stereo export.");
                await ReplayWindowAndRecovery(result!,note);
            }
        }
        game.Dispose();
    }
}
