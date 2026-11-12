using System.IO.Pipes;
using System.Runtime.InteropServices;

namespace Moments;

// WASAPI shared-mode PCM. Both endpoints are placed on one QPC timeline before
// writing the interleaved pipe, including silence when loopback has no packets.
internal sealed partial class AudioFeed : IDisposable
{
    readonly CancellationTokenSource stop=new();
    readonly NamedPipeServerStream pipe;
    readonly Task worker;
    readonly TaskCompletionSource ready=new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task Ready=>ready.Task;
    public Task Completion=>worker;
    public string Name {get;}="moments_"+Guid.NewGuid().ToString("N");
    public string PathName=>@"\\.\pipe\"+Name;
    public AudioFeed(bool system,bool mic,AppAudioArchive? apps=null)
    {
        pipe=new(Name,PipeDirection.Out,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly,0,65536);
        worker=Task.Factory.StartNew(()=>Pump(system,mic,apps),CancellationToken.None,TaskCreationOptions.LongRunning,TaskScheduler.Default);
    }
    void Pump(bool system,bool mic,AppAudioArchive? apps)
    {
        try
        {
            using var isolated=apps!=null?new AppInputs(apps,stop.Token):null;
            using var speakers=system && isolated==null?Endpoint.Open(true):null;
            using var microphone=mic?Endpoint.Open(false):null;
            ready.TrySetResult();
            pipe.WaitForConnectionAsync(stop.Token).GetAwaiter().GetResult();
            using var output=new BufferedAudioOutput(pipe,stop.Token);
            speakers?.Start();microphone?.Start();isolated?.Start();
            long origin=Stopwatch.GetTimestamp();
            long sampleZero=(long)(origin*(48000.0/Stopwatch.Frequency));
            short[] frame=new short[480*4];
            byte[] bytes=new byte[frame.Length*2];
            long next=0;
            while(!stop.IsCancellationRequested)
            {
                output.Check();
                speakers?.Poll(sampleZero,next);microphone?.Poll(sampleZero,next);isolated?.Poll(sampleZero,next);
                long available=(long)((Stopwatch.GetTimestamp()-origin)*(48000.0/Stopwatch.Frequency))-3840;
                while(next+480<=available && !stop.IsCancellationRequested)
                {
                    for(int i=0;i<480;i++){
                        speakers?.Take(next+i,frame,i*4);
                        microphone?.Take(next+i,frame,i*4+2);
                    }
                    isolated?.Write(next);Buffer.BlockCopy(frame,0,bytes,0,bytes.Length);
                    output.Write(bytes);
                    next+=480;
                }
                stop.Token.WaitHandle.WaitOne(4);
            }
        }catch(Exception ex){ready.TrySetException(ex);if(!stop.IsCancellationRequested) throw;}
    }
    public void Dispose()
    {
        stop.Cancel();pipe.Dispose();
        try{worker.GetAwaiter().GetResult();}catch(Exception ex){Home.Log("Audio: "+ex.Message);}
        stop.Dispose();
    }

