using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
namespace Moments;
internal static class Verification
{
    static readonly string report=Path.Combine(Home.Root,"build","verification.txt");
    static void Pass(string s)=>File.AppendAllText(report,s+Environment.NewLine);
    static void Check(bool ok,string error){if(!ok)throw new InvalidOperationException(error);}
    internal static async Task Run()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(report)!);File.WriteAllText(report,"");
        string folder=Home.Temp("verify");
        var canvas=new Border{Background=Brushes.Teal,Child=new TextBlock{Text="MOMENTS   /   CAPTURE TEST",Foreground=Brushes.White,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,FontSize=19}};
        var window=new Window{Title="Moments · Test Source",Width=540,Height=330,Content=canvas,WindowStartupLocation=WindowStartupLocation.CenterScreen,ShowInTaskbar=false,Topmost=true};
        var tick=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(180)};bool color=false;tick.Tick+=(_,_)=>{canvas.Background=(color=!color)?Brushes.DarkSlateBlue:Brushes.Teal;};
        var generated=new List<string>();Capture? active=null;
        try{
            window.Show();tick.Start();await Task.Delay(450);
            nint h=new WindowInteropHelper(window).Handle;
            var pt=window.PointToScreen(new Point(0,0));var br=window.PointToScreen(new Point(window.ActualWidth-20,window.ActualHeight-40));
            var target=new Target(h,"fixture",new(0,0,(int)(br.X-pt.X),(int)(br.Y-pt.Y)),true);
            var monitor=Native.Monitors().First(t=>t.Bounds.Contains((int)pt.X+60,(int)pt.Y+60));
            var region=monitor with{Region=new((int)pt.X+60,(int)pt.Y+60,200,120)};
            foreach(var (label,src,audio) in new[]{("window",target,true),("region",region,false),("screen",monitor,false)}){
                active=new Capture(src,Profile.All[0],audio,audio,false,5,"QA_"+label);
                await active.Start();await Task.Delay(2300);string f=(await active.Stop())!;active=null;generated.Add(f);
                var probe=await Media.Probe(f);Check(probe.Duration>=1.8 && probe.Width>0,label+" duration");
                if(label=="region")Check(probe.Width==200&&probe.Height==120,"region geometry");
                if(audio)Check(probe.Tracks.Select(t=>t.Name).SequenceEqual(new[]{"System","Microphone"}),"WASAPI track names: "+string.Join(",",probe.Tracks));
                if(label!="screen"){
                    string pixel=Path.Combine(folder,"pixel.rgb");
                    await Proc.Run("ffmpeg",["-v","error","-y","-ss","1","-i",f,"-vf","scale=1:1,format=rgb24","-frames:v","1","-f","rawvideo",pixel]);
                    var rgb=File.ReadAllBytes(pixel);Check(rgb.Length==3 && rgb[2]>50 && rgb[2]>rgb[0]+12,label+" lost fixture picture");
                }
                Pass($"{label}: {probe.Width}x{probe.Height} / {probe.Duration:F3}s / {string.Join(", ",probe.Tracks.Select(t=>t.Name))}");
            }
            active=new Capture(target,Profile.All[0],true,true,true,5,"QA_replay");
            await active.Start();await Task.Delay(4800);string saved=await active.SaveReplay();generated.Add(saved);
            var replay=await Media.Probe(saved);Check(replay.Duration>=4 && replay.Tracks.Count==2,"live replay");
            await Task.Delay(1200);string again=await active.SaveReplay();generated.Add(again);var second=await Media.Probe(again);Check(second.Duration>replay.Duration+.5,"replay interrupted after save");
            await active.Stop();active=null;Pass($"live repeat saves: {replay.Duration:F3}s, {second.Duration:F3}s / 2 tracks");
            window.Hide();tick.Stop();
            string fixture=Path.Combine(folder,"source.mp4");
            await Proc.Run("ffmpeg",["-v","error","-y","-f","lavfi","-i","testsrc2=size=320x180:rate=30","-f","lavfi","-i","sine=frequency=440","-f","lavfi","-i","sine=frequency=990","-t","8","-map","0:v","-map","1:a","-map","2:a","-c:v","libx264","-preset","ultrafast","-c:a","aac","-metadata:s:a:0","handler_name=System","-metadata:s:a:1","handler_name=Microphone",fixture]);
            var original=await Media.Probe(fixture);var kept=Media.Kept(8,[new(1,2.5),new(2,3),new(5,6)]);
            Check(kept.SequenceEqual(new[]{new Interval(0,1),new Interval(3,5),new Interval(6,8)}),"overlapping cuts");
            for(int i=0;i<3;i++){
                string ext=new[]{"mp4","mkv","webm"}[i];var tracks=i==0?original.Tracks.Skip(1).ToList():i==1?original.Tracks:[];
                string output=await Media.Export(fixture,"QA_edit",ext,kept,tracks,(_,_)=>{},CancellationToken.None);generated.Add(output);
                var result=await Media.Probe(output);Check(Math.Abs(result.Duration-5)<.18 && result.Tracks.Select(t=>t.Name).SequenceEqual(tracks.Select(t=>t.Name)),"Edited "+ext);
                await Proc.Run("ffmpeg",["-v","error","-i",output,"-map","0:v","-map","0:a?","-f","null","-"]);
                if(i==0){
                    string pcm=Path.Combine(folder,"tone.s16");
                    await Proc.Run("ffmpeg",["-v","error","-ss","0.25","-i",output,"-map","0:a:0","-t","0.5","-ar","16000","-ac","1","-f","s16le",pcm]);
                    var data=File.ReadAllBytes(pcm);int crossings=0;
                    for(int n=2;n<data.Length;n+=2)if(BitConverter.ToInt16(data,n-2)<0 && BitConverter.ToInt16(data,n)>=0)crossings++;
                    Check(Math.Abs(crossings*2-990)<12,"wrong sound in exported microphone track");
                }
                Pass($"export {ext}: {result.Duration:F3}s, {result.Tracks.Count} tracks, decoded");
            }
            string part=Path.Combine(folder,"2s.mkv");
            await Proc.Run("ffmpeg",["-v","error","-f","lavfi","-i","color=size=96x64:rate=10","-t","2","-c:v","libx264","-preset","ultrafast",part]);
            foreach(int minutes in new[]{5,10}){
                active=new Capture(target,Profile.All[0],false,false,true,minutes,"QA_retention");
                for(int i=0;i<minutes*30+9;i++)File.Copy(part,Path.Combine(active.Folder,$"{i:D8}.mkv"));
                active.Prune();Check(Directory.GetFiles(active.Folder,"*.mkv").Length==minutes*30+2,"prune");
                string output=await active.SaveReplay();generated.Add(output);var r=await Media.Probe(output);
                Check(Math.Abs(r.Duration-minutes*60)<.1,"bounded replay length");Pass($"replay after turnover, seeded media: {r.Duration:F2}s");
                await active.Stop();active=null;
            }
            Pass("PASS");
        }catch(Exception ex){Pass("FAIL "+ex);throw;}
        finally{if(active!=null)try{var f=await active.Stop();if(f!=null)generated.Add(f);}catch{}
            tick.Stop();window.Close();foreach(var f in generated)File.Delete(f);Home.Clear(folder);}
    }
}

