namespace Moments;

internal static class AudioPipeFormat
{
    public const int FramesPerBlock=480,Channels=4,Rate=48000,BytesPerBlock=FramesPerBlock*Channels*2;
    public static byte[] Header(){
        using var data=new MemoryStream();using var writer=new BinaryWriter(data);
        // Unknown-length PCM WAV over a pipe. The demuxer's ignore_length flag
        // permits arbitrarily long recordings; these are not disk WAV files.
        writer.Write("RIFF"u8);writer.Write(uint.MaxValue);writer.Write("WAVEfmt "u8);writer.Write(16);
        writer.Write((ushort)1);writer.Write((ushort)Channels);writer.Write(Rate);writer.Write(Rate*Channels*2);
        writer.Write((ushort)(Channels*2));writer.Write((ushort)16);writer.Write("data"u8);writer.Write(uint.MaxValue);
        return data.ToArray();
    }
    public static string[] InputArguments(string path)=>["-f","wav","-ignore_length","1","-max_size",BytesPerBlock.ToString(System.Globalization.CultureInfo.InvariantCulture),
        "-probesize","32","-analyzeduration","0","-i",path];
}
