using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ShapePath=System.Windows.Shapes.Path;
namespace Moments;
public partial class Editor
{
    readonly Dictionary<Track, ShapePath> waves=[];
    enum DragTarget{None,Playhead,A,B}
    DragTarget dragging;
    static Brush Tint(byte r,byte g,byte b,byte alpha=255)
    {
        var brush=new SolidColorBrush(Color.FromArgb(alpha,r,g,b));brush.Freeze();return brush;
    }
    CheckBox AddAudioLane(Track track,MediaPlayer player)
    {
        var row=new Grid{Height=27,Margin=new Thickness(0,4,0,0)};
        row.ColumnDefinitions.Add(new(){Width=new GridLength(128)});row.ColumnDefinitions.Add(new());
        var box=new CheckBox{Content=Media.TrackLabel(track.Name),IsChecked=true,FontSize=12,Margin=new Thickness(0),ToolTip="Include in playback and export"};
        row.Children.Add(box);
        var frame=new Grid{Background=Tint(39,48,58),ClipToBounds=true,Cursor=Cursors.Hand};
        Grid.SetColumn(frame,1);row.Children.Add(frame);
        frame.MouseLeftButtonDown+=(_,e)=>{if(ready && info!=null)Seek(e.GetPosition(frame).X/Math.Max(1,frame.ActualWidth)*info.Duration);};
        bool mic=track.Name=="Microphone";var color=mic?Tint(220,146,183):Tint(141,164,223);
        frame.Children.Add(new Border{Height=1,VerticalAlignment=VerticalAlignment.Center,Background=color,Opacity=.4});
        var path=new ShapePath{Fill=color};waves.Add(track,path);
        var canvas=new Canvas{Width=960,Height=24};canvas.Children.Add(path);
        frame.Children.Add(new Viewbox{Child=canvas,Stretch=Stretch.Fill,Margin=new Thickness(0,2,0,2),IsHitTestVisible=false});
        box.Click+=(_,_)=>{player.IsMuted=box.IsChecked!=true;frame.Opacity=player.IsMuted?.35:1;};
        AudioPanel.Children.Add(row);return box;
    }
    void SetWaveform(Track track,double[] peaks)
    {
        var shape=new StreamGeometry();
        using(var pen=shape.Open()){
            pen.BeginFigure(new Point(0,12),true,true);
            for(int i=0;i<peaks.Length;i++)pen.LineTo(new Point(i,12-Math.Sqrt(peaks[i])*11),true,false);
            for(int i=peaks.Length-1;i>=0;i--)pen.LineTo(new Point(i,12+Math.Sqrt(peaks[i])*11),true,false);
        }
        shape.Freeze();waves[track].Data=shape;
    }
    void UpdatePlayhead()
    {
        if(info==null || info.Duration<=0 || PlayheadOverlay==null)return;
        double max=Math.Max(0,PlayheadOverlay.ActualWidth-1.5);
        Canvas.SetLeft(Playhead,Math.Clamp(Timeline.Value/info.Duration*max,0,max));
    }
    void BarResized(object s,SizeChangedEventArgs e){DrawBar();UpdatePlayhead();}
    void DrawBar()
    {
        if(info==null || info.Duration<=0 || CutBar.ActualWidth<1)return;
        CutBar.Children.Clear();double w=CutBar.ActualWidth,h=CutBar.ActualHeight;
        double X(double time)=>Math.Clamp(time/info.Duration*w,0,w);
        void Block(double left,double width,Brush color,double top=0,double? height=null)
        {
            var rect=new Rectangle{Width=Math.Max(0,width),Height=height??h,Fill=color,IsHitTestVisible=false};
            Canvas.SetLeft(rect,left);Canvas.SetTop(rect,top);CutBar.Children.Add(rect);
        }
        var selected=Tint(24,84,83);var teal=Tint(52,204,189);var cut=Tint(129,71,96);
        double left=X(Math.Min(a,b)),right=X(Math.Max(a,b));
        Block(left,right-left,selected);
        foreach(var r in cuts)Block(X(r.Start),X(r.End)-X(r.Start),cut);
        for(int i=1;i<10;i++)Block(w*i/10,1,Tint(172,191,204,85),h-5,5);
        Block(left,right-left,teal,0,1);Block(left,right-left,teal,h-1,1);
        Block(Math.Min(w-3,left),3,teal);Block(Math.Clamp(right-3,0,w),3,teal);
        if(right-left>=44){
            var first=new TextBlock{Text=a<=b?"A":"B",FontSize=10,FontWeight=FontWeights.Bold,Foreground=teal};
            var last=new TextBlock{Text=a<=b?"B":"A",FontSize=10,FontWeight=FontWeights.Bold,Foreground=teal};
            Canvas.SetLeft(first,left+7);Canvas.SetTop(first,7);Canvas.SetLeft(last,right-15);Canvas.SetTop(last,7);
            CutBar.Children.Add(first);CutBar.Children.Add(last);
        }
    }
    DragTarget Hit(double x)
    {
        if(info==null)return DragTarget.None;
        double factor=CutBar.ActualWidth/info.Duration;
        double da=Math.Abs(x-a*factor),db=Math.Abs(x-b*factor);
        return Math.Min(da,db)>10?DragTarget.Playhead:da<=db?DragTarget.A:DragTarget.B;
    }
    void RangeDown(object s,MouseButtonEventArgs e)
    {
        if(!ready)return;Pause();dragging=Hit(e.GetPosition(CutBar).X);CutBar.CaptureMouse();MoveRange(e.GetPosition(CutBar).X);e.Handled=true;
    }
    void RangeMove(object s,MouseEventArgs e)
    {
        double x=e.GetPosition(CutBar).X;
        if(dragging==DragTarget.None)CutBar.Cursor=Hit(x) is DragTarget.A or DragTarget.B?Cursors.SizeWE:Cursors.Hand;
        else if(e.LeftButton==MouseButtonState.Pressed)MoveRange(x);
        else EndDrag();
    }
    void RangeUp(object s,MouseButtonEventArgs e){if(dragging!=DragTarget.None){MoveRange(e.GetPosition(CutBar).X);EndDrag();}}
    void EndDrag(){dragging=DragTarget.None;CutBar.ReleaseMouseCapture();}
    void MoveRange(double x){
        if(info==null)return;double pos=Math.Clamp(x/Math.Max(1,CutBar.ActualWidth),0,1)*info.Duration;
        if(dragging==DragTarget.A)a=pos;
        if(dragging==DragTarget.B)b=pos;
        Seek(pos);if(dragging!=DragTarget.Playhead)RefreshCuts();
    }
}

