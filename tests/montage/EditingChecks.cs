using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace Moments;

internal static class EditingChecks
{
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    [DllImport("user32.dll")]static extern bool IsWindowEnabled(nint window);
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    static T Field<T>(object o,string name)=>(T)o.GetType().GetField(name,Private)!.GetValue(o)!;
    static void Set(object o,string name,object value)=>o.GetType().GetField(name,Private)!.SetValue(o,value);
    static void Invoke(object o,string name,params object?[] args)=>o.GetType().GetMethod(name,Private)!.Invoke(o,args);
    static T El<T>(Window w,string name) where T:class=>(T)w.FindName(name);
    static void Click(Window w,string name)=>El<Button>(w,name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    static async Task Wait(Func<bool> ready){for(int i=0;i<600;i++){if(ready())return;await Task.Delay(50);}throw new Exception("Editing test timed out");}
    static bool Enabled(Window w)=>IsWindowEnabled(new WindowInteropHelper(w).Handle);
    static void Mark(Editor w,double a,double b){Set(w,"a",a);Set(w,"b",b);Invoke(w,"RefreshCuts");}
    static void Shot(Window w,string file){w.UpdateLayout();var image=new RenderTargetBitmap((int)w.ActualWidth,(int)w.ActualHeight,96,96,PixelFormats.Pbgra32);image.Render(w);
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));using var output=File.Create(file);png.Save(output);}
    public static async Task Run(Action<string> note,string reports){
        string a=Path.Combine(Home.Videos,"Range source.mp4"),b=Path.Combine(Home.Videos,"Second source.mp4"),draft=Path.Combine(Home.Root,"Projects","current.json");
        await Proc.Run("ffmpeg",["-v","error","-y","-f","lavfi","-i","color=red:size=320x180:rate=24,drawbox=color=lime:t=fill:enable='gte(t,1)',drawbox=color=white:t=fill:enable='gte(t,2)'",
            "-f","lavfi","-i","sine=frequency=440:sample_rate=48000","-f","lavfi","-i","sine=frequency=880:sample_rate=48000",
            "-map","0:v","-map","1:a","-map","2:a","-metadata:s:a:0","title=System","-metadata:s:a:1","title=Microphone","-t","3","-c:v","libx264","-preset","ultrafast","-c:a","aac",a]);
        await Proc.Run("ffmpeg",["-v","error","-y","-f","lavfi","-i","color=blue:size=320x180:rate=30","-f","lavfi","-i","sine=frequency=660:sample_rate=48000",
            "-map","0:v","-map","1:a","-metadata:s:a:0","title=System","-t","2","-c:v","libx264","-preset","ultrafast","-c:a","aac",b]);
        var ai=await Media.Probe(a);var bi=await Media.Probe(b);
        var initial=new Sequence{Title="Existing montage"}.Add(b,bi,1,0);initial.Save(draft);
        var native=new Sequence().Add(a,ai,1,0,new Interval(.5,1));
        Check(native.Fps==24 && native.Clips.All(c=>c.In==12&&c.Length==12&&c.SourceLength==72),"empty montage range did not adopt source FPS");
        bool rejected=false;try{initial.Add(a,ai,1,initial.End,new Interval(1,1.001));}catch(ArgumentException){rejected=true;}
        Check(rejected,"sub-frame selection accepted");
        var main=new MainWindow(new UpdateService(new HttpClient(new FakeReleases{Tag="v9.0.0"})));Application.Current.MainWindow=main;main.Show();
        var workspace=main.Editing;var first=workspace.OpenClip(a);var second=workspace.OpenClip(b);
        await Wait(()=>El<Button>(first,"SelectionToTimelineButton").IsEnabled && El<Button>(second,"SourceToTimelineButton").IsEnabled);
        var timeline=workspace.OpenTimeline();Check(await timeline.Ready,"montage load failed");
        Check(ReferenceEquals(timeline,workspace.OpenTimeline()),"two montage windows would overwrite the same draft");
        Check(first.Owner==null && second.Owner==null && timeline.Owner==null && Enabled(main)&&Enabled(first)&&Enabled(second)&&Enabled(timeline),"editing windows are modal or owned");
        main.WindowState=WindowState.Minimized;await Task.Delay(100);Check(first.IsVisible&&second.IsVisible&&timeline.IsVisible,"minimizing library hid independent windows");main.Show();main.WindowState=WindowState.Normal;
        Click(main,"CheckUpdateButton");await Wait(()=>El<Button>(main,"CheckUpdateButton").IsEnabled);
        Check(!El<Button>(main,"DownloadUpdateButton").IsEnabled,"update enabled with independent editor open");
        Click(main,"DownloadUpdateButton");Check(El<TextBlock>(main,"UpdateStatus").Text.Contains("close the editor"),"install guard missed ownerless windows");

        Field<List<Interval>>(first,"cuts").Add(new Interval(1,1.5));Mark(first,2.5,.5);
        var boxes=El<StackPanel>(first,"AudioPanel").Children.OfType<Grid>().SelectMany(g=>g.Children.OfType<CheckBox>()).ToArray();boxes[1].IsChecked=false;
        await first.SendToTimeline(true);var selected=Sequence.Load(draft);var added=selected.Clips.Skip(initial.Clips.Length).ToArray();
        Check(selected.Title==initial.Title && selected.Clips.Take(initial.Clips.Length).SequenceEqual(initial.Clips),"send replaced previous edits");
        Check(added.Length==4 && added.Count(c=>c.Lane>0)==2 && added.All(c=>c.SourceLength==90 && (c.Lane>0 || c.Stream==ai.Tracks[0].Index)),"cuts or track choices were lost");
        Check(added[0].Start==60&&added[0].In==15&&added[0].Length==15&&added[2].Start==75&&added[2].In==45&&added[2].Length==30,"selected source offsets or mixed FPS incorrect");
        Click(timeline,"UndoButton");Check(Sequence.Load(draft).Clips.SequenceEqual(initial.Clips),"range batch was not one undo step");
        Click(timeline,"RedoButton");Check(Sequence.Load(draft).Clips.SequenceEqual(selected.Clips),"redo lost selection");
        await CheckExport(selected,reports);

        await second.SendToTimeline(false);var full=Sequence.Load(draft);
        Check(full.End==selected.End+60 && ReferenceEquals(timeline,workspace.OpenTimeline()),"whole-source action did not reuse/append to current montage");
        Field<List<Interval>>(first,"cuts").Clear();Mark(first,0,.5);Mark(second,0,.5);
        var hold=new TaskCompletionSource<ClipInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        Field<Dictionary<string,Task<ClipInfo>>>(timeline,"probes")[a]=hold.Task;
        var send1=first.SendToTimeline(true);var send2=second.SendToTimeline(true);
        Check(!send1.IsCompleted && !send2.IsCompleted,"concurrent sends were silently dropped instead of queued");
        El<TextBox>(timeline,"ProjectName").Text="Edits while loading";Invoke(timeline,"NameChanged",El<TextBox>(timeline,"ProjectName"),null);
        hold.SetResult(ai);await Task.WhenAll(send1,send2);var queued=Sequence.Load(draft);
        var tail=queued.Clips.Skip(full.Clips.Length).Where(c=>c.Lane>0).ToArray();
        Check(queued.Title=="Edits while loading"&&tail.Length==2&&tail[0].File==a&&tail[1].File==b&&tail[0].Start==full.End&&tail[1].Start==full.End+15,"queued sends lost order, edits, or appended at stale position");
        Mark(first,1,1);await first.SendToTimeline(true);Check(Sequence.Load(draft).Clips.SequenceEqual(queued.Clips),"empty range changed montage");
        first.Width=940;first.Height=650;first.Activate();await Task.Delay(200);Shot(first,Path.Combine(reports,"clip-range-to-timeline.png"));
        timeline.Width=1100;timeline.Height=700;timeline.Activate();await Task.Delay(200);Shot(timeline,Path.Combine(reports,"shared-montage.png"));
        first.Close();await Wait(()=>!first.IsVisible);Check(second.IsVisible&&timeline.IsVisible&&Enabled(second)&&Enabled(main),"closing source closed or blocked other windows");

        var canceled=new TaskCompletionSource<ClipInfo>(TaskCreationOptions.RunContinuationsAsynchronously);Field<Dictionary<string,Task<ClipInfo>>>(timeline,"probes")[a]=canceled.Task;
        var pending=timeline.AppendSource(a,[new Interval(0,.5)]);timeline.Close();canceled.SetResult(ai);
        try{await pending;throw new Exception("closed montage accepted pending append");}catch(OperationCanceledException){}
        await Wait(()=>!timeline.IsVisible);Check(second.IsVisible&&Sequence.Load(draft).Clips.SequenceEqual(queued.Clips),"close race modified saved montage or source window");
        var reopened=workspace.OpenTimeline();Check(await reopened.Ready,"reopen failed");Check(Sequence.Load(draft).Clips.SequenceEqual(queued.Clips),"reopened draft changed");
        string projectA=Path.Combine(Home.Root,"Projects","First montage.moments");reopened.SaveProjectAs(projectA);
        Mark(second,0,.5);await second.SendToTimeline(true);var savedA=Sequence.Load(projectA);
        Check(savedA.End==queued.End+15 && Sequence.Load(draft).Clips.SequenceEqual(queued.Clips),"Save as changed previous draft or autosave missed the named project");
        reopened.NewProject();string projectB=reopened.ProjectFile;
        Check(projectB!=projectA && Sequence.Load(projectB).End==0 && Sequence.Load(projectA).Clips.SequenceEqual(savedA.Clips),"new project overwrote old montage");
        await workspace.Append(a,[new Interval(1,2)],[]);var savedB=Sequence.Load(projectB);
        Check(savedB.Clips.Length==1 && savedB.Clips[0].In==24 && savedB.End==24,"new project did not receive selected range or silent audio selection");
        reopened.OpenProject(projectA);Check(Field<Sequence>(reopened,"edit").Clips.SequenceEqual(savedA.Clips) && !El<Button>(reopened,"UndoButton").IsEnabled,"open mixed histories or lost project clips");
        var resume=new MontageProjects(Path.Combine(Home.Root,"Projects"));Check(resume.Load().Clips.SequenceEqual(savedA.Clips)&&resume.CurrentPath==projectA,"last named project was not restored");
        var switched=new TaskCompletionSource<ClipInfo>(TaskCreationOptions.RunContinuationsAsynchronously);Field<Dictionary<string,Task<ClipInfo>>>(reopened,"probes")[a]=switched.Task;
        var obsolete=reopened.AppendSource(a,[new Interval(0,1)]);reopened.OpenProject(projectB);switched.SetResult(ai);
        try{await obsolete;throw new Exception("queued send entered a different project");}catch(IOException){}
        Check(Sequence.Load(projectB).Clips.SequenceEqual(savedB.Clips)&&Sequence.Load(projectA).Clips.SequenceEqual(savedA.Clips),"switch race changed a saved project");
        string broken=Path.Combine(Home.Root,"Projects","broken.moments");File.WriteAllText(broken,"not a project");
        try{reopened.OpenProject(broken);throw new Exception("invalid project accepted");}catch(System.Text.Json.JsonException){}
        Check(reopened.ProjectFile==projectB && Field<Sequence>(reopened,"edit").Clips.SequenceEqual(savedB.Clips),"failed open replaced current project");
        reopened.Width=940;reopened.Height=660;reopened.Activate();await Task.Delay(200);Shot(reopened,Path.Combine(reports,"named-montage-project.png"));
        await workspace.CloseAll();Check(!workspace.HasWindows&&!second.IsVisible&&!reopened.IsVisible,"independent windows did not finish asynchronous cleanup");
        Check(El<Button>(main,"DownloadUpdateButton").IsEnabled,"update stayed blocked after editor cleanup");
        note("Independent windows: main, two clip previews and shared montage remain enabled; minimize/close isolation, single live draft and update/shutdown guards passed.");
        note("Range sends: reversed A/B, kept cuts, selected audio, mixed FPS, source offsets, append-only edits, one-step undo/redo, queued sends, concurrent rename, empty range, close race and draft reopen passed.");
        note("Selected-range MP4: decoded source colors, duration and enabled/disabled audio frequencies verified.");
        note("Projects: named save-as, autosave, retained old draft, new/open isolation, fresh undo history, last-project restore, malformed-file recovery and pending-send project-switch race passed.");
    }
    static async Task CheckExport(Sequence edit,string reports){
        string output=await SequenceRender.Export(edit,"mp4",(_,_)=>{},CancellationToken.None);var info=await Media.Probe(output);
        Check(Math.Abs(info.Duration-3.5)<.075,"range export duration incorrect");
        foreach(var (time,color) in new[]{(2.25,0),(2.75,1),(3.25,3)}){
            string raw=Path.Combine(reports,"range.rgb");await Proc.Run("ffmpeg",["-v","error","-y","-ss",Home.N(time),"-i",output,"-vf","scale=1:1,format=rgb24","-frames:v","1","-f","rawvideo",raw]);var rgb=File.ReadAllBytes(raw);
            Check(color==3?rgb.Min()>220:rgb[color]>180 && rgb[(color+1)%3]<60,"range export used wrong source frames");File.Delete(raw);
        }
        string pcmFile=Path.Combine(reports,"range.s16");await Proc.Run("ffmpeg",["-v","error","-y","-ss","2.15","-i",output,"-t","0.2","-map","0:a:0","-ac","1","-ar","8000","-f","s16le",pcmFile]);var pcm=File.ReadAllBytes(pcmFile);
        double Power(int hz){double re=0,im=0;for(int i=0;i<pcm.Length/2;i++){double v=BitConverter.ToInt16(pcm,i*2),p=2*Math.PI*hz*i/8000;re+=v*Math.Cos(p);im+=v*Math.Sin(p);}return Math.Sqrt(re*re+im*im)/(pcm.Length/2);}
        Check(Power(440)>100 && Power(880)<Power(440)*.1,"muted source audio was included or selected audio missing");File.Delete(pcmFile);File.Delete(output);
    }
}
