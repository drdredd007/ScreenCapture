using System.Threading.Tasks.Sources;
using Moments;

internal static class StartupChecks
{
    static readonly TimeSpan Limit=TimeSpan.FromSeconds(5);
    static void Require(bool ok,string message){if(!ok)throw new Exception(message);}
    static async Task<T> Throws<T>(Task task) where T:Exception {
        try{await task.WaitAsync(Limit);}catch(T ex){return ex;}
        throw new Exception("Expected "+typeof(T).Name);
    }
    public static async Task Run()
    {
        // Reproduce the old failure with the same kind of pending operation as
        // Windows overlapped named-pipe I/O (Task-backed fakes hide this bug).
        using(var old=new PendingStream()){
            bool failed=false;
            try{old.WriteAsync(AudioPipeFormat.Header(),CancellationToken.None).GetAwaiter().GetResult();}
            catch(InvalidOperationException){failed=true;}
            Require(failed,"Control did not reproduce the old incomplete ValueTask failure");
        }
        using(var pending=new PendingStream()){
            var write=Task.Run(()=>AudioPipeFormat.WriteHeader(pending,CancellationToken.None));
            await pending.Subscribed.Task.WaitAsync(Limit);
            Require(!write.IsCompleted,"Header completed before pending pipe write");
            pending.Complete();await write.WaitAsync(Limit);
            Require(pending.Bytes.SequenceEqual(AudioPipeFormat.Header()),"Header bytes changed");
        }
        using(var cancel=new CancellationTokenSource())
        using(var pending=new PendingStream()){
            var write=Task.Run(()=>AudioPipeFormat.WriteHeader(pending,cancel.Token));
            await pending.Subscribed.Task.WaitAsync(Limit);cancel.Cancel();
            await Throws<OperationCanceledException>(write);
        }
        using(var pending=new PendingStream()){
            var write=Task.Run(()=>AudioPipeFormat.WriteHeader(pending,CancellationToken.None));
            await pending.Subscribed.Task.WaitAsync(Limit);pending.Complete(new IOException("pipe closed"));
            Require((await Throws<IOException>(write)).Message=="pipe closed","Lost pipe error");
        }
        using(var memory=new MemoryStream()){
            AudioPipeFormat.WriteHeader(memory,CancellationToken.None);
            Require(memory.ToArray().SequenceEqual(AudioPipeFormat.Header()),"Synchronous write changed header");
        }
        Console.WriteLine("PASS: old pending ValueTask failure reproduced; production header waits, cancels and propagates broken pipe errors.");

        var frame=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var audio=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var startup=CaptureStartup.Wait(frame.Task,audio.Task,Limit,()=>"diagnostic");
        var cause=new InvalidOperationException("audio failed");audio.SetException(cause);
        var failure=await Throws<IOException>(startup);
        Require(ReferenceEquals(failure.InnerException,cause),"Startup hid the original audio error");
        await Throws<IOException>(CaptureStartup.Wait(frame.Task,Task.CompletedTask,Limit,()=>""));
        await Throws<IOException>(CaptureStartup.Wait(Task.CompletedTask,Task.FromException(cause),Limit,()=>""));
        var timeout=await Throws<TimeoutException>(CaptureStartup.Wait(frame.Task,null,TimeSpan.FromMilliseconds(20),()=>"encoder diagnostic"));
        Require(timeout.Message.Contains("encoder diagnostic"),"Timeout lost FFmpeg diagnostics");
        var runningAudio=new TaskCompletionSource();frame.SetResult();
        await CaptureStartup.Wait(frame.Task,runningAudio.Task,Limit,()=>"");
        Require(!runningAudio.Task.IsCompleted,"Startup stopped live audio");
        await CaptureStartup.Wait(frame.Task,null,Limit,()=>"");
        var videoError=new IOException("video failed");
        Require(ReferenceEquals(await Throws<IOException>(CaptureStartup.Wait(Task.FromException(videoError),runningAudio.Task,Limit,()=>"")),videoError),"Video error lost");
        Console.WriteLine("PASS: startup audio failure/exit is immediate, simultaneous faults preserved, video errors and timeout diagnostics retained.");
    }

    sealed class PendingStream : MemoryStream,IValueTaskSource
    {
        ManualResetValueTaskSourceCore<bool> source=new(){RunContinuationsAsynchronously=true};
        CancellationTokenRegistration registration;
        public readonly TaskCompletionSource Subscribed=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public byte[] Bytes=[];
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer,CancellationToken token=default){
            Bytes=buffer.ToArray();registration=token.Register(()=>Complete(new OperationCanceledException(token)));
            return new ValueTask(this,source.Version);
        }
        public void Complete(Exception? error=null){if(error==null)source.SetResult(true);else source.SetException(error);}
        public void GetResult(short token)=>source.GetResult(token);
        public ValueTaskSourceStatus GetStatus(short token)=>source.GetStatus(token);
        public void OnCompleted(Action<object?> continuation,object? state,short token,ValueTaskSourceOnCompletedFlags flags){
            source.OnCompleted(continuation,state,token,flags);Subscribed.TrySetResult();
        }
        protected override void Dispose(bool disposing){registration.Dispose();base.Dispose(disposing);}
    }
}
