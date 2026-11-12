using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace Moments;
public partial class AssemblyWindow : Window
{
    sealed record BinFile(string File,string Title,string Detail,BitmapImage? Thumb);
    static readonly HashSet<string> extensions=new(StringComparer.OrdinalIgnoreCase){".mp4",".mkv",".webm",".mov",".avi",".m4v",".mts",".m2ts"};
    internal static bool IsVideo(string path)=>extensions.Contains(Path.GetExtension(path));
    readonly string folder=Home.Temp("view");
    readonly CancellationTokenSource life=new();
    readonly ObservableCollection<BinFile> library=[];
    readonly Dictionary<string,Task<ClipInfo>> probes=new(StringComparer.OrdinalIgnoreCase);
    readonly List<Sequence> history=[],future=[];
    Sequence edit=new();
    Guid? selected;int lane=1,frame;
    bool loaded,closing,canClose;
    readonly TaskCompletionSource<bool> initialized=new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly SemaphoreSlim addGate=new(1,1);
    readonly HashSet<Task> additions=[];
    internal bool IsClosing=>closing;
    internal Task<bool> Ready=>initialized.Task;
    internal event Action? Exported;
    Task? libraryWork,exportWork;
    CancellationTokenSource? exportCancel;
    Point binAnchor;string? dragFile;int libraryVersion;

