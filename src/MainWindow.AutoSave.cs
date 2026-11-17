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
    void ResetAutoSave(){lolEpoch++;lolTracker.Reset();lolStatus="Waiting for a League of Legends replay buffer";}
    void AutoSaveChanged(object s,RoutedEventArgs e){
        if(!initialized)return;
        bool wasEnabled=cfg.LolAutoSave.Enabled;
        cfg.LolAutoSave.Enabled=LolAutoSaveBox.IsChecked==true;
        cfg.LolAutoSave.Events=LolEventOptions.Children.OfType<CheckBox>().Where(b=>b.IsChecked==true).Aggregate(LolEventKind.None,(mask,b)=>mask|(LolEventKind)b.Tag);
        cfg.Save();if(!wasEnabled && cfg.LolAutoSave.Enabled && capture?.Highlights.Pending!=true)ResetAutoSave();RefreshAutoSaveStatus();
    }
    async void PollLol(object? sender,EventArgs e){
        if(quitting || lolBusy || !IsLolCapture || (!cfg.LolAutoSave.Enabled && !capture!.Highlights.Pending))return;
        var session=capture!;int epoch=lolEpoch;lolBusy=true;
        try{
            var snapshot=await lolClient.Read(lolLife.Token);
            if(quitting || epoch!=lolEpoch || !ReferenceEquals(capture,session))return;
            var events=lolTracker.Update(snapshot,cfg.LolAutoSave.Enabled?cfg.LolAutoSave.Events:LolEventKind.None);
            double now=session.Clock.Elapsed.TotalSeconds;
            var markers=new List<ReplayEvent>();
            foreach(var item in events){
                double position=now-(snapshot.GameTime-item.GameTime);
                // Reconnected APIs can return a backlog; never save an event outside this recording/buffer.
                if(position<0 || now-position>session.Minutes*60-30)continue;
                var marker=new ReplayEvent(item.Id,item.Label,item.GameTime,position,now);
                markers.Add(marker);
            }
            session.Highlights.Observe(markers,lolTracker.NewMatch,snapshot.GameOver,now-snapshot.GameTime,now);
            lolStatus="LoL connected · "+snapshot.Player;
        }catch(Exception ex) when(ex is HttpRequestException or OperationCanceledException or JsonException or InvalidDataException){
            if(epoch==lolEpoch && ReferenceEquals(capture,session))lolStatus="Waiting for the LoL live game connection";
        }catch(Exception ex){Home.Log("LoL auto-save: "+ex);lolStatus="LoL events unavailable · see Moments.log";}
        finally{lolBusy=false;if(!quitting)RefreshAutoSaveStatus();}
    }
    void RefreshAutoSaveStatus(){
        if(AutoSaveStatus==null || LolSettingsStatus==null)return;
        string text=!cfg.LolAutoSave.Enabled?"LoL auto-save is off":cfg.LolAutoSave.Events==LolEventKind.None?"LoL auto-save: select at least one event":
            capture?.Highlights.EventCount>0?$"Match highlights · {capture.Highlights.EventCount} events collected":lolStatus;
        AutoSaveStatus.Text=LolSettingsStatus.Text=text;
    }
}
