using System.Runtime.InteropServices;
namespace Moments;

internal sealed partial class AudioFeed
{
    sealed class AppInputs : IDisposable
    {
        sealed class Reader(AppRoot app,Endpoint endpoint){
            public AppRoot App=app;
            public readonly Endpoint Endpoint=endpoint;
            public readonly int[] Block=new int[960];
            public long LastSeen=Environment.TickCount64;
            public double Lag;
            public long CalibrateAt;
            public Alignment? Align;
        }
        sealed record Scan(List<AppRoot> Apps,Dictionary<int,Endpoint> New);
        readonly Mixer mixer=new();
        readonly AppAudioArchive archive;
        readonly CancellationToken cancel;
        readonly Dictionary<int,Reader> readers=[];
        readonly Dictionary<string,int[]> mix=new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<int,long> retryAfter=[];
        Task<Scan>? pending;
        long nextScan;int revision;
        public AppInputs(AppAudioArchive archive,CancellationToken cancel){
            this.archive=archive;this.cancel=cancel;
            try{Apply(DiscoverAndOpen(new(),[]),false);}
            catch{Dispose();throw;}
        }
        Scan DiscoverAndOpen(Dictionary<int,AppRoot> current,HashSet<int> deferred){
            var apps=Discover(mixer);var added=new Dictionary<int,Endpoint>();
            try{
                foreach(var app in apps){
                    cancel.ThrowIfCancellationRequested();
                    if((current.TryGetValue(app.Pid,out var old) && old.Birth==app.Birth && string.Equals(old.Path,app.Path,StringComparison.OrdinalIgnoreCase)) || deferred.Contains(app.Pid))continue;
                    try{added.Add(app.Pid,Endpoint.ForProcess(app.Pid,cancel));}
                    catch(Exception ex) when(ex is COMException or IOException or TimeoutException){archive.Warn(app.Path,ex);}
                }
                return new(apps,added);
            }catch{foreach(var e in added.Values)e.Dispose();throw;}
        }
        public void Start(){foreach(var r in readers.Values)r.Endpoint.Start();}
        void Apply(Scan scan,bool start){
            long now=Environment.TickCount64;
            foreach(var app in scan.Apps){
                if(readers.TryGetValue(app.Pid,out var stale) && (stale.App.Birth!=app.Birth || !string.Equals(stale.App.Path,app.Path,StringComparison.OrdinalIgnoreCase))){
                    stale.Endpoint.Dispose();readers.Remove(app.Pid);
                }
                if(scan.New.Remove(app.Pid,out var endpoint)){
                    if(readers.Remove(app.Pid,out var old))old.Endpoint.Dispose();
                    try{if(start)endpoint.Start();readers[app.Pid]=new(app,endpoint);}
                    catch(COMException ex){endpoint.Dispose();archive.Warn(app.Path,ex);}
                }
                if(readers.TryGetValue(app.Pid,out var r)){
                    if(r.App.Parent!=app.Parent){r.Lag=0;r.CalibrateAt=0;r.Align=null;}
                    r.App=app;r.LastSeen=now;retryAfter.Remove(app.Pid);
                }
                else if(retryAfter.GetValueOrDefault(app.Pid)<=now)retryAfter[app.Pid]=now+2000;
            }
            foreach(var pid in readers.Where(p=>now-p.Value.LastSeen>1500).Select(p=>p.Key).ToArray()){
                readers[pid].Endpoint.Dispose();readers.Remove(pid);retryAfter.Remove(pid);
            }
            foreach(string path in mix.Keys.Except(readers.Values.Select(r=>r.App.Path),StringComparer.OrdinalIgnoreCase).ToArray())mix.Remove(path);
        }
        public void Poll(long zero,long next)
        {
            long now=Environment.TickCount64;
            if(pending?.IsCompleted==true){
                var job=pending;pending=null;
                try{Apply(job.GetAwaiter().GetResult(),true);}
                catch(COMException ex){archive.Warn("Windows audio sessions",ex);}
            }
            if(pending==null && (now>=nextScan || revision!=mixer.Revision)){
                revision=mixer.Revision;nextScan=now+350;
                var known=readers.ToDictionary(p=>p.Key,p=>p.Value.App);
                var deferred=retryAfter.Where(p=>p.Value>now).Select(p=>p.Key).ToHashSet();
                pending=Task.Run(()=>DiscoverAndOpen(known,deferred));
            }
            foreach(var (pid,r) in readers.ToArray()){
                try{r.Endpoint.Poll(zero,next);}
                catch(COMException ex){r.Endpoint.Dispose();readers.Remove(pid);archive.Warn(r.App.Path,ex);retryAfter[pid]=now+1500;}
            }
        }
        int[] Bus(string name){
            if(!mix.TryGetValue(name,out var data))mix[name]=data=new int[960];return data;
        }
        public void Write(long next)
        {
            foreach(var data in mix.Values)Array.Clear(data);
            foreach(var r in readers.Values){
                if(next<r.CalibrateAt || !readers.TryGetValue(r.App.Parent,out var parent))continue;
                r.Align??=new Alignment();
                if(r.Align.TryMeasure(parent.Endpoint,r.Endpoint,next,r.Lag,out double lag)){r.Lag=lag;r.CalibrateAt=next+12000;}
                else r.CalibrateAt=next+480;
            }
            foreach(var reader in readers.Values){
                var block=reader.Block;
                double totalLag=0;var node=reader;
                for(int depth=0;depth<readers.Count && readers.TryGetValue(node.App.Parent,out var upstream);depth++){totalLag+=node.Lag;node=upstream;}
                if(totalLag==0)reader.Endpoint.Read(next,block);else reader.Endpoint.ReadAligned(next+totalLag,block);
                var own=Bus(reader.App.Path);
                // Process loopback includes children. Remove a separately captured subtree
                // from its nearest captured ancestor before summing instances of the same app.
                var parent=readers.TryGetValue(reader.App.Parent,out var ancestor)?Bus(ancestor.App.Path):null;
                for(int i=0;i<block.Length;i++){own[i]+=block[i];if(parent!=null)parent[i]-=block[i];}
            }
            foreach(var (path,pcm) in mix)archive.Write(path,next,pcm);
        }
        // Each virtual loopback client has its own packet-clock phase. Align the
        // child's independently captured PCM to its identical copy in the parent
        // before subtracting. Unrelated processes never take this path.
        sealed class Alignment
        {
            const int Radius=960,Window=2400;
            readonly int[] parentBlock=new int[Window*2], childBlock=new int[(Window+Radius*2)*2];
            public bool TryMeasure(Endpoint parent,Endpoint child,long at,double previous,out double lag)
            {
                lag=previous;parent.Read(at,parentBlock);child.Read(at-Radius,childBlock);
                int channel=0;long left=0,right=0;
                for(int i=0;i<Window;i+=4){int j=2*(Radius+i);left+=Math.Abs((int)childBlock[j]);right+=Math.Abs((int)childBlock[j+1]);}
                if(Math.Max(left,right)<Window)return false;if(right>left)channel=1;
                int old=(int)Math.Round(previous),bestIndex=old;
                double best=double.NegativeInfinity,prior=0,energy=0;
                for(int offset=-Radius;offset<=Radius;offset++){
                    int j=2*(Radius+offset)+channel;double dot=0,power=0;
                    for(int i=channel;i<Window*2;i+=10,j+=10){
                        double y=childBlock[j];dot+=parentBlock[i]*y;power+=y*y;
                    }
                    double score=2*dot-power;
                    if(offset==old)prior=score;
                    if(score>best){best=score;bestIndex=offset;energy=power;}
                }
                if(best<energy*.15){lag=previous;return false;}
                if(prior>=best-Math.Max(1,energy*.0001))bestIndex=old;
                double numerator=0,denominator=0;
                if(Math.Abs(bestIndex)<Radius){
                    int j=2*(Radius+bestIndex)+channel;
                    for(int i=channel;i<Window*2;i+=6,j+=6){
                        double slope=(childBlock[j+2]-childBlock[j-2])*.5;
                        numerator+=(parentBlock[i]-childBlock[j])*slope;denominator+=slope*slope;
                    }
                }
                lag=bestIndex+(denominator>0?Math.Clamp(numerator/denominator,-.5,.5):0);
                return true;
            }
        }
        public void Dispose(){
            if(pending!=null)try{foreach(var e in pending.GetAwaiter().GetResult().New.Values)e.Dispose();}catch(Exception ex){Home.Log(ex.Message);}
            foreach(var r in readers.Values)r.Endpoint.Dispose();
            mixer.Dispose();archive.Flush(close:true);
        }
    }
}