    sealed class Endpoint : IDisposable
    {
        const int Capacity=48000*2;
        readonly int[] ring=new int[Capacity*2];
        short[] packet=new short[8192];
        float[]? floatPacket;
        IDeviceEnumerator? enumerator; IDevice? device; IAudioClient? client; IAudioCaptureClient? capture;
        bool started;
        AudioPacketTimeline timeline=new(false);
        long[]? stamps;
        public static Endpoint Open(bool loopback)
        {
            var e=new Endpoint();
            try {
                e.enumerator=(IDeviceEnumerator)new MMDeviceEnumerator();
                e.enumerator.GetDefaultAudioEndpoint(loopback?0:1,1,out e.device);
                var iid=typeof(IAudioClient).GUID;e.device.Activate(ref iid,23,0,out var obj);
                e.client=(IAudioClient)obj;
                e.Initialize(loopback);return e;
            }catch(Exception ex){e.Dispose();throw new IOException((loopback?"System audio":"Microphone")+" unavailable. Check the device in Windows or disable this track in Settings. "+ex.Message,ex);}
        }
        public static Endpoint ForProcess(int pid,CancellationToken cancel){
            var e=new Endpoint();
            try{e.timeline=new(true);e.stamps=new long[Capacity];e.floatPacket=new float[8192];e.client=ActivateProcess(pid,cancel);e.Initialize(true);return e;}
            catch{e.Dispose();throw;}
        }
        void Initialize(bool loopback){
            // Process trees are summed/subtracted before final quantization. Float
            // preserves headroom and avoids generating PCM16 dither for silent apps.
            bool floating=floatPacket!=null;
            Wave fmt=new(){Tag=(ushort)(floating?3:1),Channels=2,Rate=48000,Bytes=floating?384000u:192000u,Align=(ushort)(floating?8:4),Bits=(ushort)(floating?32:16)};
            client!.Initialize(0,0x88000000u|(loopback?0x20000u:0),2_000_000,0,ref fmt,0);
            var iid=typeof(IAudioCaptureClient).GUID;client.GetService(ref iid,out var obj);capture=(IAudioCaptureClient)obj;
        }
        public void Start(){client!.Start();started=true;}
        public void Poll(long zero,long next)
        {
            capture!.GetNextPacketSize(out uint size);
            while(size>0)
            {
                capture.GetBuffer(out nint data,out uint frames,out uint flags,out ulong devicePosition,out ulong qpc);
                try {
                    int count=checked((int)frames);
                    if(count==0)break;
                    long now=(long)(Stopwatch.GetTimestamp()*(48000.0/Stopwatch.Frequency))-zero;
                    long first=timeline.Place(count,flags,devicePosition,qpc,zero,now);
                    bool silent=(flags&2)!=0;
                    if(!silent){
                        if(floatPacket!=null){if(floatPacket.Length<count*2)Array.Resize(ref floatPacket,count*2);Marshal.Copy(data,floatPacket,0,count*2);}
                        else{if(packet.Length<count*2)Array.Resize(ref packet,count*2);Marshal.Copy(data,packet,0,count*2);}
                    }
                    for(int i=0;i<count;i++){
                        long at=first+i;if(at<next || at>=next+Capacity)continue;
                        int slot=(int)(at%Capacity),j=slot*2;
                        if(silent){ring[j]=ring[j+1]=0;}
                        else if(floatPacket!=null){ring[j]=(int)MathF.Round(floatPacket[i*2]*32768f);ring[j+1]=(int)MathF.Round(floatPacket[i*2+1]*32768f);}
                        else{ring[j]=packet[i*2];ring[j+1]=packet[i*2+1];}
                        if(stamps!=null)stamps[slot]=at+1;
                    }
                }finally{capture.ReleaseBuffer(frames);}
                capture.GetNextPacketSize(out size);
            }
        }
        public void Take(long pos,short[] buffer,int off)
        {
            int j=(int)(pos%Capacity)*2;buffer[off]=(short)ring[j];buffer[off+1]=(short)ring[j+1];ring[j]=ring[j+1]=0;
        }
        public void Read(long first,int[] buffer){
            for(int i=0;i<buffer.Length/2;i++){
                long at=first+i;int slot=at<0?0:(int)(at%Capacity);bool valid=at>=0 && stamps![slot]==at+1;
                buffer[2*i]=valid?ring[slot*2]:0;buffer[2*i+1]=valid?ring[slot*2+1]:0;
            }
        }
        int Sample(long at,int channel){
            if(at<0)return 0;int slot=(int)(at%Capacity);return stamps![slot]==at+1?ring[2*slot+channel]:0;
        }
        public void ReadAligned(double first,int[] buffer){
            long n=(long)Math.Floor(first);double t=first-n;
            double a=-t*(t-1)*(t-2)/6,b=(t+1)*(t-1)*(t-2)/2,c=-(t+1)*t*(t-2)/2,d=(t+1)*t*(t-1)/6;
            for(int i=0;i<buffer.Length;i+=2,n++)for(int ch=0;ch<2;ch++)
                buffer[i+ch]=(int)Math.Round(a*Sample(n-1,ch)+b*Sample(n,ch)+c*Sample(n+1,ch)+d*Sample(n+2,ch));
        }
        public void Dispose(){
            if(started){try{client!.Stop();}catch(COMException){}}
            foreach(object? o in new object?[]{capture,client,device,enumerator})if(o!=null)Marshal.ReleaseComObject(o);
        }
    }
    [StructLayout(LayoutKind.Sequential,Pack=2)] struct Wave{public ushort Tag,Channels;public uint Rate,Bytes;public ushort Align,Bits,Extra;}
    [ComImport,Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]class MMDeviceEnumerator{}
    [ComImport,Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDeviceEnumerator{void EnumAudioEndpoints(int flow,uint mask,out IDeviceCollection list);void GetDefaultAudioEndpoint(int flow,int role,out IDevice device);}
    [ComImport,Guid("D666063F-1587-4E43-81F1-B948E807363F"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDevice{void Activate(ref Guid iid,uint ctx,nint act,[MarshalAs(UnmanagedType.IUnknown)] out object value);void OpenPropertyStore(uint access,out nint store);void GetId([MarshalAs(UnmanagedType.LPWStr)]out string id);}
    [ComImport,Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioClient{
        void Initialize(int mode,uint flags,long duration,long period,ref Wave format,nint session);
        void GetBufferSize(out uint size);void GetStreamLatency(out long latency);void GetCurrentPadding(out uint padding);
        void IsFormatSupported(int mode,nint format,out nint closest);void GetMixFormat(out nint fmt);void GetDevicePeriod(out long normal,out long min);
        void Start();void Stop();void Reset();void SetEventHandle(nint h);void GetService(ref Guid iid,[MarshalAs(UnmanagedType.IUnknown)] out object service);
    }
    [ComImport,Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioCaptureClient{
        void GetBuffer(out nint data,out uint frames,out uint flags,out ulong devicePos,out ulong qpc);
        void ReleaseBuffer(uint frames);void GetNextPacketSize(out uint frames);
    }
}


