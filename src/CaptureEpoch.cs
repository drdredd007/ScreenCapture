using System.Diagnostics;
using System.Globalization;

namespace Moments;

// One origin for video, mixed PCM, microphone and archived application tracks.
// UTC is used only to bridge the first FFmpeg video frame to the QPC clock.
// Subsequent frames retain gfxcapture's monotonic timestamps, and audio retains
// its sample counter: encoder/pipe delays must never change presentation time.
internal sealed class CaptureEpoch
{
    public const int Rate=48000;
    public readonly Stopwatch Clock=Stopwatch.StartNew();
    public readonly long Qpc;
    public readonly double UnixSeconds;
    public CaptureEpoch()
    {
        long before=Stopwatch.GetTimestamp();
        UnixSeconds=(DateTime.UtcNow.Ticks-DateTime.UnixEpoch.Ticks)/10_000_000d;
        long after=Stopwatch.GetTimestamp();
        Qpc=before+(after-before)/2;
    }
    public long SampleZero=>(long)(Qpc*(Rate/(double)Stopwatch.Frequency));
    public long SamplesNow=>(long)((Stopwatch.GetTimestamp()-Qpc)*(Rate/(double)Stopwatch.Frequency));
    public string VideoFilter=>VideoFilterAt(UnixSeconds);
    internal static string VideoFilterAt(double unixSeconds)=>
        "setpts='PTS-STARTPTS+if(eq(N,0),st(0,max(0,time(0)-"+
        unixSeconds.ToString("F7",CultureInfo.InvariantCulture)+")/TB),ld(0))'";
}
