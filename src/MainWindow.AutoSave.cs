using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
namespace Moments;

public partial class MainWindow
{
    readonly LolClient lolClient=new();
    readonly CancellationTokenSource lolLife=new();
    readonly LolEventTracker lolTracker=new();
    readonly AutoReplay autoReplay=new();
    readonly List<ReplayEvent> recentEvents=[];
    readonly DispatcherTimer lolTimer=new(){Interval=TimeSpan.FromMilliseconds(500)};
    bool lolBusy;int lolEpoch;
    string lolStatus="Waiting for a League of Legends replay buffer";
    void InitializeAutoSave(){
        LolAutoSaveBox.IsChecked=cfg.LolAutoSave.Enabled;
        foreach(var (kind,label) in LolAutoSaveOptions.Choices){
            var box=new CheckBox{Content=label,Tag=kind,MinWidth=170,IsChecked=(cfg.LolAutoSave.Events&kind)!=0,Margin=new Thickness(0,5,12,5)};
            box.Click+=AutoSaveChanged;LolEventOptions.Children.Add(box);
        }
        lolTimer.Tick+=PollLol;lolTimer.Start();
    }
    bool IsLolCapture=>capture?.Replay==true && string.Equals(Path.GetFileName(Native.ImagePath(gamePid)),"League of Legends.exe",StringComparison.OrdinalIgnoreCase);
    void ResetAutoSave(){lolEpoch++;lolTracker.Reset();autoReplay.Reset();recentEvents.Clear();lolStatus="Waiting for a League of Legends replay buffer";}
    void AutoSaveChanged(object s,RoutedEventArgs e){
        if(!initialized)return;
        cfg.LolAutoSave.Enabled=LolAutoSaveBox.IsChecked==true;
        cfg.LolAutoSave.Events=LolEventOptions.Children.OfType<CheckBox>().Where(b=>b.IsChecked==true).Aggregate(LolEventKind.None,(mask,b)=>mask|(LolEventKind)b.Tag);
        cfg.Save();ResetAutoSave();RefreshAutoSaveStatus();
    }
    async void PollLol(object? sender,EventArgs e){
        if(quitting || lolBusy || !cfg.LolAutoSave.Enabled || cfg.LolAutoSave.Events==LolEventKind.None || !IsLolCapture)return;
        var session=capture!;int epoch=lolEpoch;lolBusy=true;
        try{
            var snapshot=await lolClient.Read(lolLife.Token);
            if(quitting || epoch!=lolEpoch || !ReferenceEquals(capture,session))return;
            var events=lolTracker.Update(snapshot,cfg.LolAutoSave.Events);
            double now=session.Clock.Elapsed.TotalSeconds;
            foreach(var item in events){
                double position=now-(snapshot.GameTime-item.GameTime);
                // Reconnected APIs can return a backlog; never save an event outside this recording/buffer.
                if(position<0 || now-position>session.Minutes*60-30)continue;
                var marker=new ReplayEvent(item.Id,item.Label,item.GameTime,position,now);
                recentEvents.Add(marker);autoReplay.Add(marker);
            }
            recentEvents.RemoveAll(item=>item.RecordingTime<now-session.Minutes*60-10);
            lolStatus="LoL connected · "+snapshot.Player;
        }catch(Exception ex) when(ex is HttpRequestException or OperationCanceledException or JsonException or InvalidDataException){
            if(epoch==lolEpoch && ReferenceEquals(capture,session))lolStatus="Waiting for the LoL live game connection";
        }catch(Exception ex){Home.Log("LoL auto-save: "+ex);lolStatus="LoL events unavailable · see Moments.log";}
        finally{lolBusy=false;if(!quitting)RefreshAutoSaveStatus();}
    }
    void RefreshAutoSaveStatus(){
        if(AutoSaveStatus==null || LolSettingsStatus==null)return;
        string text=!cfg.LolAutoSave.Enabled?"LoL auto-save is off":cfg.LolAutoSave.Events==LolEventKind.None?"LoL auto-save: select at least one event":
            autoReplay.Pending && capture!=null?$"LoL auto-save in {Math.Max(0,Math.Ceiling(autoReplay.DueAt(capture.Minutes)-capture.Clock.Elapsed.TotalSeconds))} s · last {capture.Minutes} min":lolStatus;
        AutoSaveStatus.Text=LolSettingsStatus.Text=text;
    }
    // Called only while holding the existing capture gate: manual/automatic saves,
    // segment pruning, and shutdown must never use the same temporary files concurrently.
    async Task SaveAutoReplay(bool force=false){
        var session=capture;
        if(session?.Replay!=true || !autoReplay.Pending || (!force && !autoReplay.Due(session.Clock.Elapsed.TotalSeconds,session.Minutes)))return;
        var batch=autoReplay.Snapshot();
        var markers=recentEvents.ToArray();
        Message("Saving LoL replay…");
        try{
            var saved=await session.SaveReplayDetails("LoL auto · "+batch[^1].Label);
            // A metadata failure must not cause duplicate video exports on the next tick.
            autoReplay.Saved(batch);
            try{ClipEvents.Write(saved.File,saved.Start,saved.Duration,markers);}
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){Home.Log("Auto-clip markers: "+ex.Message);}
            Message("LoL replay saved · "+Path.GetFileName(saved.File),true);if(page==1)LoadLibrary();
        }catch(Exception ex){
            autoReplay.Failed(session.Clock.Elapsed.TotalSeconds);Home.Log("LoL replay save: "+ex);
            Message("LoL auto-save failed; retrying in 5 s. "+ex.Message,true);
        }
        RefreshAutoSaveStatus();
    }
}
