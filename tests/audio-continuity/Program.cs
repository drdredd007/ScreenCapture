using Moments;

static class Checks
{
    const long Zero=480000000;
    static ulong Qpc(long frame)=>(ulong)Math.Round((Zero+frame)/0.0048);
    static void Require(bool ok,string message){if(!ok)throw new Exception(message);}

    static void PacketContinuity()
    {
        foreach(bool process in new[]{false,true}){
            var clock=new AudioPacketTimeline(process);
            var pcm=new int[480000];
            long cursor=0;var random=new Random(71);
            while(cursor<pcm.Length){
                int frames=(int)Math.Min(random.Next(2)==0?480:240,pcm.Length-cursor);
                // Multiple invalid timestamps in one polling batch used to all
                // overwrite next+3840-count. Valid process timestamps also jitter.
                uint flags=cursor>0 && cursor%4800<2400?4u:0u;
                long jitter=cursor==0?0:random.Next(-240,241);
                long first=clock.Place(frames,flags,process?0:(ulong)cursor,Qpc(cursor+jitter),Zero,3840);
                Require(first==cursor,$"Packet hole/overlap at {cursor}: {first}, process={process}, flags={flags}");
                for(int i=0;i<frames;i++)pcm[first+i]=(int)(cursor+i+1);
                cursor+=frames;
            }
            for(int i=0;i<pcm.Length;i++)Require(pcm[i]==i+1,"PCM dropped, repeated or reordered");
        }
        Console.WriteLine("PASS: 10 seconds of exact PCM, jitter, mixed valid/invalid timestamps and variable packet sizes.");
    }

    static void GapsAndStarts()
    {
        var clock=new AudioPacketTimeline(true);
        Require(clock.Place(480,4,0,0,Zero,960)==480,"first invalid timestamp fallback");
        Require(clock.Place(480,4,0,0,Zero,960)==960,"invalid packet batch overwrote earlier PCM");
        Require(clock.Place(480,0,0,Qpc(1440),Zero,1920)==1440,"timestamp recovery");
        Require(clock.Place(480,1,0,Qpc(4800),Zero,5280)==4800,"discontinuity silence collapsed");
        Require(clock.Place(480,2,0,Qpc(5280),Zero,5760)==5280,"silent packet did not consume time");
        Require(clock.Place(480,0,0,Qpc(48000),Zero,48480)==48000,"inactive app gap collapsed");
        Require(clock.Place(480,1,0,Qpc(47000),Zero,48480)==48480,"backward timestamp overwrote audio");
        var late=new AudioPacketTimeline(true);
        Require(late.Place(480,0,0,Qpc(96000),Zero,96480)==96000,"late app was moved to recording start");
        var device=new AudioPacketTimeline(false);
        Require(device.Place(480,0,0,Qpc(0),Zero,480)==0,"device anchor");
        Require(device.Place(480,0,960,Qpc(960),Zero,1440)==960,"short device gap collapsed");
        Require(device.Place(480,0,1440,Qpc(1500),Zero,1920)==1440,"device continuity ignored");
        Require(device.Place(480,1,0,Qpc(9600),Zero,10080)==9600,"device reset did not reanchor");
        Console.WriteLine("PASS: startup, real gaps, silent packets, late app, timestamp recovery and device reset.");
    }

    static async Task OutputBackpressure()
    {
        using var stop=new CancellationTokenSource();
        using var sink=new GatedStream();
        using var output=new BufferedAudioOutput(sink,stop.Token,200);
        byte[] block=new byte[3840];var expected=new MemoryStream();
        // Hold the encoder while capture submits a full second of audio.
        for(int i=0;i<100;i++){
            Array.Fill(block,(byte)i);expected.Write(block);output.Write(block);
        }
        await sink.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Array.Fill(block,(byte)255); // catches buffer aliasing while consumer stalls
        sink.Release.TrySetResult();
        await sink.WaitForBytes(expected.Length);
        Require(sink.Snapshot().SequenceEqual(expected.ToArray()),"slow encoder lost/reordered/reused audio blocks");
        Console.WriteLine("PASS: stalled encoder does not block producer; PCM resumes byte-for-byte in order.");
    }

    static async Task OutputLimits()
    {
        using var sink=new GatedStream();
        using(var output=new BufferedAudioOutput(sink,CancellationToken.None,2)){
            output.Write([1]);await sink.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            output.Write([2]);output.Write([3]);
            bool failed=false;try{output.Write([4]);}catch(IOException){failed=true;}
            Require(failed,"overflow silently dropped audio or blocked producer");
        } // cancellation must interrupt a blocked write without hanging shutdown
        using var broken=new BrokenStream();
        var failedOutput=new BufferedAudioOutput(broken,CancellationToken.None);
        failedOutput.Write([1]);
        await broken.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        bool fault=false;
        for(int i=0;i<100 && !fault;i++){
            try{failedOutput.Check();}catch(IOException){fault=true;}
            if(!fault)await Task.Delay(10);
        }
        try{failedOutput.Dispose();}catch(IOException){}
        Require(fault,"encoder failure hidden from capture");
        Console.WriteLine("PASS: overflow fails explicitly, blocked shutdown cancels, encoder failure propagates.");
    }

    public static async Task Main()
    {
        PacketContinuity();GapsAndStarts();await OutputBackpressure();await OutputLimits();
        await StartupChecks.Run();
    }

    sealed class GatedStream : MemoryStream
    {
        public readonly TaskCompletionSource Started=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Release=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer,CancellationToken token=default){
            Started.TrySetResult();await Release.Task.WaitAsync(token);
            lock(this)Write(buffer.Span);
        }
        public byte[] Snapshot(){lock(this)return ToArray();}
        public async Task WaitForBytes(long length){
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while(true){lock(this)if(Length==length)return;await Task.Delay(5,timeout.Token);}
        }
    }
    sealed class BrokenStream : MemoryStream
    {
        public readonly TaskCompletionSource Started=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer,CancellationToken token=default){
            Started.TrySetResult();return ValueTask.FromException(new IOException("Simulated encoder disconnect"));
        }
    }
}
