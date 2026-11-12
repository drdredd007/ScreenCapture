using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.Json;
namespace Moments;

// The video muxer cannot add streams after its header is written. Keep only audible
// stereo blocks on the same 48 kHz clock as the recorder, then mux the chosen interval.
internal sealed class AppAudioArchive
{
    const int Rate=48000,Frames=Rate*Capture.Segment,Bytes=Frames*4;
    sealed record Layout(bool Microphone,string Preferred);
    sealed class Stem(string id,string label){
        public string Id=id,Label=label;
        public int Chunk=-1;
        public FileStream? Writer;
    }
    readonly string directory;
    readonly Layout layout;
    readonly object sync=new();
    readonly Dictionary<string,Stem> stems=new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> warnings=new(StringComparer.OrdinalIgnoreCase);
    readonly byte[] block=new byte[480*4];
    int pruned;
    static string Chunk(int n)=>n.ToString("D8",CultureInfo.InvariantCulture)+".wav";
    public static string DirectoryFor(string folder)=>Path.Combine(folder,"app-audio");
    public string Warning {get{lock(sync)return warnings.Count==0?"":" Some app audio was unavailable. See Moments.log.";}}
    AppAudioArchive(string directory,Layout layout){this.directory=directory;this.layout=layout;}
    public static AppAudioArchive Create(string folder,bool mic,string preferred){
        var dir=DirectoryFor(folder);Directory.CreateDirectory(dir);var layout=new Layout(mic,preferred);
        File.WriteAllText(Path.Combine(dir,"layout.json"),JsonSerializer.Serialize(layout));
        using var silence=MakeWave(Path.Combine(dir,"silence.wav"));
        return new(dir,layout);
    }
    public static AppAudioArchive Reopen(string folder){
        var dir=DirectoryFor(folder);var layout=JsonSerializer.Deserialize<Layout>(File.ReadAllText(Path.Combine(dir,"layout.json")))??throw new InvalidDataException("Missing audio layout.");
        var archive=new AppAudioArchive(dir,layout);
        foreach(string sub in Directory.GetDirectories(dir)){
            string id=Path.GetFileName(sub),label=Path.Combine(sub,"name.txt");
            if(id.Length==0 || !id.All(char.IsAsciiDigit) || !File.Exists(label))continue;
            archive.stems[id]=new(id,File.ReadAllText(label));
        }
        return archive;
    }
    internal static async Task Recover(string folder,string raw){
        var archive=Reopen(folder);string video=raw;
        string rebuilt=Path.Combine(folder,"recovered.mkv"),tmp=Path.Combine(folder,"recovered.mp4");
        try{
            double length;
            try{length=(await Media.Probe(raw)).Duration;}
            catch(Exception ex) when(ex is KeyNotFoundException or FormatException or IOException){
                await Proc.Run("ffmpeg",["-v","error","-y","-i",raw,"-map","0","-c","copy",rebuilt]);
                video=rebuilt;length=(await Media.Probe(video)).Duration;
            }
            await archive.Mux(["-i",video],tmp,0,length);File.Move(tmp,Home.Unique(Home.Videos,"Recovered recording","mp4"));
        }finally{File.Delete(tmp);File.Delete(rebuilt);}
    }
    static FileStream MakeWave(string file){
        var stream=new FileStream(file,FileMode.Create,FileAccess.Write,FileShare.ReadWrite);
        try{
            using var w=new BinaryWriter(stream,Encoding.UTF8,true);
            w.Write("RIFF"u8);w.Write(36+Bytes);w.Write("WAVEfmt "u8);w.Write(16);
            w.Write((ushort)1);w.Write((ushort)2);w.Write(Rate);w.Write(Rate*4);w.Write((ushort)4);w.Write((ushort)16);
            w.Write("data"u8);w.Write(Bytes);stream.SetLength(44+Bytes);return stream;
        }catch{stream.Dispose();throw;}
    }
    public void Warn(string name,Exception ex){
        lock(sync)if(warnings.Add(name))Home.Log("Application audio unavailable for "+Path.GetFileName(name)+": "+ex.Message);
    }
    public void Write(string identity,long first,int[] samples){
        if(samples.AsSpan().IndexOfAnyExcept(0)<0)return;
        int chunk=checked((int)(first/Frames)),offset=(int)(first%Frames)*4;
        lock(sync){
            if(chunk<pruned)return;
            if(!stems.TryGetValue(identity,out var stem)){
                string id=stems.Count.ToString("D4",CultureInfo.InvariantCulture),name=Path.GetFileNameWithoutExtension(identity);
                string label=name;for(int i=2;stems.Values.Any(s=>s.Label.Equals(label,StringComparison.OrdinalIgnoreCase)) || label=="Microphone";i++)label=$"{name} ({i})";
                stem=stems[identity]=new(id,label);Directory.CreateDirectory(Path.Combine(directory,id));
                File.WriteAllText(Path.Combine(directory,id,"name.txt"),label);
            }
            if(stem.Chunk!=chunk){
                stem.Writer?.Dispose();stem.Writer=MakeWave(Path.Combine(directory,stem.Id,Chunk(chunk)));stem.Chunk=chunk;
            }
            for(int i=0;i<samples.Length;i++)BinaryPrimitives.WriteInt16LittleEndian(block.AsSpan(i*2), (short)Math.Clamp(samples[i],short.MinValue,short.MaxValue));
            long pos=44L+offset;if(stem.Writer!.Position!=pos)stem.Writer.Position=pos;
            stem.Writer.Write(block);
        }
    }
    public void Flush(bool close){
        lock(sync)foreach(var s in stems.Values){s.Writer?.Flush();if(close){s.Writer?.Dispose();s.Writer=null;}}
    }
    public void PruneBefore(int earliest){
        lock(sync){
            for(int n=pruned;n<earliest;n++)foreach(var stem in stems.Values){
                if(stem.Chunk==n){stem.Writer?.Dispose();stem.Writer=null;}
                File.Delete(Path.Combine(directory,stem.Id,Chunk(n)));
            }
            pruned=Math.Max(pruned,earliest);
        }
    }
    static bool HasSound(string path,int chunk,double from,double until){
        if(!File.Exists(path))return false;
        int first=(int)Math.Clamp(Math.Round((from-chunk*Capture.Segment)*Rate),0,Frames);
        int end=(int)Math.Clamp(Math.Ceiling((until-chunk*Capture.Segment)*Rate),0,Frames);
        if(end<=first)return false;
        if(first==0 && end==Frames)return true; // Whole chunks are created only after a nonzero block.
        using var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite);
        input.Position=44L+first*4;Span<byte> bytes=stackalloc byte[4096];
        for(int remaining=(end-first)*4;remaining>0;){
            int n=input.Read(bytes[..Math.Min(bytes.Length,remaining)]);if(n==0)break;
            if(bytes[..n].IndexOfAnyExcept((byte)0)>=0)return true;remaining-=n;
        }
        return false;
    }
    public async Task Mux(string[] videoInputs,string output,double from,double duration){
        if(duration<=0)throw new IOException("There is no recorded media to save.");
        int first=(int)Math.Floor(from/Capture.Segment),last=(int)Math.Ceiling((from+duration)/Capture.Segment)-1;
        Stem[] tracks;
        lock(sync){
            Flush(close:false);
            tracks=stems.Values.OrderByDescending(s=>s.Label.Equals(layout.Preferred,StringComparison.OrdinalIgnoreCase)).ThenBy(s=>s.Label,StringComparer.OrdinalIgnoreCase).ToArray();
        }
        List<string> args=["-v","error","-y",..videoInputs];List<string> labels=[],manifests=[];
        try{
            foreach(var stem in tracks){
                List<string> lines=[];bool sound=false;
                for(int n=first;n<=last;n++){
                    string relative=stem.Id+"/"+Chunk(n);
                    string path=Path.Combine(directory,relative);
                    if(!File.Exists(path))relative="silence.wav";
                    else if(!sound)sound=HasSound(path,n,from,from+duration);
                    lines.Add($"file '{relative}'");lines.Add("duration "+Capture.Segment);
                }
                if(!sound)continue;
                string list=Path.Combine(directory,Guid.NewGuid().ToString("N")+".ffconcat");manifests.Add(list);
                File.WriteAllLines(list,lines,new UTF8Encoding(false));
                args.AddRange(["-f","concat","-safe","1","-ss",Home.N(from-first*Capture.Segment),"-t",Home.N(duration),"-i",list]);
                labels.Add(stem.Label);
            }
            args.AddRange(["-map","0:v:0","-c:v","copy"]);
            var audioInputs=Enumerable.Range(1,labels.Count).Select(i=>$"{i}:a:0").ToList();
            if(layout.Microphone){audioInputs.Add("0:a:0");labels.Add("Microphone");}
            Media.AddPlaybackAudio(args,audioInputs,labels);
            args.AddRange(["-t",Home.N(duration),"-movflags","+faststart",output]);await Proc.Run("ffmpeg",args);
        }finally{foreach(var file in manifests)File.Delete(file);}
    }
}

