using System.Windows;
namespace Moments;
public partial class MainWindow
{
    readonly UpdateService updateService;
    CancellationTokenSource? updateCancel;
    Task updateWork=Task.CompletedTask;
    UpdateRelease? availableUpdate;
    string? downloadedUpdate;
    void InitializeUpdates(){InstalledVersion.Text="Installed version  "+UpdateService.Current;}
    void UpdateButtons(bool busy){
        CheckUpdateButton.IsEnabled=!busy;
        DownloadUpdateButton.IsEnabled=!busy && availableUpdate?.Version>UpdateService.Current;
        CancelUpdateButton.Visibility=busy?Visibility.Visible:Visibility.Collapsed;
        OpenUpdateButton.Visibility=downloadedUpdate!=null?Visibility.Visible:Visibility.Collapsed;
        UpdateReleaseButton.IsEnabled=availableUpdate!=null;
        UpdateProgress.Visibility=busy?Visibility.Visible:Visibility.Collapsed;
    }
    async void CheckUpdateClick(object sender,RoutedEventArgs e){
        if(updateCancel!=null || quitting)return;
        updateCancel=new();UpdateButtons(true);UpdateProgress.IsIndeterminate=true;UpdateStatus.Text="Checking GitHub releases…";
        updateWork=CheckUpdate();await updateWork;
    }
    async Task CheckUpdate(){
        try{
            availableUpdate=await updateService.Check(updateCancel!.Token);
            UpdateStatus.Text=availableUpdate.Version>UpdateService.Current?$"Version {availableUpdate.Version} is available.":"You have the latest version.";
        }catch(OperationCanceledException){UpdateStatus.Text=updateCancel!.IsCancellationRequested?"Update check canceled.":"GitHub did not respond in time. Try again.";}
        catch(Exception ex){availableUpdate=null;UpdateStatus.Text="Could not check for updates. "+ex.Message;Home.Log("Update check: "+ex.Message);}
        finally{updateCancel!.Dispose();updateCancel=null;UpdateButtons(false);}
    }
    async void DownloadUpdateClick(object sender,RoutedEventArgs e){
        if(updateCancel!=null || availableUpdate==null || availableUpdate.Version<=UpdateService.Current || quitting)return;
        updateCancel=new();UpdateButtons(true);UpdateProgress.IsIndeterminate=false;UpdateProgress.Value=0;
        updateWork=DownloadUpdate(availableUpdate);await updateWork;
    }
    async Task DownloadUpdate(UpdateRelease release){
        try{
            var active=updateCancel;
            var progress=new Progress<double>(value=>{if(!ReferenceEquals(updateCancel,active) || quitting)return;UpdateProgress.Value=value;UpdateStatus.Text=$"Downloading {release.Version} · {value:0}%";});
            downloadedUpdate=await updateService.Download(release,Path.Combine(Home.Root,"Updates"),progress,updateCancel!.Token);
            UpdateStatus.Text=$"Version {release.Version} downloaded and verified. Open the folder to install it.";
        }catch(OperationCanceledException){UpdateStatus.Text=updateCancel!.IsCancellationRequested?"Download canceled.":"Download timed out. Try again.";}
        catch(Exception ex){UpdateStatus.Text="Could not download the update. "+ex.Message;Home.Log("Update download: "+ex.Message);}
        finally{updateCancel!.Dispose();updateCancel=null;UpdateButtons(false);}
    }
    void CancelUpdateClick(object sender,RoutedEventArgs e)=>updateCancel?.Cancel();
    void OpenUpdateClick(object sender,RoutedEventArgs e){
        try{if(downloadedUpdate!=null)Process.Start(new ProcessStartInfo(Path.GetDirectoryName(downloadedUpdate)!){UseShellExecute=true});}
        catch(Exception ex){UpdateStatus.Text=ex.Message;}
    }
    void UpdateReleaseClick(object sender,RoutedEventArgs e){
        try{if(availableUpdate!=null)Process.Start(new ProcessStartInfo(availableUpdate.Page.AbsoluteUri){UseShellExecute=true});}
        catch(Exception ex){UpdateStatus.Text=ex.Message;}
    }
    async Task StopUpdates(){updateCancel?.Cancel();await updateWork;updateService.Dispose();}
}
