using System.Threading.Channels;

namespace Moments;

// Keep encoder backpressure off the WASAPI polling thread. Bound memory, but never
// silently discard sound: a sustained stall fails the capture with an explanation.
internal sealed class BufferedAudioOutput : IDisposable
{
    readonly Channel<byte[]> queue;
    readonly CancellationTokenSource cancel;
    readonly Task writer;
    public BufferedAudioOutput(Stream destination,CancellationToken stop,int capacity=200)
    {
        queue=Channel.CreateBounded<byte[]>(new BoundedChannelOptions(capacity){
            SingleReader=true,SingleWriter=true,FullMode=BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations=false});
        cancel=CancellationTokenSource.CreateLinkedTokenSource(stop);
        writer=Task.Run(async()=>{
            await foreach(var block in queue.Reader.ReadAllAsync(cancel.Token))
                await destination.WriteAsync(block,cancel.Token);
        });
    }
    public void Check(){if(writer.IsCompleted)writer.GetAwaiter().GetResult();}
    public void Write(byte[] block)
    {
        Check();
        // The producer reuses its buffer for the next 10 ms block.
        if(!queue.Writer.TryWrite((byte[])block.Clone()))
            throw new IOException("The video encoder stopped accepting audio for over two seconds. Lower recording quality or check disk performance.");
    }
    public void Dispose()
    {
        queue.Writer.TryComplete();cancel.Cancel();
        try{writer.GetAwaiter().GetResult();}catch(OperationCanceledException) when(cancel.IsCancellationRequested){}
        finally{cancel.Dispose();}
    }
}
