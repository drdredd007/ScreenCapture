using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Path=System.IO.Path;
namespace Moments;
public partial class Editor : Window
{
    readonly string path,folder=Home.Temp("view");
    readonly EditingWorkspace workspace;
    bool sending;
    readonly CancellationTokenSource life=new();
    CancellationTokenSource? exporting;
    readonly List<Interval> cuts=[];
    readonly ClipEvent[] gameEvents;
    readonly Dictionary<Track, (MediaPlayer Player,CheckBox Box)> stems=[];
    readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(50)};
    Task? work,loading;
    ClipInfo? info;
    bool internalMove,playing,ready,closing,canClose;
    double a,b,playStart;
    readonly Stopwatch playback=new();
    internal Editor(string path,EditingWorkspace? workspace=null)
    {
        this.workspace=workspace??(Application.Current.MainWindow as MainWindow)?.Editing??new EditingWorkspace();
        this.path=path;gameEvents=ClipEvents.Read(path)?.Events??[];InitializeComponent();
        Video.Failed+=err=>{if(!closing){Pause();Say("Preview: "+err);}};
        Style=(Style)FindResource(typeof(Window));
        var file=new FileInfo(path);
        FileTitle.Text=Home.ClipTitle(path);FileTitle.ToolTip=path;Title="Moments · "+FileTitle.Text;
        SourcePath.Text=Path.GetRelativePath(Home.Root,path);SourcePath.ToolTip=path;
        CreatedText.Text=file.CreationTime.ToString("dd.MM.yyyy   HH:mm");FileSizeText.Text=$"{file.Length/1048576d:0.##} MB";
        OutputName.Text=FileTitle.Text[..Math.Min(70,FileTitle.Text.Length)]+" · edit";FormatBox.ItemsSource=new[]{"MP4 · H.264","MKV · H.264","WebM · VP9"};FormatBox.SelectedIndex=0;ExportButton.IsEnabled=false;
        timer.Tick+=Tick;Loaded+=async (_,_)=>{loading=Prepare();await loading;};Closing+=CloseEditor;
        PreviewKeyDown+=(_,e)=>{
            if(e.OriginalSource is TextBox || !ready)return;
            if(e.Key==System.Windows.Input.Key.Space){TogglePlay();e.Handled=true;}
            if(e.Key==System.Windows.Input.Key.A){SetA(this,new());e.Handled=true;}
            if(e.Key==System.Windows.Input.Key.B){SetB(this,new());e.Handled=true;}
        };
    }
    void Say(string msg){Feedback.Text=msg;Feedback.ToolTip=msg;}
    async Task Prepare()
    {
        try{
            info=await Media.Probe(path,life.Token);b=info.Duration;Timeline.Maximum=b;
            FileMeta.Text=$"{info.Width} × {info.Height}   ·   {info.Fps:0.##} FPS\n{info.VideoCodec.ToUpperInvariant()}   ·   {Home.Time(b)}";
            RefreshCuts();LoadGameEvents();
            foreach(var t in info.Tracks){
                var player=new MediaPlayer();
                var box=AddAudioLane(t,player);
                stems.Add(t,(player,box));
            }
            if(info.Tracks.Count==0)AudioPanel.Children.Add(new TextBlock{Text="No audio tracks",Style=(Style)FindResource("Sub")});
            Video.Display(path,info,0,false);
            foreach(var (track,stem) in stems){
                var audio=await Media.PreviewAudio(path,track,folder,info.Duration,life.Token);
                SetWaveform(track,audio.Peaks);stem.Player.Open(new Uri(audio.File));stem.Player.Pause();
            }
            life.Token.ThrowIfCancellationRequested();ready=true;Seek(0);Say("Ready. Press Space to play, or A and B to set markers.");timer.Start();
        }catch(OperationCanceledException){}catch(Exception ex){Say(ex.Message);Home.Log(ex.ToString());}
        finally{ExportButton.IsEnabled=info!=null && !closing;UpdateSendButtons();}
    }
    async void CloseEditor(object? sender,CancelEventArgs e)
    {
        if(canClose)return;e.Cancel=true;if(closing)return;closing=true;
        life.Cancel();exporting?.Cancel();timer.Stop();Pause();
        try{if(loading!=null)await loading;await Video.ClearVideo();if(work!=null)await work;}
        finally{foreach(var p in stems.Values)p.Player.Close();Home.Clear(folder);life.Dispose();canClose=true;_=Dispatcher.BeginInvoke(Close);}
    }
    void PlayClick(object s,RoutedEventArgs e)=>TogglePlay();
    void UpdateSendButtons(){SourceToTimelineButton.IsEnabled=SelectionToTimelineButton.IsEnabled=ready && !closing && !sending;}
    async void AssemblyClick(object s,RoutedEventArgs e)=>await SendToTimeline(false);
    async void SelectionClick(object s,RoutedEventArgs e)=>await SendToTimeline(true);
    internal async Task SendToTimeline(bool selection){
        if(!ready || info==null || closing || sending)return;
        Interval[]? ranges=null;int[]? streams=null;
        if(selection){
            double lo=Math.Min(a,b),hi=Math.Max(a,b);
            ranges=Media.Kept(info.Duration,cuts).Select(r=>new Interval(Math.Max(lo,r.Start),Math.Min(hi,r.End))).Where(r=>r.End>r.Start).ToArray();
            if(ranges.Length==0){Say("Select a kept range between A and B.");return;}
            streams=stems.Where(kv=>kv.Value.Box.IsChecked==true).Select(kv=>kv.Key.Index).ToArray();
        }
        sending=true;UpdateSendButtons();Pause();
        try{await workspace.Append(path,ranges,streams);if(!closing)Say(selection?"Selected range added to the current montage.":"Source added to the current montage.");}
        catch(OperationCanceledException){if(!closing)Say("The video editor was closed; the selection was not added.");}
        catch(Exception ex){if(!closing)Say(ex.Message);Home.Log("Send to montage: "+ex);}
        finally{sending=false;if(!closing)UpdateSendButtons();}
    }
    void TogglePlay()
    {
        if(!ready || info==null)return;if(playing){Pause();return;}
        var kept=Media.Kept(info.Duration,cuts);
        if(kept.Count==0)return;
        double at=Timeline.Value;if(at>=kept[^1].End-.025)at=kept[0].Start;
        var next=kept.FirstOrDefault(r=>r.End>at);playing=true;Seek(Math.Max(next.Start,at));
        foreach(var p in stems.Values)p.Player.Play();PlayButton.Content="Ⅱ   Pause";
    }
    void Pause(){
        bool wasPlaying=playing;playing=false;playback.Reset();
        foreach(var p in stems.Values)p.Player.Pause();PlayButton.Content="▶   Play";
        if(wasPlaying && info!=null && !closing)Video.Display(path,info,Timeline.Value,false);
    }
    void Tick(object? s,EventArgs e)
    {
        if(!playing || info==null)return;double pos=playStart+playback.Elapsed.TotalSeconds;
        var next=Media.Kept(info.Duration,cuts).FirstOrDefault(r=>r.End>pos+.015);
        if(next.End==0){Pause();Seek(0);return;}
        if(pos+.015<next.Start){Seek(next.Start);return;}
        var t=TimeSpan.FromSeconds(pos);foreach(var p in stems.Values)if(Math.Abs((p.Player.Position-t).TotalSeconds)>.18)p.Player.Position=t;
        Video.Display(path,info,pos,true);
        SetSlider(pos);
    }
    void SetSlider(double pos){internalMove=true;Timeline.Value=pos;internalMove=false;PositionText.Text=Stamp(pos)+"  /  "+Stamp(info?.Duration??0);UpdatePlayhead();}
    static string Stamp(double s)=>Home.Time(s)+"."+((int)(s*10)%10);
    void Seek(double pos){
        if(info==null)return;var t=TimeSpan.FromSeconds(Math.Clamp(pos,0,info.Duration));playStart=t.TotalSeconds;
        if(playing)playback.Restart();Video.Display(path,info,playStart,playing,true);
        foreach(var p in stems.Values)p.Player.Position=t;SetSlider(playStart);
    }
    void Scrub(object s,RoutedPropertyChangedEventArgs<double> e){if(!internalMove && ready)Seek(e.NewValue);}
    void SetA(object s,RoutedEventArgs e){a=Timeline.Value;RefreshCuts();}
    void SetB(object s,RoutedEventArgs e){b=Timeline.Value;RefreshCuts();}
    void RemoveAB(object s,RoutedEventArgs e)=>ApplyCut(false);
    void KeepAB(object s,RoutedEventArgs e)=>ApplyCut(true);
    void ApplyCut(bool keep)
    {
        if(info==null)return;var lo=Math.Min(a,b);var hi=Math.Max(a,b);if(hi-lo<.04){Say("Leave at least one frame between the markers.");return;}
        List<Interval> add=[];if(keep){if(lo>0)add.Add(new(0,lo));if(hi<info.Duration)add.Add(new(hi,info.Duration));}else add.Add(new(lo,hi));
        var result=Media.Kept(info.Duration,cuts.Concat(add));if(result.Sum(r=>r.End-r.Start)<.04){Say("Keep at least one frame to export.");return;}
        cuts.AddRange(add);Pause();RefreshCuts();Say("Cuts updated. Playback skips the removed sections.");
    }
    void UndoCut(object s,RoutedEventArgs e){if(CutList.SelectedIndex>=0){cuts.RemoveAt(CutList.SelectedIndex);RefreshCuts();}}
    void RefreshCuts(){
        if(info==null)return;Markers.Text=$"A   {Stamp(a)}       B   {Stamp(b)}";CutList.ItemsSource=null;CutList.ItemsSource=cuts.Select(r=>Stamp(r.Start)+"   →   "+Stamp(r.End)).ToArray();if(cuts.Count>0)CutList.SelectedIndex=cuts.Count-1;
        CutHint.Text=cuts.Count==0?"Set markers A and B to select a range.":$"Cuts: {cuts.Count}";
        CutList.Visibility=RestoreCut.Visibility=cuts.Count==0?Visibility.Collapsed:Visibility.Visible;
        ResultLength.Text="Output duration   "+Stamp(Media.Kept(info.Duration,cuts).Sum(r=>r.End-r.Start));DrawBar();
    }
    async void ExportClick(object s,RoutedEventArgs e)
    {
        if(exporting!=null){exporting.Cancel();return;}if(info==null || loading?.IsCompleted!=true)return;
        exporting=CancellationTokenSource.CreateLinkedTokenSource(life.Token);ExportButton.Content="Cancel export";Pause();
        work=DoExport(exporting.Token);try{await work;}finally{exporting.Dispose();exporting=null;ExportButton.Content="Export";}
    }
    async Task DoExport(CancellationToken token)
    {
        try{
            var ext=new[]{"mp4","mkv","webm"}[FormatBox.SelectedIndex];
            var file=await Media.Export(path,OutputName.Text,ext,Media.Kept(info!.Duration,cuts),stems.Where(kv=>kv.Value.Box.IsChecked==true).Select(kv=>kv.Key).ToArray(),(i,count)=>Say($"Exporting · segment {i+1} of {count}…"),token);
            Say("Saved to library · "+Path.GetFileName(file));
            workspace.NotifyLibraryChanged();
        }catch(OperationCanceledException){Say("Export canceled.");}catch(Exception ex){Home.Log(ex.ToString());Say(ex.Message);}
    }
}


