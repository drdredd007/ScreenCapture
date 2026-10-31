using System.ComponentModel;
using System.Runtime.InteropServices;
namespace Moments;

[ComVisible(true),Guid("41D949AB-9862-444A-80F6-C261334DA5EB"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IAudioActivationCallback { [PreserveSig] int ActivateCompleted(IAsyncAudioActivation operation); }
[ComImport,Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IAsyncAudioActivation {void GetActivateResult(out int result,[MarshalAs(UnmanagedType.IUnknown)]out object client);}
[ComVisible(true),Guid("641DD20B-4D41-49CC-ABA3-174B9477BB08"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IAudioSessionCreated { [PreserveSig] int OnSessionCreated(nint session); }

internal sealed partial class AudioFeed
{
    [StructLayout(LayoutKind.Sequential)] struct ActivationParams {public int Type,Pid,Mode;}
    [StructLayout(LayoutKind.Sequential)] struct BlobVariant {public ushort Type,Reserved1,Reserved2,Reserved3;public uint Size;public nint Data;}
    [DllImport("mmdevapi.dll",CharSet=CharSet.Unicode,ExactSpelling=true)]
    static extern int ActivateAudioInterfaceAsync(string device,ref Guid iid,ref BlobVariant parameters,IAudioActivationCallback handler,out IAsyncAudioActivation operation);

    [ComVisible(true),ClassInterface(ClassInterfaceType.None)]
    sealed class Activated : IAudioActivationCallback
    {
        public readonly TaskCompletionSource<object> Result=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int ActivateCompleted(IAsyncAudioActivation operation){
            try{operation.GetActivateResult(out int hr,out object client);Marshal.ThrowExceptionForHR(hr);Result.TrySetResult(client);}
            catch(Exception ex){Result.TrySetException(ex);}
            return 0;
        }
    }
    static IAudioClient ActivateProcess(int pid,CancellationToken cancel)
    {
        var parameters=new ActivationParams{Type=1,Pid=pid,Mode=0};
        nint data=Marshal.AllocHGlobal(Marshal.SizeOf<ActivationParams>());
        try{
            Marshal.StructureToPtr(parameters,data,false);
            var blob=new BlobVariant{Type=65,Size=(uint)Marshal.SizeOf<ActivationParams>(),Data=data};
            var iid=typeof(IAudioClient).GUID;var callback=new Activated();
            Marshal.ThrowExceptionForHR(ActivateAudioInterfaceAsync(@"VAD\Process_Loopback",ref iid,ref blob,callback,out var operation));
            try{return (IAudioClient)callback.Result.Task.WaitAsync(TimeSpan.FromSeconds(4),cancel).GetAwaiter().GetResult();}
            finally{Marshal.ReleaseComObject(operation);GC.KeepAlive(callback);}
        }finally{Marshal.FreeHGlobal(data);}
    }

    [ComImport,Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDeviceCollection{void GetCount(out uint count);void Item(uint index,out IDevice device);}
    [ComImport,Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ISessionManager{
        void GetAudioSessionControl(nint id,uint flags,out nint ctl);void GetSimpleAudioVolume(nint id,uint flags,out nint vol);
        void GetSessionEnumerator(out ISessionList sessions);
        void RegisterSessionNotification(IAudioSessionCreated listener);void UnregisterSessionNotification(IAudioSessionCreated listener);
    }
    [ComImport,Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ISessionList{void GetCount(out int count);void GetSession(int i,[MarshalAs(UnmanagedType.IUnknown)]out object session);}
    [ComImport,Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ISession2{
        void GetState(out int state);void GetDisplayName(out nint v);void SetDisplayName(nint v,nint ctx);void GetIconPath(out nint v);void SetIconPath(nint v,nint ctx);
        void GetGroupingParam(out Guid v);void SetGroupingParam(ref Guid v,nint ctx);void RegisterAudioSessionNotification(nint v);void UnregisterAudioSessionNotification(nint v);
        void GetSessionIdentifier(out nint v);void GetSessionInstanceIdentifier(out nint v);void GetProcessId(out int pid);
        [PreserveSig] int IsSystemSoundsSession();
    }
    [ComVisible(true),ClassInterface(ClassInterfaceType.None)] sealed class SessionNotice : IAudioSessionCreated{
        public int Revision;
        readonly List<nint> pending=[];bool closed;
        public int OnSessionCreated(nint session){
            lock(pending){if(closed)return 0;Marshal.AddRef(session);pending.Add(session);Interlocked.Increment(ref Revision);}return 0;
        }
        public nint[] Drain(bool close=false){
            lock(pending){closed|=close;var snapshot=pending.ToArray();pending.Clear();return snapshot;}
        }
    }
    // Managers must remain subscribed after their first enumeration to see sessions created later.
    sealed class Mixer : IDisposable
    {
        readonly IDeviceEnumerator devices=(IDeviceEnumerator)new MMDeviceEnumerator();
        readonly Dictionary<string,ISessionManager> managers=[];
        readonly SessionNotice notice=new();
        readonly HashSet<nint> notified=[];
        long nextDevices;
        public int Revision=>Volatile.Read(ref notice.Revision);
        void Remove(string id){
            if(!managers.Remove(id,out var manager))return;
            try{manager.UnregisterSessionNotification(notice);}catch(COMException){}
            Marshal.ReleaseComObject(manager);
        }
        void RefreshDevices(){
            devices.EnumAudioEndpoints(0,1,out var list);
            try{
                list.GetCount(out uint count);var connected=new HashSet<string>();
                for(uint i=0;i<count;i++){
                    list.Item(i,out var device);
                    try{
                        device.GetId(out string id);connected.Add(id);
                        if(!managers.ContainsKey(id)){
                            var iid=typeof(ISessionManager).GUID;device.Activate(ref iid,23,0,out var obj);var manager=(ISessionManager)obj;
                            try{manager.RegisterSessionNotification(notice);managers.Add(id,manager);}
                            catch{Marshal.ReleaseComObject(manager);throw;}
                        }
                    }catch(COMException){}finally{Marshal.ReleaseComObject(device);}
                }
                foreach(var id in managers.Keys.Except(connected).ToArray())Remove(id);
            }finally{Marshal.ReleaseComObject(list);}
        }
        public HashSet<int> Sources(){
            if(Environment.TickCount64>=nextDevices){RefreshDevices();nextDevices=Environment.TickCount64+2000;}
            var pids=new HashSet<int>();
            bool AddSession(object obj){
                var session=(ISession2)obj;session.GetState(out int state);
                if(state==2 || session.IsSystemSoundsSession()==0)return false;
                session.GetProcessId(out int pid);if(pid>0)pids.Add(pid);return pid>0;
            }
            // Windows can notify about a session that is absent from the enumerator.
            foreach(var session in notice.Drain())if(!notified.Add(session))Marshal.Release(session);
            foreach(var session in notified.ToArray()){
                object obj=Marshal.GetObjectForIUnknown(session);bool keep=false;
                try{keep=AddSession(obj);}catch(COMException){}finally{Marshal.ReleaseComObject(obj);}
                if(!keep){notified.Remove(session);Marshal.Release(session);}
            }
            foreach(var (id,manager) in managers.ToArray()){
                try{
                    manager.GetSessionEnumerator(out var sessions);
                    try{
                        sessions.GetCount(out int count);
                        for(int i=0;i<count;i++){
                            sessions.GetSession(i,out object obj);
                            try{AddSession(obj);}catch(COMException){}finally{Marshal.ReleaseComObject(obj);}
                        }
                    }finally{Marshal.ReleaseComObject(sessions);}
                }catch(COMException){Remove(id);nextDevices=0;}
            }
            return pids;
        }
        public void Dispose(){
            foreach(var p in notice.Drain(true))Marshal.Release(p);
            foreach(var id in managers.Keys.ToArray())Remove(id);
            foreach(var p in notified)Marshal.Release(p);Marshal.ReleaseComObject(devices);
        }
    }

    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
    struct ProcessEntry {public uint Size,Usage,Pid;public nuint Heap;public uint Module,Threads,Parent;public int Priority;public uint Flags;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)] public string Name;}
    [DllImport("kernel32.dll",SetLastError=true)] static extern nint CreateToolhelp32Snapshot(uint flags,uint pid);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern bool Process32First(nint snapshot,ref ProcessEntry entry);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern bool Process32Next(nint snapshot,ref ProcessEntry entry);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(nint h);
    static Dictionary<int,(int Parent,string Name)> ProcessTree(){
        var result=new Dictionary<int,(int,string)>();var snapshot=CreateToolhelp32Snapshot(2,0);
        if(snapshot==-1)throw new Win32Exception(Marshal.GetLastWin32Error());
        try{
            var p=new ProcessEntry{Size=(uint)Marshal.SizeOf<ProcessEntry>(),Name=""};
            if(Process32First(snapshot,ref p))do{result[(int)p.Pid]=((int)p.Parent,p.Name);}while(Process32Next(snapshot,ref p));
        }finally{CloseHandle(snapshot);}return result;
    }

    sealed record AppRoot(int Pid,string Path,long Birth,int Parent);
    static List<AppRoot> Discover(Mixer mixer){
        var pids=mixer.Sources();var tree=ProcessTree();Dictionary<int,string> roots=[];
        var paths=new Dictionary<int,string>();
        string Image(int pid){
            if(!paths.TryGetValue(pid,out var path))paths[pid]=path=Native.ImagePath(pid)??tree[pid].Name;
            return path;
        }
        foreach(int pid in pids){
            if(!tree.TryGetValue(pid,out var process))continue;
            int root=pid;int parent=process.Parent;
            var seen=new HashSet<int>{pid};bool own=pid==Environment.ProcessId;
            while(parent>0 && seen.Add(parent) && tree.TryGetValue(parent,out var ancestor)){
                if(parent==Environment.ProcessId){own=true;break;}
                if(tree[root].Parent==parent && string.Equals(ancestor.Name,process.Name,StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(Image(root),Image(parent),StringComparison.OrdinalIgnoreCase))root=parent;
                parent=ancestor.Parent;
            }
            if(!own)roots.TryAdd(root,Image(root));
        }
        var result=new List<AppRoot>();
        foreach(var (pid,path) in roots){
            var seen=new HashSet<int>{pid};int parent=tree[pid].Parent;
            while(parent!=0 && !roots.ContainsKey(parent) && seen.Add(parent) && tree.TryGetValue(parent,out var entry))parent=entry.Parent;
            result.Add(new(pid,path,Native.ProcessBirth(pid),parent!=pid&&roots.ContainsKey(parent)?parent:0));
        }
        return result;
    }
}
