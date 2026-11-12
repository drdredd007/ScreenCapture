using System.Collections.Generic;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
namespace Moments;
internal static class FeatureUiChecks
{
    static void Require(bool ok,string message){if(!ok)throw new Exception(message);}
    static T Find<T>(Window w,string name) where T:class=>(T)w.FindName(name);
    static void Click(Window w,string name)=>Find<Button>(w,name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    static async Task Layout(Window w){await w.Dispatcher.InvokeAsync(()=>w.UpdateLayout(),DispatcherPriority.ApplicationIdle);await Task.Delay(100);}
    static IEnumerable<Button> Cards(Window w)=>Find<StackPanel>(w,"ClipList").Children.OfType<Panel>().SelectMany(p=>p.Children.OfType<Button>());
    static IEnumerable<Image> Images(DependencyObject root){
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){
            var child=VisualTreeHelper.GetChild(root,i);if(child is Image image)yield return image;
            foreach(var nested in Images(child))yield return nested;
        }
    }
    static async Task Thumbnails(Window window){
        for(int i=0;i<300;i++){
            var images=Images(Find<StackPanel>(window,"ClipList")).ToArray();
            if(images.Length==8 && images.All(image=>image.Source!=null))return;
            await Task.Delay(100);
        }
        throw new Exception("Library thumbnails did not finish loading");
    }
    static void Shot(Window w,string file){
        w.UpdateLayout();var bmp=new RenderTargetBitmap((int)Math.Ceiling(w.ActualWidth),(int)Math.Ceiling(w.ActualHeight),96,96,PixelFormats.Pbgra32);
        bmp.Render(w);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bmp));using var output=File.Create(file);png.Save(output);
    }
    public static async Task Run(Action<string> note,string reportDirectory){
        new Settings{Auto=false,LibraryTiles=false}.Save();
        string source=Path.Combine(Home.Videos,"sample 01.mp4");
        await Proc.Run("ffmpeg",["-v","error","-y","-f","lavfi","-i","testsrc2=size=640x360:rate=10","-t","0.3","-c:v","libx264","-preset","ultrafast",source]);
        for(int i=2;i<=8;i++)File.Copy(source,Path.Combine(Home.Videos,$"sample {i:00} — a longer recording name for layout.mp4"));
        File.SetCreationTime(source,DateTime.Now.AddDays(-1));
        ClipEvents.Write(source,0,.3,[new("1","Pentakill",120,.1,.1)]);
        var window=new MainWindow();window.Show();await Layout(window);
        Click(window,"LibraryNav");await Layout(window);
        Require(Cards(window).Count()==8,"List view lost recordings");
        // Let real thumbnail generation finish before rendering the preview.
        await Thumbnails(window);await Layout(window);Shot(window,Path.Combine(reportDirectory,"library-list.png"));
        Click(window,"TilesViewButton");await Layout(window);
        Require(Cards(window).Count()==8 && Settings.Load().LibraryTiles,"tile switch/persistence");
        Require(Find<StackPanel>(window,"ClipList").Children.OfType<WrapPanel>().Count()==2,"tile date grouping");
        await Thumbnails(window);Shot(window,Path.Combine(reportDirectory,"library-tiles.png"));
        window.Width=850;window.Height=590;await Layout(window);
        foreach(var wrap in Find<StackPanel>(window,"ClipList").Children.OfType<WrapPanel>())foreach(var card in wrap.Children.OfType<Button>())
            Require(card.ActualWidth<=wrap.ActualWidth && card.ActualHeight>100,"tile exceeds viewport or preview missing");
        Shot(window,Path.Combine(reportDirectory,"library-tiles-compact.png"));
        Find<TextBox>(window,"Search").Text="sample 01";await Layout(window);Require(Cards(window).Count()==1,"tile search differs from list search");
        Click(window,"ListViewButton");await Layout(window);Require(Cards(window).Count()==1 && !Settings.Load().LibraryTiles,"list switch lost filter or persistence");
        Find<TextBox>(window,"Search").Text="does-not-exist";await Layout(window);Require(Cards(window).Count()==0,"empty search");
        window.Width=1090;window.Height=750;Click(window,"SettingsNav");await Layout(window);
        var options=Find<WrapPanel>(window,"LolEventOptions").Children.OfType<CheckBox>().ToArray();
        Require(options.Length==13,"missing LoL event choices");
        var death=options.Single(b=>(LolEventKind)b.Tag==LolEventKind.Death);death.IsChecked=true;death.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
        Require((Settings.Load().LolAutoSave.Events&LolEventKind.Death)!=0,"event choice not persisted");
        var enabled=Find<CheckBox>(window,"LolAutoSaveBox");enabled.IsChecked=false;enabled.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));await Layout(window);
        Require(!Settings.Load().LolAutoSave.Enabled && !Find<WrapPanel>(window,"LolEventOptions").IsEnabled,"auto-save disable/state binding");
        enabled.IsChecked=true;enabled.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));await Layout(window);
        Shot(window,Path.Combine(reportDirectory,"lol-autosave-settings.png"));
        note("List/tiles: identical recordings, date groups, filter, empty state, minimum window size and persisted view. All 13 event settings, persistence and enable/disable binding passed.");
        // The surrounding test Application shuts down all windows after PASS.
    }
}
