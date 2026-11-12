namespace Moments;

internal static class CaptureStartup
{
    public static async Task Wait(Task firstFrame,Task? audio,TimeSpan timeout,Func<string> diagnostics)
    {
        try{
            await (audio==null?firstFrame:Task.WhenAny(firstFrame,audio)).WaitAsync(timeout);
        }catch(TimeoutException ex){
            throw new TimeoutException("Capture did not produce its first frame. "+diagnostics(),ex);
        }
        // Check audio even if both tasks finished together; a dead audio feed
        // must never turn into a successful recording with frozen/silent sound.
        if(audio?.IsCompleted==true){
            try{await audio;}catch(Exception ex){throw new IOException("Audio capture failed during startup. "+ex.Message,ex);}
            throw new IOException("Audio capture ended before recording started.");
        }
        await firstFrame;
    }
}
