using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
namespace Moments;
public partial class AssemblyWindow
{
    const double LaneHeight=43,Ruler=26,SnapDistance=8;
    double zoom=1,pixelsPerFrame=1;
    bool drawing,mouseActive,scrubbing;
    Point anchor;
    TimelineClip? grabbed;
    ClipEdit gesture;
    Border? ghost;
    Line? playhead,hoverLine,snapLine;
    double SnapRadius=>(Keyboard.Modifiers&ModifierKeys.Alt)!=0?0:SnapDistance/pixelsPerFrame;
    static readonly Brush laneBack=Paint("#202831"),focusBack=Paint("#28333E"),gridInk=Paint("#36414C"),
        videoFill=Paint("#354D74"),videoInk=Paint("#7298D1"),audioFill=Paint("#234649"),audioInk=Paint("#69ADAA"),
        micFill=Paint("#49374C"),micInk=Paint("#B887AB"),cursorInk=Paint("#9FE4DE");
    static Brush Paint(string hex){var b=(Brush)new BrushConverter().ConvertFromString(hex)!;b.Freeze();return b;}
    int LaneAt(double y){
        int row=(int)Math.Floor((y-Ruler)/LaneHeight);
        return row<0||row>=edit.VideoLanes+edit.AudioLanes?0:row<edit.VideoLanes?edit.VideoLanes-row:-(row-edit.VideoLanes+1);
    }
    double Y(int n)=>Ruler+(n>0?edit.VideoLanes-n:edit.VideoLanes+(-n-1))*LaneHeight;
    int AtX(double x)=>Math.Max(0,(int)Math.Round(x/pixelsPerFrame));
    void Put(UIElement element,double x,double y){
        Canvas.SetLeft(element,x);Canvas.SetTop(element,y);TimelineCanvas.Children.Add(element);
    }
    void DrawTimeline()
    {
        if(!loaded || closing || drawing)return;drawing=true;
        try{
            int span=edit.End==0?edit.Fps*20:Math.Max(edit.Fps*3,edit.End+(int)Math.Ceiling(edit.End*.09));
            double view=Math.Max(150,TrackScroll.ViewportWidth);
            pixelsPerFrame=view*zoom/span;
            double width=Math.Max(view,view*zoom),height=Ruler+(edit.VideoLanes+edit.AudioLanes)*LaneHeight;
            TimelineCanvas.Width=width;TimelineCanvas.Height=height;TimelineCanvas.Focusable=true;
            TimelineCanvas.Children.Clear();Headers.Children.Clear();ghost=null;
            Headers.Children.Add(new TextBlock{Text="   TRACKS",Height=Ruler,Padding=new Thickness(2,5,0,0),FontSize=10,Foreground=Brushes.LightSlateGray});
            double left=TrackScroll.HorizontalOffset,right=left+view;
            double ideal=85/pixelsPerFrame;
            int step=new[]{1,2,5,10,edit.Fps,edit.Fps*2,edit.Fps*5,edit.Fps*10,edit.Fps*30,edit.Fps*60,edit.Fps*300,edit.Fps*600}.Order().FirstOrDefault(t=>t>=ideal);
            if(step==0)step=Math.Max(1,(int)Math.Ceiling(ideal/edit.Fps)*edit.Fps);
            for(int f=(int)(left/pixelsPerFrame)/step*step;f*pixelsPerFrame<right+2;f+=step){
                double x=f*pixelsPerFrame;Put(new TextBlock{Text=edit.Timecode(f),FontFamily=new FontFamily("Consolas"),FontSize=10,Foreground=Brushes.LightSlateGray},x+3,3);
                Put(new Rectangle{Width=1,Height=5,Fill=gridInk},x,Ruler-5);
            }
            foreach(int track in edit.Lanes()){
                var bg=new Border{Background=track==lane?focusBack:laneBack,BorderThickness=new Thickness(0,1,0,0),BorderBrush=gridInk,Width=width,Height=LaneHeight};
                Put(bg,0,Y(track));
                var header=new DockPanel{Width=105,Height=LaneHeight,Background=track==lane?focusBack:laneBack};
                string? laneTitle=null;
                if(track<0){
                    var names=edit.Clips.Where(c=>c.Lane==track).Select(c=>c.AudioName).Distinct().Take(2).ToArray();
                    if(names.Length==1 && names[0] is string name)laneTitle=Media.TrackLabel(name);
                    bool muted=edit.Muted.Contains(track);var mute=new Button{Content="M",Width=25,Height=23,Padding=new Thickness(0),Margin=new Thickness(0,0,4,0),ToolTip=(muted?"Unmute ":"Mute ")+(laneTitle??"track"),Foreground=muted?Brushes.Orange:Brushes.SlateGray};
                    mute.Click+=(_,_)=>Commit(edit with {Muted=muted?edit.Muted.Where(n=>n!=track).ToArray():[..edit.Muted,track]},muted?"Track unmuted.":"Track muted.");
                    DockPanel.SetDock(mute,Dock.Right);header.Children.Add(mute);
                }
                var select=new Button{Content=Sequence.LaneName(track),Background=Brushes.Transparent,BorderBrush=Brushes.Transparent,HorizontalContentAlignment=HorizontalAlignment.Left,
                    Padding=new Thickness(12,0,0,0),Margin=new Thickness(0),Foreground=track==lane?cursorInk:Brushes.LightGray,ToolTip="Select this track for splitting at the playhead"};
                if(laneTitle!=null){
                    var caption=new StackPanel{Width=61};caption.Children.Add(new TextBlock{Text=Sequence.LaneName(track),FontSize=11});
                    caption.Children.Add(new TextBlock{Text=laneTitle,FontSize=10,TextTrimming=TextTrimming.CharacterEllipsis,ToolTip=laneTitle});select.Content=caption;
                }
                select.Click+=(_,_)=>{lane=track;selected=edit.At(track,frame)?.Id;TimelineCanvas.Focus();DrawTimeline();};header.Children.Add(select);Headers.Children.Add(header);
            }
            foreach(var c in edit.Clips){
                double x=c.Start*pixelsPerFrame,w=c.Length*pixelsPerFrame;
                if(x+w<left || x>right)continue;
                var ink=c.Lane>0?videoInk:c.Lane%2==0?micInk:audioInk;
                var body=new Grid{ClipToBounds=true};
                if(c.Lane<0 && prepared.TryGetValue(c.File,out var asset) && asset.Audio.TryGetValue(c.Stream,out var stem))
                    body.Children.Add(Wave(stem.Peaks,c,ink,w,asset.Info.Duration*edit.Fps,left-x,right-x));
                string title=Home.ClipTitle(c.File);
                if(c.Lane<0){
                    string? name=c.AudioName;
                    if(name==null && prepared.TryGetValue(c.File,out var source))name=source.Info.Tracks.FirstOrDefault(t=>t.Index==c.Stream)?.Name;
                    if(name!=null)title=Media.TrackLabel(name)+"   ·   "+title;
                }
                var label=new TextBlock{Text=title,FontSize=11,Foreground=Brushes.WhiteSmoke,TextTrimming=TextTrimming.CharacterEllipsis,Margin=new Thickness(Math.Max(5,left-x+4),1,3,0),VerticalAlignment=VerticalAlignment.Top};
                if(w>=20)body.Children.Add(label);
                var tile=new Border{Width=Math.Max(.5,w-.6),Height=LaneHeight-6,Background=c.Lane>0?videoFill:c.Lane%2==0?micFill:audioFill,BorderBrush=c.Id==selected?Brushes.WhiteSmoke:ink,BorderThickness=new Thickness(c.Id==selected?2:1),
                    CornerRadius=new CornerRadius(3),Child=body,Opacity=edit.Muted.Contains(c.Lane)?.45:1,ToolTip=$"{title}   ·   {Sequence.LaneName(c.Lane)}\n{edit.Timecode(c.Start)} — {edit.Timecode(c.End)}"+(c.Link.HasValue?"   ·   linked":"")};
                Put(tile,x,Y(c.Lane)+3);
            }
            hoverLine=new Line{Y1=Ruler,Y2=height,Stroke=Brushes.LightSlateGray,StrokeDashArray=new DoubleCollection{2,3},IsHitTestVisible=false,Visibility=Visibility.Hidden};TimelineCanvas.Children.Add(hoverLine);
            playhead=new Line{Y1=0,Y2=height,Stroke=cursorInk,StrokeThickness=1.5,IsHitTestVisible=false};TimelineCanvas.Children.Add(playhead);MovePlayhead();
            snapLine=new Line{Y1=Ruler,Y2=height,Stroke=Brushes.Gold,StrokeThickness=1.5,Visibility=Visibility.Hidden,IsHitTestVisible=false};
            Panel.SetZIndex(snapLine,1);TimelineCanvas.Children.Add(snapLine);
            var chosen=edit.Clips.FirstOrDefault(c=>c.Id==selected);DeleteButton.IsEnabled=chosen!=null;UnlinkButton.IsEnabled=chosen?.Link!=null;SplitButton.IsEnabled=edit.End>0;
        }finally{drawing=false;}
    }
    static System.Windows.Shapes.Path Wave(double[] peaks,TimelineClip clip,Brush color,double width,double sourceFrames,double left,double right)
    {
        double from=Math.Clamp(left,0,width),to=Math.Clamp(right,from,width);
        int bins=Math.Max(1,(int)Math.Ceiling((to-from)/3));var g=new StreamGeometry();
        using(var pen=g.Open()){
            pen.BeginFigure(new Point(from,23),true,true);
            for(int i=0;i<=bins;i++){
                double x=from+(to-from)*i/bins,source=clip.In+clip.Length*x/width;
                int index=Math.Clamp((int)(source/Math.Max(1,sourceFrames)*peaks.Length),0,peaks.Length-1);
                pen.LineTo(new Point(x,23-10*Math.Sqrt(peaks[index])),true,false);
            }
            for(int i=bins;i>=0;i--){
                double x=from+(to-from)*i/bins,source=clip.In+clip.Length*x/width;
                int index=Math.Clamp((int)(source/Math.Max(1,sourceFrames)*peaks.Length),0,peaks.Length-1);
                pen.LineTo(new Point(x,24+10*Math.Sqrt(peaks[index])),true,false);
            }
        }
        g.Freeze();return new System.Windows.Shapes.Path{Data=g,Fill=color,IsHitTestVisible=false};
    }
    void MovePlayhead(){if(playhead!=null)playhead.X1=playhead.X2=frame*pixelsPerFrame;}
    void TimelineScroll(object s,ScrollChangedEventArgs e){
        if(e.ViewportWidthChange!=0 || e.HorizontalChange!=0)DrawTimeline();
    }
    void ZoomIn(object s,RoutedEventArgs e)=>Zoom(zoom*2);
    void ZoomOut(object s,RoutedEventArgs e)=>Zoom(zoom/2);
    void FitClick(object s,RoutedEventArgs e)=>Zoom(1);
    void Zoom(double value){zoom=Math.Clamp(value,1,512);DrawTimeline();TrackScroll.ScrollToHorizontalOffset(Math.Max(0,frame*pixelsPerFrame-TrackScroll.ViewportWidth*.45));}
    void ShowSnap(int? target){
        if(snapLine==null)return;
        snapLine.Visibility=target.HasValue?Visibility.Visible:Visibility.Hidden;
        if(target.HasValue)snapLine.X1=snapLine.X2=target.Value*pixelsPerFrame;
    }
    async void TimelineDragEnter(object s,DragEventArgs e){
        if(e.Data.GetData(DataFormats.FileDrop) is not string[] files)return;
        foreach(string path in files.Where(IsVideo)){
            if(probes.ContainsKey(path))continue;
            try{await Probe(path);}catch(Exception){/* Drop reports media errors. */}
            if(closing)return;
        }
    }
    void TimelineDragOver(object s,DragEventArgs e){
        var p=e.GetPosition(TimelineCanvas);int track=LaneAt(p.Y);int? snap=null;e.Effects=DragDropEffects.None;
        if(track!=0 && e.Data.GetData(DataFormats.FileDrop) is string[] files){
            var videos=files.Where(IsVideo).ToArray();
            if(videos.Length>0){
                e.Effects=DragDropEffects.Copy;
                if(videos.All(f=>probes.TryGetValue(f,out var info)&&info.IsCompletedSuccessfully))
                    edit.DropStart(track,p.X/pixelsPerFrame,videos.Sum(f=>Math.Max(1,edit.Frame(probes[f].Result.Duration))),SnapRadius,out snap);
            }
        }
        ShowSnap(snap);e.Handled=true;
    }
    void TimelineDragLeave(object s,DragEventArgs e)=>ShowSnap(null);
    async void TimelineDrop(object s,DragEventArgs e){
        var p=e.GetPosition(TimelineCanvas);int track=LaneAt(p.Y);e.Handled=true;ShowSnap(null);
        if(track!=0 && e.Data.GetData(DataFormats.FileDrop) is string[] files)await AddFiles(files,track,p.X/pixelsPerFrame,SnapRadius);
    }
    void TimelineDown(object s,MouseButtonEventArgs e){
        if(!loaded)return;TimelineCanvas.Focus();Pause();anchor=e.GetPosition(TimelineCanvas);int track=LaneAt(anchor.Y);
        scrubbing=anchor.Y>=0 && anchor.Y<Ruler;
        if(scrubbing)SetFrame(AtX(anchor.X));
        grabbed=track==0?null:edit.At(track,(int)(anchor.X/pixelsPerFrame));
        if(track!=0){lane=track;selected=grabbed?.Id;}
        if(grabbed!=null){
            double x=grabbed.Start*pixelsPerFrame,end=grabbed.End*pixelsPerFrame,margin=Math.Min(7,(end-x)*.23);
            gesture=anchor.X-x<margin?ClipEdit.TrimLeft:end-anchor.X<margin?ClipEdit.TrimRight:ClipEdit.Move;
        }
        DrawTimeline();mouseActive=true;TimelineCanvas.CaptureMouse();e.Handled=true;
    }
    int Destination(Point p)=>gesture==ClipEdit.Move && LaneAt(p.Y)!=0?LaneAt(p.Y):grabbed!.Lane;
    Sequence PlaceGrabbed(Point p,out int? snap)=>edit.Place(grabbed!.Id,gesture,(p.X-anchor.X)/pixelsPerFrame,Destination(p),SnapRadius,out snap);
    void TimelineMove(object s,MouseEventArgs e){
        if(!loaded)return;var p=e.GetPosition(TimelineCanvas);
        if(!mouseActive){
            int track=LaneAt(p.Y);var hit=edit.At(track,(int)(p.X/pixelsPerFrame));var nearEdge=hit!=null && Math.Min(p.X-hit.Start*pixelsPerFrame,hit.End*pixelsPerFrame-p.X)<7;
            TimelineCanvas.Cursor=nearEdge?Cursors.SizeWE:hit!=null?Cursors.Hand:Cursors.Cross;
            if(hoverLine!=null){hoverLine.Visibility=track!=0?Visibility.Visible:Visibility.Hidden;hoverLine.X1=hoverLine.X2=p.X;}return;
        }
        if(scrubbing){if(p.Y>=0 && p.Y<Ruler)SetFrame(AtX(p.X));return;}
        if(grabbed==null)return;
        if((p-anchor).Length<3){ShowSnap(null);if(ghost!=null)ghost.Visibility=Visibility.Hidden;return;}
        try{
            var next=PlaceGrabbed(p,out int? snap);var placed=next.Clips.First(c=>c.Id==grabbed.Id);
            ShowSnap(snap);
            if(ghost==null){ghost=new Border{BorderBrush=Brushes.White,Background=Paint("#3333BBBB"),BorderThickness=new Thickness(1),Height=LaneHeight-6,IsHitTestVisible=false};TimelineCanvas.Children.Add(ghost);}
            ghost.Visibility=Visibility.Visible;ghost.Width=Math.Max(2,placed.Length*pixelsPerFrame);
            Canvas.SetLeft(ghost,placed.Start*pixelsPerFrame);Canvas.SetTop(ghost,Y(placed.Lane)+3);
        }catch(InvalidOperationException){ShowSnap(null);if(ghost!=null)ghost.Visibility=Visibility.Hidden;}
    }
    void TimelineUp(object s,MouseButtonEventArgs e){
        if(!mouseActive)return;var p=e.GetPosition(TimelineCanvas);var c=grabbed;
        try{
            if(scrubbing && p.Y>=0 && p.Y<Ruler)SetFrame(AtX(p.X));
            if(c==null || (p-anchor).Length<3)return;
            int dest=Destination(p);var next=PlaceGrabbed(p,out _);
            if(Commit(next,"Timeline updated.")){lane=dest;selected=c.Id;DrawTimeline();}
        }
        catch(InvalidOperationException ex){Say(ex.Message);}
        finally{CancelDrag();}
    }
    void TimelineLeave(object s,MouseEventArgs e){if(hoverLine!=null)hoverLine.Visibility=Visibility.Hidden;}
    void CaptureLost(object s,MouseEventArgs e){mouseActive=false;scrubbing=false;grabbed=null;ShowSnap(null);if(ghost!=null){TimelineCanvas.Children.Remove(ghost);ghost=null;}}
    void CancelDrag(){if(TimelineCanvas.IsMouseCaptured)TimelineCanvas.ReleaseMouseCapture();}
}
