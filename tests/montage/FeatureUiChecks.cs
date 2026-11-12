using System.Collections.Generic;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Net;
using System.Net.Http;
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
    static async Task Ready(Window w){
        for(int i=0;i<100;i++){if(Find<Button>(w,"CheckUpdateButton").IsEnabled)return;await Task.Delay(50);}
        throw new Exception("Update UI did not finish the request");
    }
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
        ClipEvents.Write(source,0,.3,[new("1","My kills",120,.01,.01),new("2","My kills",120,.02,.02),new("3","Double kill",120,.1,.1),new("4","Triple kill",120,.2,.2)]);
        var releases=new FakeReleases{Tag="v9.0.0",Delay=150};
        var installer=new FakeInstaller();bool restart=false;
        var window=new MainWindow(new UpdateService(new HttpClient(releases)),installer,()=>restart=true);window.Show();await Layout(window);
        Click(window,"LibraryNav");await Layout(window);
        Require(Cards(window).Count()==8,"List view lost recordings");
        Require(Cards(window).Any(c=>Text(c).Contains("Kill ×2")&&Text(c).Contains("Double kill")&&Text(c).Contains("Triple kill")),"list event labels missing");
        // Let real thumbnail generation finish before rendering the preview.
        await Thumbnails(window);await Layout(window);Shot(window,Path.Combine(reportDirectory,"library-list.png"));
        Click(window,"TilesViewButton");await Layout(window);
        Require(Cards(window).Count()==8 && Settings.Load().LibraryTiles,"tile switch/persistence");
        Require(Cards(window).Any(c=>Text(c).Contains("Kill ×2")&&Text(c).Contains("Triple kill")),"tile event labels missing");
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
        Require(Find<TextBlock>(window,"InstalledVersion").Text.Contains(UpdateService.Current.ToString()),"installed version label");
        Require(!Find<Button>(window,"DownloadUpdateButton").IsEnabled,"download enabled before checking");
        Click(window,"CheckUpdateButton");
        Require(!Find<Button>(window,"CheckUpdateButton").IsEnabled && Find<Button>(window,"CancelUpdateButton").Visibility==Visibility.Visible,"loading/cancel state");
        await Ready(window);Require(Find<Button>(window,"DownloadUpdateButton").IsEnabled,"new update did not enable download");
        using(var reservation=new GateReservation(window)){
            Click(window,"DownloadUpdateButton");Require(installer.Prepared==0,"update ignored busy capture gate");
        }
        var captureField=typeof(MainWindow).GetField("capture",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
        var dummy=new Capture(new Target(0,"test",new System.Drawing.Rectangle(0,0,100,100),false),Profile.All[0],false,false,true,5,"test");
        captureField.SetValue(window,dummy);Click(window,"DownloadUpdateButton");Require(installer.Prepared==0,"update interrupted replay capture");captureField.SetValue(window,null);Home.Clear(dummy.Folder);
        var editor=new Window{Owner=window};editor.Show();Click(window,"DownloadUpdateButton");Require(installer.Prepared==0,"update closed an open editor");editor.Close();
        Click(window,"DownloadUpdateButton");await Ready(window);
        Require(Find<Button>(window,"OpenUpdateButton").Visibility==Visibility.Visible && File.ReadAllBytes(Path.Combine(Home.Root,"Updates",releases.Name)).SequenceEqual(releases.Bytes),"verified ZIP/open folder state");
        Require(installer.Prepared==1 && !restart && Find<TextBlock>(window,"UpdateStatus").Text.Contains("installer test failure"),"installer failure closed Moments or hid error");
        Click(window,"CheckUpdateButton");await Ready(window);
        window.Width=850;window.Height=590;Find<Border>(window,"UpdatesCard").BringIntoView();await Layout(window);
        Shot(window,Path.Combine(reportDirectory,"updates-settings.png"));
        releases.Tag="v"+UpdateService.Current;Click(window,"CheckUpdateButton");await Ready(window);
        Require(!Find<Button>(window,"DownloadUpdateButton").IsEnabled && Find<TextBlock>(window,"UpdateStatus").Text.Contains("latest version"),"up-to-date state");
        releases.Status=HttpStatusCode.Forbidden;Click(window,"CheckUpdateButton");await Ready(window);
        Require(!Find<Button>(window,"DownloadUpdateButton").IsEnabled && Find<TextBlock>(window,"UpdateStatus").Text.Contains("limiting"),"network failure state");
        releases.Status=HttpStatusCode.OK;releases.Delay=5000;Click(window,"CheckUpdateButton");Click(window,"CancelUpdateButton");await Ready(window);
        Require(Find<TextBlock>(window,"UpdateStatus").Text.Contains("canceled"),"update cancellation state");
        releases.Delay=10;releases.Tag="v9.0.0";Click(window,"CheckUpdateButton");await Ready(window);
        installer.Fail=false;Click(window,"DownloadUpdateButton");
        for(int i=0;i<100 && !restart;i++)await Task.Delay(50);
        Require(restart && installer.Prepared==2 && installer.Launched==2,"one-click update did not prepare, hand off and request restart");
        Require(!Find<Button>(window,"DownloadUpdateButton").IsEnabled,"double install allowed after handoff");
        note("Updates UI: one-click download/prepare/restart, recording/replay/busy/editor guards, installation failure/retry, progress/cancel, current-version and network errors passed.");
        note("List/tiles: event type badges/counts, identical recordings, date groups, filter, empty state, minimum window size and persisted view. All 13 event settings, persistence and enable/disable binding passed.");
        // The surrounding test Application shuts down all windows after PASS.
    }
    static string Text(DependencyObject root){
        string result=root is TextBlock text?text.Text:"";
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)result+=" "+Text(VisualTreeHelper.GetChild(root,i));return result;
    }
    sealed class FakeInstaller:IUpdateInstaller{
        public int Prepared,Launched;public bool Fail=true;
        public Task<string> Prepare(string archive,Version version,System.Threading.CancellationToken token){Require(File.Exists(archive),"installer received missing ZIP");Prepared++;return Task.FromResult("test-plan");}
        public Task Launch(string plan,System.Threading.CancellationToken token){Launched++;return Fail?Task.FromException(new IOException("installer test failure")):Task.CompletedTask;}
    }
    sealed class GateReservation:IDisposable{
        readonly System.Threading.SemaphoreSlim gate;
        public GateReservation(MainWindow window){gate=(System.Threading.SemaphoreSlim)typeof(MainWindow).GetField("gate",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(window)!;Require(gate.Wait(0),"capture gate already busy");}
        public void Dispose()=>gate.Release();
    }
}
