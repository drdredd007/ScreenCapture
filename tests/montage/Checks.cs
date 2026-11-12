using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Drawing=System.Drawing;

namespace Moments;
public sealed class MontageChecks : Application
{
    static string report="";
    static void Note(string s){File.AppendAllText(report,s+Environment.NewLine);Console.WriteLine(s);}
    static void Check(bool b,string why){if(!b)throw new Exception(why);}
    [STAThread] public static int Main(){
        var app=new MontageChecks{ShutdownMode=ShutdownMode.OnExplicitShutdown};
        var source=System.Xml.Linq.XElement.Load(Path.Combine(AppContext.BaseDirectory,"StudioTheme.xaml"));
        var ns=source.Name.Namespace;
        var theme=new System.Xml.Linq.XElement(ns+"ResourceDictionary",source.Attributes().Where(a=>a.IsNamespaceDeclaration),source.Element(ns+"Application.Resources")!.Elements());
        app.Resources=(ResourceDictionary)System.Windows.Markup.XamlReader.Parse(theme.ToString());return app.Run();
    }
    protected override async void OnStartup(StartupEventArgs args){
        report=args.Args[0];File.WriteAllText(report,"");Home.Init();Theme.Install();
        try{
            if(args.Args.Length>1 && args.Args[1]=="editing"){await EditingChecks.Run(Note,Path.GetDirectoryName(report)!);Note("PASS");Shutdown(0);return;}
            if(args.Args.Length>1 && args.Args[1]=="cadence"){await VideoCadenceChecks.Run(Note);Note("PASS");Shutdown(0);return;}
            if(args.Args.Length>1 && args.Args[1]=="sync"){await AudioCompatibilityChecks.Run(Note);await AvSyncChecks.Run(Note);Note("PASS");Shutdown(0);return;}
            if(args.Args.Length>1 && args.Args[1]=="features"){await FeatureUiChecks.Run(Note,Path.GetDirectoryName(report)!);Note("PASS");Shutdown(0);return;}
            if(args.Args.Length>1 && args.Args[1]=="apps"){await AppAudioChecks.Run(Note);Note("PASS");Shutdown(0);return;}
            if(args.Args.Length>1 && args.Args[1]=="colors"){await DesktopColors();Note("PASS");Shutdown(0);return;}
            string a=Path.Combine(Home.Videos,"01 · Спектр.mp4"),b=Path.Combine(Home.Videos,"02 · Орбита.mp4");
            await Fixture(a,"testsrc2=size=640x360:rate=24,drawbox=x=300:y=155:w=40:h=50:color=red:t=fill",4,440,880);
            await Fixture(b,"mandelbrot=size=480x270:rate=30:maxiter=42,drawbox=x=220:y=110:w=40:h=50:color=blue:t=fill",2,660);
            var ai=await Media.Probe(a);var bi=await Media.Probe(b);
            await ModelAndRender(a,ai,b,bi);
            await Interface(a,b,Path.Combine(Path.GetDirectoryName(report)!,"montage-ui.png"));
            await DesktopColors();
            Note("PASS");Shutdown(0);
        }catch(Exception ex){Note("FAIL "+ex);Shutdown(1);}
    }
    static async Task Fixture(string path,string pattern,int seconds,params int[] frequencies){
        List<string> args=["-v","error","-y","-f","lavfi","-i",pattern];
        foreach(int hz in frequencies)args.AddRange(["-f","lavfi","-i",$"aevalsrc=0.13*sin(2*PI*{hz}*t)*(0.55+0.4*sin(2*PI*1.3*t)):s=48000"]);
        args.AddRange(["-map","0:v"]);for(int i=0;i<frequencies.Length;i++)args.AddRange(["-map",$"{i+1}:a",$"-metadata:s:a:{i}","handler_name="+(i==0?"System":"Microphone")]);
        args.AddRange(["-t",seconds.ToString(),"-c:v","libx264","-preset","ultrafast","-pix_fmt","yuv420p","-c:a","aac",path]);await Proc.Run("ffmpeg",args);
    }
    static void Snapping(string file){
        var group=Guid.NewGuid();
        var clip=new TimelineClip(Guid.NewGuid(),group,file,1,-1,20,15,20,140);
        var audio=clip with{Id=Guid.NewGuid(),Lane=-1,Stream=1};
        var edge=clip with{Id=Guid.NewGuid(),Link=null,Lane=2,Start=80,Length=40};
        var edit=new Sequence{Clips=[clip,audio,edge]};
        foreach(double scale in new[]{1d,4d}){
            var move=edit.Place(clip.Id,ClipEdit.Move,40+7/scale,1,8/scale,out var snap);
            Check(snap==80 && move.At(1,79)!.End==80 && move.At(-1,79)!.End==80,"trailing edge + linked audio snap");
            move=edit.Place(clip.Id,ClipEdit.Move,100+7/scale,1,8/scale,out snap);
            Check(snap==120 && move.At(1,120)!.Start==120,"leading edge to another track");
            move=edit.Place(clip.Id,ClipEdit.Move,100+9/scale,1,8/scale,out snap);
            Check(snap==null && move.Clips.First(c=>c.Id==clip.Id).Start>120,"outside screen-space radius");
        }
        var local=edit with{Clips=[clip,audio,edge with{Lane=1,Start=64},edge with{Id=Guid.NewGuid(),Start=65}]};
        var fit=local.Place(clip.Id,ClipEdit.Move,24.8,1,2,out var guide);
        Check(guide==64 && fit.At(1,63)!.End==64,"skip a closer snap which overlaps on the destination track");
        var trims=edit with{Clips=[clip,audio,edge with{Start=30,Length=20}]};
        foreach(var tool in new[]{ClipEdit.TrimLeft,ClipEdit.TrimRight}){
            var adjusted=trims.Place(clip.Id,tool,8.8,1,2,out guide);
            var piece=adjusted.Clips.First(c=>c.Id==clip.Id);
            Check(tool==ClipEdit.TrimLeft?guide==30 && piece.Start==30:guide==50 && piece.End==50,"trim snaps the dragged edge");
        }
        var loose=edit.Place(clip.Id,ClipEdit.Move,101,1,0,out guide);
        Check(guide==null && loose.At(1,121)!.Start==121,"snap bypass");
        var alone=edit with{Clips=[clip,audio]};
        var moved=alone.Place(clip.Id,ClipEdit.Move,1,1,8,out guide);
        Check(guide==null && moved.At(1,21)!.Start==21,"linked members must not attract themselves");
        Check(local.DropStart(1,45.3,20,2,out guide)==44 && guide==64,"library drop snaps trailing edge before collision handling");
        Note("Snapping: leading and trailing move edges, both trims, legal adjacent placement, linked audio, drop, bypass and zoom-scaled radius.");
    }
    static async Task ModelAndRender(string a,ClipInfo ai,string b,ClipInfo bi){
        Snapping(a);
        var baseEdit=new Sequence().Add(a,ai,1,0).Add(b,bi,2,24);
        int length=baseEdit.Clips.First(c=>c.Lane==1).Length;
        var cut=baseEdit.Split(1,48);Check(cut.Clips.Length==baseEdit.Clips.Length+1 && cut.At(-1,60)!.Start==0,"S split a second lane");
        var moved=cut.Edit(cut.At(1,60)!.Id,ClipEdit.Move,96);
        Check(moved.At(1,150)!.In==48 && moved.At(-1,150)!.In==48 && moved.At(-2,150)!.In==48 && moved.At(-1,0)!.End==48,"linked subrange move");
        var trim=baseEdit.Edit(baseEdit.At(1,2)!.Id,ClipEdit.TrimLeft,12);
        Check(trim.At(-1,12)!.In==12 && trim.At(-2,12)!.Start==12,"linked trim");
        var free=trim.Unlink(trim.At(1,12)!.Id).Edit(trim.At(1,12)!.Id,ClipEdit.Move,24);
        Check(free.At(1,36)!.In==12 && free.At(-1,12)!.Start==12 && free.At(-2,12)!.Length==length-12,"unlink");
        var gap=baseEdit.Add(b,bi,1,144);
        foreach(string ext in new[]{"mp4","mkv","webm"}){
            var source=ext=="webm"?gap with{Muted=gap.Lanes().Where(n=>n<0).ToArray()}:gap;
            string output=await SequenceRender.Export(source,ext,(_,_)=>{},CancellationToken.None);
            var probe=await Media.Probe(output);
            Check(Math.Abs(probe.Duration-(double)source.End/source.Fps)<.075 && probe.Width==640 && probe.Tracks.Count==(ext=="webm"?0:1),"duration/streams "+ext);
            foreach(var (t,channel) in new[]{(.25,0),(1.25,2),(3.5,0),(5.0,-1),(6.5,2)}){
                byte[] rgb=await Center(output,t);
                Check(channel<0?rgb.Max()<9:rgb[channel]>190&&rgb[(channel+1)%3]<45,"layer/gap "+ext+" at "+t+" "+string.Join(",",rgb));
            }
            if(ext=="mp4"){
                string raw=Path.Combine(Home.Work,"mix.s16");await Proc.Run("ffmpeg",["-v","error","-y","-ss","1.25","-i",output,"-t","0.25","-map","0:a:0","-ac","1","-ar","8000","-f","s16le",raw]);
                byte[] pcm=File.ReadAllBytes(raw);foreach(int f in new[]{440,660,880}){
                    double re=0,im=0;for(int i=0;i<pcm.Length/2;i++){var v=BitConverter.ToInt16(pcm,i*2);double phase=2*Math.PI*f*i/8000;re+=v*Math.Cos(phase);im+=v*Math.Sin(phase);}
                    Check(Math.Sqrt(re*re+im*im)/(pcm.Length/2)>150,"missing audio "+f);
                }
            }
            File.Delete(output);Note($"Timeline export {ext}, {probe.Duration:0.000}s, layer priority and black gap decoded.");
        }
        Note("Model: linked range move/trim after a single-lane split, explicit unlink, three mixed tones and silent export.");
    }
    static async Task<byte[]> Center(string path,double t){
        string raw=Path.Combine(Home.Work,"point.rgb");await Proc.Run("ffmpeg",["-v","error","-y","-ss",Home.N(t),"-i",path,"-vf","crop=4:4:(iw-4)/2:(ih-4)/2,scale=1:1,format=rgb24","-frames:v","1","-f","rawvideo",raw]);return File.ReadAllBytes(raw);
    }
    [StructLayout(LayoutKind.Sequential)] struct P{public int X,Y;}
    [DllImport("user32.dll",EntryPoint="GetPhysicalCursorPos")]static extern bool GetCursorPos(out P p);
    [DllImport("user32.dll",EntryPoint="SetPhysicalCursorPos")]static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll",EntryPoint="WindowFromPhysicalPoint")]static extern nint WindowFromPoint(P p);
    [DllImport("user32.dll")]static extern nint GetAncestor(nint h,uint flags);
    [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(nint h,out int pid);
    [DllImport("user32.dll")]static extern void mouse_event(uint flag,uint x,uint y,uint data,nuint extra);
    [DllImport("user32.dll")]static extern void keybd_event(byte key,byte scan,uint flags,nuint extra);
    [DllImport("user32.dll")]static extern nint GetForegroundWindow();
    [DllImport("user32.dll")]static extern bool PrintWindow(nint h,nint dc,uint flags);
    [DllImport("user32.dll")]static extern bool GetWindowRect(nint h,out Box box);
    [StructLayout(LayoutKind.Sequential)] struct Box{public int X,Y,R,B;}
    static Drawing.Bitmap Snapshot(Window w){
        var hwnd=new WindowInteropHelper(w).Handle;GetWindowRect(hwnd,out var r);
        var bmp=new Drawing.Bitmap(r.R-r.X,r.B-r.Y);using var g=Drawing.Graphics.FromImage(bmp);var h=g.GetHdc();
        try{Check(PrintWindow(hwnd,h,2),"PrintWindow");}finally{g.ReleaseHdc(h);}return bmp;
    }
    static void Key(Window win,byte vk){Check(GetForegroundWindow()==new WindowInteropHelper(win).Handle,"wrong foreground for key");keybd_event(vk,0,0,0);keybd_event(vk,0,2,0);}
    static async Task Wait(Func<bool> test,string why,int ms=10000){
        var until=DateTime.UtcNow.AddMilliseconds(ms);while(!test()){if(DateTime.UtcNow>until)throw new Exception("Timeout: "+why);await Task.Delay(40);}
    }
    static P Screen(FrameworkElement el,double x,double y){var pt=el.PointToScreen(new(x,y));return new P{X=(int)Math.Round(pt.X),Y=(int)Math.Round(pt.Y)};}
    static void Over(nint hwnd,P p){var hit=WindowFromPoint(p);GetWindowThreadProcessId(hit,out int pid);Check(GetAncestor(hit,2)==hwnd || pid==Environment.ProcessId,"mouse target outside test window");SetCursorPos(p.X,p.Y);}
    static async Task Drag(Window w,P start,P end){
        var h=new WindowInteropHelper(w).Handle;
        await Task.Run(()=>{Over(h,start);Thread.Sleep(70);mouse_event(2,0,0,0,0);
            try{Thread.Sleep(80);for(int i=1;i<=16;i++){var p=new P{X=start.X+(end.X-start.X)*i/16,Y=start.Y+(end.Y-start.Y)*i/16};Over(h,p);Thread.Sleep(25);}
                Over(h,end);Thread.Sleep(80);GetCursorPos(out var final);Check(final.X==end.X && final.Y==end.Y,"Desktop pointer moved during fixture drag");}
            finally{mouse_event(4,0,0,0,0);}
        });await Task.Delay(150);
    }
    static async Task ClickAt(Window w,P p){
        var h=new WindowInteropHelper(w).Handle;Over(h,p);await Task.Delay(50);mouse_event(2,0,0,0,0);mouse_event(4,0,0,0,0);await Task.Delay(120);
    }
    static T El<T>(FrameworkElement w,string id) where T:FrameworkElement=>(T)w.FindName(id);
    static void Button(Window w,string id)=>El<Button>(w,id).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
    static async Task Interface(string a,string b,string shot){
        string draft=Path.Combine(Home.Root,"Projects","current.json");Sequence State()=>Sequence.Load(draft);
        var w=new AssemblyWindow{Topmost=true};w.Show();w.Activate();GetCursorPos(out var prior);
        try{
            var bin=El<ListBox>(w,"Bin");await Wait(()=>bin.Items.Count==2,"media bin");
            await Task.Delay(500);bin.SelectedIndex=1; // select by the human-readable row, independent of file system dates
            ListBoxItem Row(string title){
                for(int i=0;i<bin.Items.Count;i++)if(bin.Items[i].ToString()!.Contains(title))return (ListBoxItem)bin.ItemContainerGenerator.ContainerFromIndex(i);
                throw new Exception("missing bin row "+title);
            }
            Row("Спектр").IsSelected=true;Button(w,"AppendButton");await Wait(()=>State().Clips.Length==3,"append");
            w.UpdateLayout();await Task.Delay(180);
            var canvas=El<Canvas>(w,"TimelineCanvas");
            P At(int lane,double f){
                var seq=State();double span=Math.Max(seq.Fps*3,seq.End+Math.Ceiling(seq.End*.09));
                return Screen(canvas,f*canvas.ActualWidth/span,26+43*(lane>0?seq.VideoLanes-lane:seq.VideoLanes-lane-1)+20);
            }
            P RulerAt(int f){var p=At(1,f);p.Y=Screen(canvas,0,10).Y;return p;}
            var clock=El<TextBlock>(w,"FrameReadout");
            bool HeadAt(int f)=>clock.Text.StartsWith(State().Timecode(f)+" ");
            var item=Row("Орбита");await Drag(w,Screen(item,30,item.ActualHeight/2),At(2,24));
            await Wait(()=>State().Clips.Length==5,"drag from bin");var baseline=State();Check(baseline.At(2,25)!=null,"drop lane");
            Check(HeadAt(0),"dropping a clip must preserve the playhead");
            await ClickAt(w,RulerAt(15));Check(HeadAt(15),"ruler click seeks");
            await Drag(w,RulerAt(15),RulerAt(40));Check(HeadAt(40),"ruler drag scrubs");
            await Drag(w,At(2,3),RulerAt(14));Check(HeadAt(40),"drag from an empty lane cannot become a scrub");
            var nearJoin=At(1,baseline.At(1,0)!.End+5);
            nearJoin.X-=Screen(canvas,7,0).X-Screen(canvas,0,0).X;
            await Drag(w,At(2,29),nearJoin);
            var joined=State();int seam=baseline.At(1,0)!.End;
            Check(joined.At(1,seam)?.File==b && joined.At(1,seam)!.Start==seam && joined.At(-3,seam)!.Start==seam,
                "dragging slightly inside the occupied lane snaps the video and linked audio exactly to the join");
            Check(HeadAt(40),"snapped move must preserve playhead");Button(w,"UndoButton");
            var h=new WindowInteropHelper(w).Handle;
            Over(h,At(1,12));await Task.Delay(70);Key(w,0x53);
            await Wait(()=>State().Clips.Length==6,"hover S");Check(State().At(1,12)!.Start==12 && State().At(-1,12)!.Start==0,"hover cut location");
            await ClickAt(w,At(-1,16));Check(HeadAt(40),"selecting a clip cannot seek");
            Over(h,Screen(bin,18,15));await Task.Delay(70);Key(w,0x53);
            await Wait(()=>State().Clips.Length==7,"playhead S");Check(State().At(-1,40)!.Start==40 && State().At(2,40)!.Start==24,"fallback S");
            Button(w,"UndoButton");Button(w,"UndoButton");Check(State().Clips.Length==5,"undo both cuts");
            int neighborEnd=baseline.At(2,25)!.End;
            var edge=At(1,baseline.At(1,1)!.End);edge.X-=2;var almost=At(1,neighborEnd);almost.X+=4;
            await Drag(w,edge,almost);
            var trimmed=State();Check(trimmed.At(1,5)!.End==neighborEnd && trimmed.At(-1,1)!.End==neighborEnd,"trim handle snaps to neighboring clip end");
            Check(HeadAt(40),"trimming must preserve the playhead");
            Button(w,"UnlinkButton");await Drag(w,At(1,20),At(1,30));var unlinked=State();
            Check(unlinked.At(1,15)!.Start>0 && unlinked.At(-1,0)!.Start==0,"unlink then video-only move");
            Check(HeadAt(40),"moving a clip must preserve the playhead");
            await ClickAt(w,RulerAt(42));await Task.Delay(550);using(var bmp=Snapshot(w))bmp.Save(shot,Drawing.Imaging.ImageFormat.Png);
            string old=clock.Text;Button(w,"Play");await Wait(()=>clock.Text!=old,"preview transport");Button(w,"Play");
            Button(w,"ExportButton");await Wait(()=>Directory.GetFiles(Home.Exports,"*.mp4").Length>0,"export button",45000);
            int count=State().Clips.Length;w.Close();await Wait(()=>!w.IsVisible,"close");w=new AssemblyWindow();w.Show();await Task.Delay(350);
            Check(State().Clips.Length==count && El<Button>(w,"ExportButton").IsEnabled,"draft reopen");
            Note("UI: ruler click/drag seeks; bin drop, clip selection, empty-lane drag, snapped move to adjacent clip and linked trim preserve the playhead. Both S modes, undo, playback, export and draft reopen passed.");
        }finally{w.Close();await Wait(()=>!w.IsVisible,"close editor");SetCursorPos(prior.X,prior.Y);}
    }
    static async Task DesktopColors(){
        var bars=new[]{Color.FromRgb(0,0,0),Color.FromRgb(255,255,255),Colors.Red,Color.FromRgb(0,255,0),Colors.Blue,Color.FromRgb(32,96,192),Color.FromRgb(200,160,20),Color.FromRgb(80,80,80)};
        var grid=new Grid();foreach(var c in bars){grid.ColumnDefinitions.Add(new());var block=new Border{Background=new SolidColorBrush(c)};Grid.SetColumn(block,grid.Children.Count);grid.Children.Add(block);}
        var window=new Window{Title="Moments QA · RGB",Width=640,Height=240,Content=grid,WindowStartupLocation=WindowStartupLocation.CenterScreen};window.Show();await Task.Delay(450);
        string file;
        var h=new WindowInteropHelper(window).Handle;
        var cap=new Capture(new Target(h,"QA",new(0,0,(int)grid.ActualWidth,(int)grid.ActualHeight),true),Profile.All[0],false,false,false,5,"SDR");
        try{await cap.Start();await Task.Delay(1400);}finally{file=(await cap.Stop())!;window.Close();}
        string result=await Proc.Run("ffprobe",["-v","error","-select_streams","v:0","-show_entries","stream=color_range,color_space","-of","csv=p=0",file]);
        Check(result.Contains("tv")&&result.Contains("bt470bg"),"capture SDR tags "+result);
        string legacy=Path.Combine(Home.Work,"legacy.mp4");
        await Proc.Run("ffmpeg",["-v","error","-y","-i",file,"-map","0:v:0","-c","copy","-colorspace","rgb","-color_range","pc","-movflags","+write_colr",legacy]);
        var info=await Media.Probe(legacy);Check(info.LegacyRgbTags,"old capture detection");
        string raw=Path.Combine(Home.Work,"decoded.rgb");await Proc.Run("ffmpeg",["-v","error","-y","-ss","0.5","-i",legacy,"-frames:v","1","-vf",Media.DesktopYuv+",format=rgb24","-f","rawvideo",raw]);
        var rgb=File.ReadAllBytes(raw);int worst=0;
        for(int i=0;i<bars.Length;i++){
            int k=(info.Width*(info.Height/2)+info.Width*(2*i+1)/16)*3;
            var expected=new[]{bars[i].R,bars[i].G,bars[i].B};
            for(int j=0;j<3;j++)worst=Math.Max(worst,Math.Abs(rgb[k+j]-expected[j]));
        }
        Check(worst<=7,"source RGB vs corrected decoded "+worst);
        var oldError=await WindowColorError(legacy,bars,false);var newError=await WindowColorError(legacy,bars,true);
        Note($"SDR: actual WGC/NVENC recording tags {result.Trim()}. Reference vs decoded max {worst}/255. On-screen max channel error: Windows media presenter {oldError}, Moments RGB preview {newError}.");
        Check(newError<=6 && newError<=oldError+2,"renderer correction");
    }
    static async Task<int> WindowColorError(string file,Color[] bars,bool useRgb){
        var media=new MediaElement{LoadedBehavior=MediaState.Manual,ScrubbingEnabled=true,Stretch=Stretch.Fill,IsMuted=true};
        var surface=new RgbVideoView{Stretch=Stretch.Fill};FrameworkElement view=useRgb?surface:media;
        var w=new Window{Title="Moments QA · Предпросмотр",Width=660,Height=300,Content=view,WindowStartupLocation=WindowStartupLocation.CenterScreen};
        bool open=false;media.MediaOpened+=(_,_)=>open=true;w.Show();
        try{
            if(useRgb){surface.Display(file,await Media.Probe(file),.5,false);await Wait(()=>surface.Source!=null,"RGB frame");}
            else{media.Source=new Uri(file);media.Play();await Wait(()=>open,"video opens");media.Position=TimeSpan.FromSeconds(.5);await Task.Delay(150);media.Pause();}await Task.Delay(150);
            using var bmp=Snapshot(w);var hwnd=new WindowInteropHelper(w).Handle;GetWindowRect(hwnd,out var r);int worst=0;
            for(int i=0;i<bars.Length;i++){
                var pt=Screen(view,view.ActualWidth*(2*i+1)/16,view.ActualHeight/2);var actual=bmp.GetPixel(pt.X-r.X,pt.Y-r.Y);
                worst=Math.Max(worst,new[]{Math.Abs(actual.R-bars[i].R),Math.Abs(actual.G-bars[i].G),Math.Abs(actual.B-bars[i].B)}.Max());
            }return worst;
        }finally{media.Close();await surface.ClearVideo();w.Close();}
    }
}

