using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms=System.Windows.Forms;
using RectI=System.Drawing.Rectangle;
namespace Moments;
public partial class MainWindow : Window
{
    readonly Settings cfg=Settings.Load();
    readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromSeconds(1)};
    readonly SemaphoreSlim gate=new(1,1);
    internal readonly EditingWorkspace Editing=new();
    readonly Forms.NotifyIcon tray=new();
    readonly EventWaitHandle showEvent=new(false,EventResetMode.AutoReset,@"Local\MomentsStudio_Show");
    RegisteredWaitHandle? waiter;
    readonly HashSet<int> failedGames=[];
    int mode,page,gamePid,scanTick,libraryGeneration;
    bool initialized,quitting,closed,hotkeyOk;
    Capture? capture; RectI? region;
    nint hwnd;
    readonly GameShortcutInput gameKeys=new();
    long lastReplayPress=-1000;
    bool replayQueued;
    static Brush BrushOf(string s)=>(Brush)new BrushConverter().ConvertFromString(s)!;
    public MainWindow():this(new UpdateService()){}
    internal MainWindow(UpdateService updates,IUpdateInstaller? installer=null,Action? restart=null)
    {
        updateService=updates;updateInstaller=installer??new UpdateInstaller();restartForUpdate=restart??Close;InitializeComponent();InitializeUpdates();
        Editing.LibraryChanged+=()=>{if(!quitting && page==1)LoadLibrary();};
        Editing.WindowsChanged+=()=>{if(!quitting)UpdateButtons(updateCancel!=null);};
        Style=(Style)FindResource(typeof(Window));
        Quality.ItemsSource=Profile.All;Quality.SelectedIndex=cfg.Quality;
        LengthBox.ItemsSource=new[]{"5 minutes","10 minutes"};LengthBox.SelectedIndex=cfg.Minutes==10?1:0;
        HotkeyButton.Content=cfg.Hotkey;
        Deactivated+=(_,_)=>StopListening();
        HotkeyButton.LostKeyboardFocus+=(_,_)=>StopListening();
        HotkeyButton.IsVisibleChanged+=(_,_)=>{if(!HotkeyButton.IsVisible)StopListening();};
        AutoBox.IsChecked=cfg.Auto;MicBox.IsChecked=cfg.Mic;SystemBox.IsChecked=cfg.SystemAudio;AppTracksBox.IsChecked=cfg.SeparateApps;RefreshGames();InitializeAutoSave();RefreshLibraryViewButtons();
        tray.Text="Moments · Capture Studio";tray.Icon=System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!)??System.Drawing.SystemIcons.Application;
        var menu=Theme.TrayMenu();menu.Items.Add("Open Moments",null,(_,_)=>Restore());
        menu.Items.Add("Save replay",null,async (_,_)=>await Guard(SaveCurrentReplay));
        menu.Items.Add("Stop recording",null,async (_,_)=>await Guard(async()=>{if(capture?.Replay==false)await StopCapture();}));
        menu.Items.Add("Exit",null,(_,_)=>Close());tray.ContextMenuStrip=menu;tray.DoubleClick+=(_,_)=>Restore();tray.Visible=true;
        timer.Tick+=Tick;SourceInitialized+=(_,_)=>{
            hwnd=new WindowInteropHelper(this).Handle;HwndSource.FromHwnd(hwnd)?.AddHook(Hook);
            RegisterKey();waiter=ThreadPool.RegisterWaitForSingleObject(showEvent,(_,_)=>Dispatcher.InvokeAsync(Restore),null,-1,false);
        };
        initialized=true;SetMode(0);ShowPage(0);UpdateState();timer.Start();
        StateChanged+=(_,_)=>{if(WindowState==WindowState.Minimized)Hide();};
        Closing+=OnClosing;
    }
    void Restore(){Show();WindowState=WindowState.Normal;Activate();}
    void HideClick(object s,RoutedEventArgs e)=>Hide();
    void Message(string msg,bool popup=false){Notice.Text=msg;Notice.ToolTip=msg;if(popup&&!IsActive){tray.BalloonTipTitle="Moments";tray.BalloonTipText=msg;tray.ShowBalloonTip(3000);}}
    async Task Guard(Func<Task> action)
    {
        if(quitting || installingUpdate || updateHandedOff || !await gate.WaitAsync(0)){return;}
        try{await action();}catch(Exception ex){Home.Log(ex.ToString());Message(ex.Message,true);}
        finally{gate.Release();UpdateState();}
    }
    async void OnClosing(object? s,CancelEventArgs e)
    {
        if(closed)return;e.Cancel=true;if(quitting)return;quitting=true;timer.Stop();lolTimer.Stop();lolLife.Cancel();await StopUpdates();Message("Finishing recording…");
        await Editing.CloseAll();
        await gate.WaitAsync();try{if(capture!=null)await StopCapture();}catch(Exception ex){Home.Log(ex.ToString());}
        finally{StopListening();Native.UnregisterHotKey(hwnd,hotkeyId);waiter?.Unregister(null);showEvent.Dispose();lolClient.Dispose();tray.Visible=false;tray.Dispose();closed=true;gate.Release();_=Dispatcher.BeginInvoke(()=>System.Windows.Application.Current.Shutdown());}
    }
    nint Hook(nint h,int msg,nint wp,nint lp,ref bool handled){
        if(msg==0x312 && hotkeyOk && wp==hotkeyId){handled=true;if(hotkeyListener==null)ReplayPress("WM_HOTKEY");}
        if(msg==0xFF && capture?.Replay==true && hotkeyOk && gameKeys.Matches(lp,activeShortcut) && hotkeyListener==null){
            Native.GetWindowThreadProcessId(GameShortcutInput.GetForegroundWindow(),out int pid);
            if(pid==gamePid)ReplayPress("Raw Input");
        }
        return 0;
    }
    async void ReplayPress(string source){
        long now=Environment.TickCount64;
        if(quitting || installingUpdate || updateHandedOff || replayQueued || now-lastReplayPress<180)return;
        lastReplayPress=now;replayQueued=true;var session=capture;
        await gate.WaitAsync();
        try{if(!quitting && ReferenceEquals(session,capture)){Home.Log("Replay shortcut: "+source);await SaveCurrentReplay();}}
        catch(Exception ex){Home.Log(ex.ToString());Message(ex.Message,true);}
        finally{gate.Release();replayQueued=false;UpdateState();}
    }
    async void Tick(object? s,EventArgs e)
    {
        UpdateState();
        if(quitting || installingUpdate || updateHandedOff || gate.CurrentCount==0)return;
        await Guard(async()=>{
            if(capture!=null){
                string? reason=capture.Failure;
                if(Home.Free<128_000_000)reason="Recording stopped. Less than 128 MB of disk space remains.";
                if(reason!=null){
                    if(gamePid>0)failedGames.Add(gamePid);
                    await StopCapture();Message(reason,true);return;
                }
                if(capture.Replay){
                    if(!cfg.Auto || !Native.IsWindow(capture.Source.Handle) || !IsSelectedGame(gamePid)){await StopCapture();return;}
                    await SaveAutoReplay();
                    capture.Prune();
                }
            }else if(cfg.Auto && cfg.Games.Count>0 && ++scanTick%2==0){
                var match=FindGame();
                if(match!=null){
                    var (target,id,name)=match.Value;Message("Starting replay buffer…");
                    try{await Begin(target,true,name);gamePid=id;Message("Replay buffer active · "+name,true);}
                    catch{failedGames.Add(id);throw;}
                }
            }
        });
    }
    bool IsSelectedGame(int pid)=>cfg.Games.Contains(Native.ImagePath(pid)??"",StringComparer.OrdinalIgnoreCase);
    (Target,int,string)? FindGame()
    {
        foreach(var path in cfg.Games){
            var name=Path.GetFileNameWithoutExtension(path);
            var ps=Process.GetProcessesByName(name);
            try{foreach(var p in ps){
                if(failedGames.Contains(p.Id))continue;
                try{if(!string.Equals(Native.ImagePath(p.Id),path,StringComparison.OrdinalIgnoreCase))continue;
                    var target=Native.Windows(p.Id).FirstOrDefault();if(target!=null)return(target,p.Id,name);
                }catch(Exception ex)when(ex is Win32Exception or InvalidOperationException){}
            }}finally{foreach(var p in ps)p.Dispose();}
        }return null;
    }
    async Task Begin(Target target,bool replay,string name)
    {
        var session=new Capture(target,Profile.All[cfg.Quality],cfg.SystemAudio,cfg.Mic,replay,cfg.Minutes,name,cfg.SeparateApps);
        Message("Preparing video and audio…");RecordButton.IsEnabled=false;
        await session.Start();capture=session;ResetAutoSave();if(replay)gameKeys.Start(hwnd);
    }
    async Task StopCapture()
    {
        var old=capture;if(old==null)return;
        await SaveAutoReplay(force:true);
        bool keep=old.Replay && autoReplay.Pending;
        gameKeys.Stop();capture=null;gamePid=0;ResetAutoSave();
        Message("Finishing recording…");string? saved=await old.Stop(keep);
        if(keep){Home.Log("Unsaved auto-replay retained in "+old.Folder);Message("Auto-save failed. Replay source retained in "+old.Folder,true);return;}
        Message(saved==null?"Replay buffer stopped":"Recording saved · "+Path.GetFileName(saved)+old.AudioWarning,saved!=null);
        if(page==1)LoadLibrary();
    }
    async Task SaveCurrentReplay()
    {
        if(capture?.Replay!=true){Message("No replay buffer is active.",true);return;}
        Message("Saving replay…");string file=await capture.SaveReplay();Message("Replay saved · "+Path.GetFileName(file)+capture.AudioWarning,true);if(page==1)LoadLibrary();
    }
    async void RecordClick(object s,RoutedEventArgs e)=>await Guard(async()=>{
        if(capture?.Replay==false){await StopCapture();return;}
        if(Targets.SelectedItem is not Target target)throw new IOException("Choose an available source.");
        if(mode==2 && region==null)throw new IOException("Select a region on the display first.");
        if(mode==2)target=target with{Region=region};
        if(capture!=null)await StopCapture();
        await Begin(target,false,mode switch{1=>target.Title,2=>"Region",_=>"Screen"});Message("Recording. Stop here or from the tray icon.");
    });
    async void ReplayClick(object s,RoutedEventArgs e)=>await Guard(SaveCurrentReplay);
    void UpdateState()
    {
        if(!initialized)return;
        UpdateButtons(updateCancel!=null);
        var c=capture;DiskLabel.Text=$"Drive {Path.GetPathRoot(Home.Root)!.TrimEnd('\\')}   ·   {Home.Free/1073741824d:0.0} GiB";
        StateText.Text=c==null?"●  Idle":(c.Replay?"●  Buffer   ":"●  Recording   ")+Home.Time(c.Clock.Elapsed.TotalSeconds);
        RecordButton.Content=c==null?"●   Start recording":c.Replay?"Start manual recording":"■   Stop";
        RecordButton.IsEnabled=gate.CurrentCount!=0 || c!=null;
        Targets.IsEnabled=ModePanel.IsEnabled=RegionRow.IsEnabled=RefreshTargets.IsEnabled=c?.Replay!=false;
        SaveReplayButton.IsEnabled=c?.Replay==true;
        ReplayTitle.Text=c?.Replay==true?"Buffer active · "+c.Label:cfg.Games.Count==0?"Add your first game":cfg.Auto?"Waiting for a game":"Auto capture is off";
        RefreshAutoSaveStatus();
        ReplayHint.Text=$"Last {c?.Minutes??cfg.Minutes} min   ·   "+(hotkeyOk?cfg.Hotkey:"shortcut unavailable")+$"   ·   Games: {cfg.Games.Count}";
    }
    void NavClick(object s,RoutedEventArgs e)=>ShowPage(int.Parse((string)((Button)s).Tag));
    void GamesClick(object s,RoutedEventArgs e)=>ShowPage(2);
    void AssemblyClick(object s,RoutedEventArgs e){
        if(quitting || installingUpdate || updateHandedOff)return;
        try{Editing.OpenTimeline();}catch(Exception ex){Message(ex.Message);}
    }
    void ShowPage(int n){
        page=n;PageTitle.Text=new[]{"Capture","Library","Settings"}[n];
        var pages=new[]{CapturePage,LibraryPage,SettingsPage};var nav=new[]{CaptureNav,LibraryNav,SettingsNav};
        for(int i=0;i<3;i++){pages[i].Visibility=i==n?Visibility.Visible:Visibility.Collapsed;nav[i].Background=BrushOf(i==n?"#2B3932":"Transparent");nav[i].Foreground=BrushOf(i==n?"#B7F59B":"#CBD2D8");}
        if(n==1)LoadLibrary();
    }
    void ModeClick(object s,RoutedEventArgs e)=>SetMode(int.Parse((string)((Button)s).Tag));
    void SetMode(int n)
    {
        mode=n;region=null;SourceHeading.Text=new[]{"Screen recording","Window recording","Region recording"}[n];
        SourceDescription.Text=new[]{"Record the entire display, including the cursor.","Record one window, even when another window covers it.","Drag a rectangle on the selected display."}[n];
        var buttons=new[]{ScreenButton,WindowButton,RegionButton};for(int i=0;i<3;i++)buttons[i].Background=BrushOf(i==n?"#405545":"#29313A");
        RegionRow.Visibility=n==2?Visibility.Visible:Visibility.Collapsed;RefreshSources();
    }
    void RefreshSources(){var old=(Targets.SelectedItem as Target)?.Handle;var items=mode==1?Native.Windows():Native.Monitors();Targets.ItemsSource=items;Targets.SelectedItem=items.FirstOrDefault(t=>t.Handle==old)??items.FirstOrDefault();}
    void RefreshClick(object s,RoutedEventArgs e)=>RefreshSources();
    void TargetChanged(object s,SelectionChangedEventArgs e){region=null;if(RegionLabel!=null)RegionLabel.Text="No region selected";}
    void RegionClick(object s,RoutedEventArgs e){
        if(Targets.SelectedItem is not Target t)return;Hide();
        try{using var overlay=new RegionPicker(t.Bounds);if(overlay.ShowDialog()==Forms.DialogResult.OK){region=overlay.Selection;RegionLabel.Text=$"{region.Value.Width} × {region.Value.Height} px";}}
        finally{Restore();}
    }
    void SettingsChanged(object s,RoutedEventArgs e)
    {
        if(!initialized)return;cfg.Quality=Quality.SelectedIndex;cfg.Minutes=LengthBox.SelectedIndex==1?10:5;
        cfg.Auto=AutoBox.IsChecked==true;cfg.SystemAudio=SystemBox.IsChecked==true;cfg.Mic=MicBox.IsChecked==true;cfg.SeparateApps=AppTracksBox.IsChecked==true;
        cfg.Save();failedGames.Clear();UpdateState();
        if(capture!=null)Message("The profile, replay length and audio tracks apply to the next capture.");
    }
    void RefreshGames(){GameList.ItemsSource=null;GameList.ItemsSource=cfg.Games;}
    void AddGame(object s,RoutedEventArgs e){
        var dlg=new GamePicker{Owner=this};
        if(dlg.ShowDialog()!=true || dlg.SelectedFile is not string file)return;
        if(!cfg.Games.Contains(file,StringComparer.OrdinalIgnoreCase))cfg.Games.Add(file);
        cfg.Save();failedGames.Clear();RefreshGames();UpdateState();Message("Game added · "+Path.GetFileNameWithoutExtension(file));
    }
    void RemoveGame(object s,RoutedEventArgs e){if(GameList.SelectedItem is string file){cfg.Games.Remove(file);cfg.Save();RefreshGames();UpdateState();}}
    void OpenFolder(object s,RoutedEventArgs e)=>Process.Start(new ProcessStartInfo(Home.Videos){UseShellExecute=true});
    void SearchChanged(object s,TextChangedEventArgs e){if(initialized && page==1)LoadLibrary();}

}
internal sealed class RegionPicker : Forms.Form
{
    System.Drawing.Point anchor;RectI box;bool dragging;
    public RectI Selection=>new(Left+box.Left,Top+box.Top,box.Width,box.Height);
    public RegionPicker(RectI screen){
        FormBorderStyle=Forms.FormBorderStyle.None;StartPosition=Forms.FormStartPosition.Manual;Bounds=screen;TopMost=true;ShowInTaskbar=false;
        DoubleBuffered=true;BackColor=System.Drawing.Color.Black;Opacity=.48;Cursor=Forms.Cursors.Cross;KeyPreview=true;
        KeyDown+=(_,e)=>{if(e.KeyCode==Forms.Keys.Escape)DialogResult=Forms.DialogResult.Cancel;};
        MouseDown+=(_,e)=>{if(e.Button!=Forms.MouseButtons.Left)return;anchor=e.Location;dragging=true;Capture=true;};
        MouseMove+=(_,e)=>{if(!dragging)return;var end=new System.Drawing.Point(Math.Clamp(e.X,0,ClientSize.Width),Math.Clamp(e.Y,0,ClientSize.Height));box=RectI.FromLTRB(Math.Min(anchor.X,end.X),Math.Min(anchor.Y,end.Y),Math.Max(anchor.X,end.X),Math.Max(anchor.Y,end.Y));Invalidate();};
        MouseUp+=(_,e)=>{if(!dragging)return;dragging=false;Capture=false;if(box.Width>=16&&box.Height>=16)DialogResult=Forms.DialogResult.OK;};
    }
    protected override void OnPaint(Forms.PaintEventArgs e){
        base.OnPaint(e);using var pen=new System.Drawing.Pen(System.Drawing.Color.White,3);e.Graphics.DrawRectangle(pen,box);
        string hint=dragging?$"{box.Width} × {box.Height}":"Drag to select a region   ·   Esc to cancel";
        using var font=new System.Drawing.Font("Segoe UI",16);e.Graphics.DrawString(hint,font,System.Drawing.Brushes.White,20,20);
    }
}


