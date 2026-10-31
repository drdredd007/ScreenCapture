using System.Windows;
namespace Moments;
public partial class App : Application
{
    Mutex? singleton;
    protected override async void OnStartup(StartupEventArgs e)
    {
        var english=System.Globalization.CultureInfo.GetCultureInfo("en-US");
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture=english;
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture=english;
        base.OnStartup(e);
        try{
            Home.Init();Theme.Install();
            if(e.Args.Contains("--self-test")){await Verification.Run();Shutdown(0);return;}
            singleton=new Mutex(true,@"Local\MomentsStudio_FMoments",out bool first);
            if(!first){using var signal=EventWaitHandle.OpenExisting(@"Local\MomentsStudio_Show");signal.Set();Shutdown();return;}
            foreach(string dir in Directory.GetDirectories(Home.Work)){
                var name=Path.GetFileName(dir);if(name.StartsWith("take_")){
                    var raw=Path.Combine(dir,"take.mkv");
                    if(File.Exists(raw)&&new FileInfo(raw).Length>0){
                        if(Directory.Exists(AppAudioArchive.DirectoryFor(dir))){
                            try{await AppAudioArchive.Recover(dir,raw);}
                            catch(Exception ex){Home.Log("Recovery deferred for "+dir+": "+ex.Message);continue;}
                        }else File.Move(raw,Home.Unique(Home.Videos,"Recovered recording","mkv"));
                    }
                }
                if(new[]{"take_","ring_","edit_","view_"}.Any(name.StartsWith))Home.Clear(dir);
            }
            var window=new MainWindow();MainWindow=window;window.Show();
        }catch(Exception ex){Home.Log(ex.ToString());if(!e.Args.Contains("--self-test"))MessageBox.Show(ex.Message,"Moments");Shutdown(1);}
    }
    protected override void OnExit(ExitEventArgs e){singleton?.Dispose();base.OnExit(e);}
}
