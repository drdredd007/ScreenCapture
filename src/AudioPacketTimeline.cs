namespace Moments;

// A packet timestamp anchors a stream; it must not splice every packet into PCM.
// In particular, process loopback clocks jitter and may not expose device positions.
internal sealed class AudioPacketTimeline(bool processLoopback)
{
    const long JitterFrames=2400; // 50 ms; larger unreported gaps start a new span.
    long? end;
    ulong? deviceEnd;

    public long Place(int frames,uint flags,ulong devicePosition,ulong qpc,long zero,long now)
    {
        bool timestampValid=(flags&4)==0 && qpc!=0;
        bool discontinuity=(flags&1)!=0;
        long timestamp=timestampValid ? (long)Math.Round(qpc*0.0048)-zero : now-frames;
        long first=timestamp;
        if(end is long expected){
            // A real endpoint's frame counter distinguishes lost audio from QPC
            // jitter. Do not close a genuine hole just because it is short.
            bool deviceGap=!processLoopback && timestampValid && deviceEnd is ulong previous && devicePosition>previous;
            bool contiguousDevice=!processLoopback && timestampValid && deviceEnd==devicePosition;
            if(deviceGap)
                first=checked(expected+(long)(devicePosition-deviceEnd!.Value));
            else if(!discontinuity && (!timestampValid || contiguousDevice || Math.Abs(timestamp-expected)<=JitterFrames))
                first=expected;
            else
                first=Math.Max(expected,timestamp); // Never overwrite PCM already accepted.
        }
        end=checked(first+frames);
        deviceEnd=timestampValid ? checked(devicePosition+(ulong)frames) : null;
        return first;
    }
}
