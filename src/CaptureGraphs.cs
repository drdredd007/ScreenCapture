namespace Moments;

internal static class CaptureGraphs
{
    public static void Add(List<string> arguments,string video,string? audio)
    {
        // Each complex graph has its own FFmpeg scheduler thread. A live PCM
        // input arrives in demuxer-sized packets (4096 samples at 48 kHz), not
        // every video refresh. Putting it in the source-only WGC graph blocks
        // that graph while waiting for sound and loses real screen frames.
        arguments.AddRange(["-filter_complex",video]);
        if(!string.IsNullOrEmpty(audio))arguments.AddRange(["-filter_complex",audio]);
    }
}