    internal AssemblyWindow(string? firstFile=null)
    {
        InitializeComponent();Format.ItemsSource=new[]{"MP4","MKV","WebM"};Format.SelectedIndex=0;Bin.ItemsSource=library;
        Monitor.Failed+=err=>{if(!closing){Pause();Say("Preview: "+err);}};
        Loaded+=async (_,_)=>{
            try{
                edit=projects.Load();loaded=true;ProjectName.Text=edit.Title;
                Refresh();ReloadBin();ProjectLabel();Say("S to split · Space to play · Ctrl+Z to undo");
                initialized.TrySetResult(true);
                if(firstFile!=null)await AddFiles([firstFile],1,edit.End);
            }catch(Exception ex){initialized.TrySetResult(false);Say(ex.Message);if(!loaded){TimelineCanvas.IsEnabled=false;Say("Could not open the draft. "+ex.Message);}}
        };
        Closing+=ClosingWindow;PreviewKeyDown+=OnKey;timer.Tick+=PlaybackTick;
        Activated+=(_,_) => (Application.Current.MainWindow as MainWindow)?.GiveSplitKeyToEditor(true);
        Deactivated+=(_,_) => (Application.Current.MainWindow as MainWindow)?.GiveSplitKeyToEditor(false);
    }
    void Say(string text){Status.Text=text;Status.ToolTip=text;}
    Task<ClipInfo> Probe(string file)
    {
        if(!probes.TryGetValue(file,out var job) || job.IsFaulted)probes[file]=job=Media.Probe(file,life.Token);
        return job;
    }
    bool Commit(Sequence next,string text)
    {
        if(ReferenceEquals(next,edit)||!loaded)return false;
        try{projects.Save(next);}catch(Exception ex){Say("Could not save the project. "+ex.Message);return false;}
        Pause();history.Add(edit);if(history.Count>80)history.RemoveAt(0);future.Clear();
        bool binChanged=next.Imports!=edit.Imports;edit=next;Refresh();if(binChanged)ReloadBin();Say(text);return true;
    }
    void Refresh()
    {
        Title="Moments · "+edit.Title+" · Video Editor";ProjectLabel();
        frame=Math.Clamp(frame,0,edit.End);if(!edit.Lanes().Contains(lane))lane=1;
        if(!edit.Clips.Any(c=>c.Id==selected))selected=null;
        FormatInfo.Text=edit.End==0?"":$"{edit.Width} × {edit.Height}   ·   {edit.Fps} FPS";
        ProjectFrame.Width=edit.Width;ProjectFrame.Height=edit.Height;
        ProjectName.Text=edit.Title;
        UndoButton.IsEnabled=history.Count>0;RedoButton.IsEnabled=future.Count>0;
        ExportButton.IsEnabled=edit.End>0 || exportCancel!=null;
        DrawTimeline();UpdatePreview(true);
        foreach(var file in edit.Clips.Select(c=>c.File).Distinct(StringComparer.OrdinalIgnoreCase))Warm(file);
    }
    void RestoreHistory(List<Sequence> from,List<Sequence> to)
    {
        if(from.Count==0)return;
        var next=from[^1];try{projects.Save(next);}catch(Exception ex){Say(ex.Message);return;}
        Pause();from.RemoveAt(from.Count-1);to.Add(edit);
        bool binChanged=edit.Imports!=next.Imports;edit=next;Refresh();if(binChanged)ReloadBin();Say("Draft saved.");
    }
    void UndoClick(object s,RoutedEventArgs e)=>RestoreHistory(history,future);
    void RedoClick(object s,RoutedEventArgs e)=>RestoreHistory(future,history);
    void ClearClick(object s,RoutedEventArgs e){if(edit.Clips.Length>0)Commit(new Sequence{Imports=edit.Imports,Title=edit.Title},"Timeline cleared. Press Ctrl+Z to undo.");}
    void NameChanged(object s,KeyboardFocusChangedEventArgs e){
        if(!loaded)return;string name=ProjectName.Text.Trim();if(name=="")name="My sequence";
        if(name!=edit.Title)Commit(edit with {Title=name},"Name saved.");else ProjectName.Text=name;
    }
    void NameKey(object s,KeyEventArgs e){if(e.Key==Key.Enter){Play.Focus();e.Handled=true;}}
    void AddVideoLane(object s,RoutedEventArgs e)=>Commit(edit with{VideoLanes=edit.VideoLanes+1},"Video track added.");
    void AddAudioLane(object s,RoutedEventArgs e)=>Commit(edit with{AudioLanes=edit.AudioLanes+1},"Audio track added.");
    void UnlinkClick(object s,RoutedEventArgs e){if(selected.HasValue)Commit(edit.Unlink(selected.Value),"Clips unlinked. You can now edit them independently.");}
    void DeleteClick(object s,RoutedEventArgs e){if(selected.HasValue)Commit(edit.Edit(selected.Value,ClipEdit.Delete),"Clip removed from the timeline.");}
    void ReloadBin(){
        libraryWork=FillBin(++libraryVersion);
    }
    async Task FillBin(int version)
    {
        try{
            var paths=Directory.EnumerateFiles(Home.Videos,"*",SearchOption.AllDirectories).Where(IsVideo)
                .Concat(edit.Imports).Concat(edit.Clips.Select(c=>c.File)).Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(p=>new FileInfo(p)).OrderByDescending(f=>f.Exists?f.CreationTimeUtc:DateTime.MinValue).ToArray();
            library.Clear();
            foreach(var file in paths)library.Add(new(file.FullName,Home.ClipTitle(file.Name),file.Exists?$"{file.Extension.TrimStart('.').ToUpperInvariant()}   ·   {file.Length/1048576d:0.#} MB":"File unavailable",null));
            for(int i=0;i<paths.Length;i++){
                if(version!=libraryVersion||closing)return;
                var file=paths[i];if(!file.Exists)continue;
                try{var thumb=await Media.Thumbnail(file,life.Token);
                    if(version!=libraryVersion||closing)return;
                    var row=library[i];bool picked=Equals(Bin.SelectedItem,row);library[i]=row with{Thumb=thumb};if(picked)Bin.SelectedIndex=i;
                }catch(OperationCanceledException){return;}catch(IOException){}
            }
        }catch(Exception ex){if(!closing)Say(ex.Message);}
    }
    void RegisterImports(IEnumerable<string> files)
    {
        var found=files.Where(f=>IsVideo(f)&&File.Exists(f)).Select(Path.GetFullPath).ToArray();
        if(found.Length==0){Say("Choose a supported video file.");return;}
        var all=edit.Imports.Concat(found).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if(all.Length!=edit.Imports.Length)Commit(edit with{Imports=all},"Video added to the media bin.");
        Bin.SelectedItem=library.FirstOrDefault(b=>string.Equals(b.File,found[0],StringComparison.OrdinalIgnoreCase));
    }
    void ImportClick(object s,RoutedEventArgs e){
        var picker=new GamePicker(true){Owner=this};if(picker.ShowDialog()==true && picker.SelectedFile!=null)RegisterImports([picker.SelectedFile]);
    }
    void BinDragOver(object s,DragEventArgs e){e.Effects=e.Data.GetDataPresent(DataFormats.FileDrop)?DragDropEffects.Copy:DragDropEffects.None;e.Handled=true;}
    void BinDrop(object s,DragEventArgs e){if(e.Data.GetData(DataFormats.FileDrop) is string[] files)RegisterImports(files);e.Handled=true;}
    void BinDown(object s,MouseButtonEventArgs e){
        binAnchor=e.GetPosition(Bin);dragFile=(ItemsControl.ContainerFromElement(Bin,e.OriginalSource as DependencyObject) as ListBoxItem)?.DataContext is BinFile item?item.File:null;
    }
    void BinMove(object s,MouseEventArgs e){
        if(e.LeftButton!=MouseButtonState.Pressed||dragFile==null)return;
        var delta=e.GetPosition(Bin)-binAnchor;
        if(Math.Abs(delta.X)<SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y)<SystemParameters.MinimumVerticalDragDistance)return;
        var data=new DataObject(DataFormats.FileDrop,new[]{dragFile});dragFile=null;DragDrop.DoDragDrop(Bin,data,DragDropEffects.Copy);
    }
    async void AppendClick(object s,RoutedEventArgs e){if(Bin.SelectedItem is BinFile b)await AddFiles([b.File],lane>0?lane:1,edit.End);}
    void BinDoubleClick(object s,MouseButtonEventArgs e){if(e.ChangedButton==MouseButton.Left)AppendClick(s,e);}
    async Task AddFiles(string[] files,int target,double position,double snapRadius=0)
    {
        if(!loaded || closing)return;
        var job=AddFilesCore(files,target,position,snapRadius);additions.Add(job);
        try{await job;}finally{additions.Remove(job);}
    }
    async Task AddFilesCore(string[] files,int target,double position,double snapRadius)
    {
        bool entered=false;int revision=projectRevision;
        try{
            await addGate.WaitAsync(life.Token);entered=true;
            var paths=files.Where(IsVideo).Select(Path.GetFullPath).ToArray();
            if(paths.Length==0){Say("Choose a video file.");return;}
            var media=new List<ClipInfo>();foreach(string p in paths)media.Add(await Probe(p));
            if(closing)return;
            if(revision!=projectRevision)throw new IOException("The active project changed. Send the clip again to add it to this project.");
            var next=edit;TimelineClip? first=null;
            int cursor=edit.DropStart(target,position,media.Sum(m=>Math.Max(1,edit.Frame(m.Duration))),snapRadius,out _);
            for(int i=0;i<paths.Length;i++){
                int count=next.Clips.Length;next=next.Add(paths[i],media[i],target,cursor);
                var added=next.Clips[count];first??=added;cursor=added.End;
            }
            var imports=paths.Where(p=>!p.StartsWith(Home.Videos+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)).Except(edit.Imports,StringComparer.OrdinalIgnoreCase).ToArray();
            if(imports.Length>0)next=next with{Imports=[..edit.Imports,..imports]};
            if(Commit(next,"Clip added. Drag an edge to trim or the center to move. Hold Alt to bypass snapping.")){
                selected=first!.Id;lane=target;DrawTimeline();
            }
        }catch(OperationCanceledException){}catch(Exception ex){Say(ex.Message);}finally{if(entered)addGate.Release();}
    }
    internal async Task AppendSource(string file,Interval[]? ranges=null,int[]? streams=null){
        if(closing)throw new OperationCanceledException("The video editor is closing.");
        // Snapshot the send request before probing; subsequent source edits must
        // not change the selected range or tracks already queued for insertion.
        var job=AppendSourceCore(Path.GetFullPath(file),ranges?.ToArray(),streams?.ToArray());additions.Add(job);
        try{await job;}finally{additions.Remove(job);}
    }
    async Task AppendSourceCore(string file,Interval[]? ranges,int[]? streams){
        var token=life.Token;int revision=projectRevision;
        await initialized.Task.WaitAsync(token);
        if(!loaded)throw new IOException("Could not open the current montage project.");
        await addGate.WaitAsync(token);
        try{
            var media=await Probe(file);token.ThrowIfCancellationRequested();
            if(revision!=projectRevision)throw new IOException("The active project changed. Send the selection again to add it to this project.");
            if(streams!=null)media=media with{Tracks=media.Tracks.Where(t=>streams.Contains(t.Index)).ToList()};
            var selectedRanges=ranges??[new Interval(0,media.Duration)];
            if(selectedRanges.Length==0)throw new ArgumentException("The selected range contains no kept frames.");
            // Read the current sequence after awaiting, not a stale snapshot.
            // A batch from A–B (possibly separated by cuts) is one undo step.
            var next=edit;TimelineClip? first=null;
            foreach(var range in selectedRanges){int count=next.Clips.Length;next=next.Add(file,media,1,next.End,range);first??=next.Clips[count];}
            if(!file.StartsWith(Home.Videos+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase) && !next.Imports.Contains(file,StringComparer.OrdinalIgnoreCase))
                next=next with{Imports=[..next.Imports,file]};
            CancelDrag();
            if(!Commit(next,ranges==null?"Source appended to the montage.":"Selected range appended to the montage. Ctrl+Z to undo."))
                throw new IOException("Could not save the montage draft. The selection was not added.");
            selected=first!.Id;lane=1;DrawTimeline();
        }finally{addGate.Release();}
    }
    void SplitAt(int target,int at)
    {
        var next=edit.Split(target,at);
        if(ReferenceEquals(next,edit)){Say($"No clip to split at this position on {Sequence.LaneName(target)}.");return;}
        if(Commit(next,$"Split on {Sequence.LaneName(target)}   ·   {edit.Timecode(at)}")){
            lane=target;selected=edit.At(target,at)?.Id;DrawTimeline();
        }
    }
    void SplitClick(object s,RoutedEventArgs e)=>SplitAt(lane,frame);
    void OnKey(object s,KeyEventArgs e)
    {
        if(!loaded || e.IsRepeat || Keyboard.FocusedElement is TextBox || Format.IsDropDownOpen || (mouseActive && e.Key!=Key.Escape))return;
        if(Keyboard.Modifiers==ModifierKeys.Control && e.Key is Key.Z or Key.Y){
            if(e.Key==Key.Z)UndoClick(s,e);else RedoClick(s,e);e.Handled=true;return;
        }
        if(Keyboard.Modifiers!=ModifierKeys.None)return;
        switch(e.Key){
            case Key.S:
                var p=Mouse.GetPosition(TimelineCanvas);var hover=TimelineCanvas.IsMouseOver?LaneAt(p.Y):0;
                if(hover!=0)SplitAt(hover,AtX(p.X));else SplitAt(lane,frame);break;
            case Key.Delete:DeleteClick(s,e);break;
            case Key.Space:PlayClick(s,e);break;
            case Key.Left:BackFrame(s,e);break;
            case Key.Right:ForwardFrame(s,e);break;
            case Key.Escape:CancelDrag();break;
            default:return;
        }
        e.Handled=true;
    }
    async void ExportClick(object s,RoutedEventArgs e){
        if(exportCancel!=null){exportCancel.Cancel();return;}if(!loaded||edit.End==0)return;
        Pause();var snapshot=edit;exportCancel=CancellationTokenSource.CreateLinkedTokenSource(life.Token);ExportButton.Content="Cancel export";
        var job=SequenceRender.Export(snapshot,((string)Format.SelectedItem).ToLowerInvariant(),(i,n)=>Say($"Exporting   ·   segment {i} of {n}"),exportCancel.Token);
        exportWork=job;
        try{var path=await job;Say("Saved to library · "+Path.GetFileName(path));if(!closing){ReloadBin();Exported?.Invoke();}}
        catch(OperationCanceledException){Say("Export canceled.");}catch(Exception ex){Say(ex.Message);Home.Log(ex.ToString());}
        finally{exportWork=null;exportCancel.Dispose();exportCancel=null;ExportButton.Content="Export";ExportButton.IsEnabled=edit.End>0;}
    }
    async void ClosingWindow(object? s,CancelEventArgs e){
        if(canClose)return;e.Cancel=true;if(closing)return;closing=true;
        if(loaded && ProjectName.IsKeyboardFocusWithin)NameChanged(ProjectName,null!);
        life.Cancel();Pause();timer.Stop();ClosePlayers();
        var jobs=warmJobs.Values.Cast<Task>().Concat(probes.Values).Concat(additions).Append(Monitor.ClearVideo()).Append(libraryWork??Task.CompletedTask).Append(exportWork??Task.CompletedTask);
        try{await Task.WhenAll(jobs.ToArray());}catch(Exception){}
        Home.Clear(folder);life.Dispose();canClose=true;_=Dispatcher.BeginInvoke(Close);
    }
}

