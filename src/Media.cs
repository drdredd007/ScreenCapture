using System.Globalization;
using System.Text;
using System.Text.Json;
namespace Moments;
internal sealed record Track(int Index,string Name,string Codec);
internal sealed record ClipInfo(double Duration,int Width,int Height,string VideoCodec,List<Track> Tracks,double Fps,bool LegacyRgbTags=false);
internal readonly record struct Interval(double Start,double End)
{
    public override string ToString()=>Home.Time(Start)+"   →   "+Home.Time(End);
}
internal static class Media
{
    internal const string DesktopYuv="setparams=range=limited:colorspace=bt470bg:color_primaries=bt709:color_trc=iec61966-2-1";
    static bool HasOldCaptureTags(JsonElement stream){
        string? Tag(string key)=>stream.TryGetProperty(key,out var val)?val.GetString():null;
        return Tag("codec_name")=="h264" && Tag("pix_fmt")=="yuv420p" && Tag("color_range")=="pc" &&
            Tag("color_space")=="gbr" && Tag("color_primaries")=="bt709" && Tag("color_transfer")=="iec61966-2-1";
    }
    public static async Task<System.Windows.Media.Imaging.BitmapImage> Thumbnail(FileInfo file,CancellationToken ct=default)
    {
        string key=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes("color3"+file.FullName+file.Length+file.LastWriteTimeUtc.Ticks)));
        string cache=Path.Combine(Home.Work,"thumbs");Directory.CreateDirectory(cache);
        string path=Path.Combine(cache,key+".jpg");
        if(!File.Exists(path)){
            string tmp=Path.Combine(cache,Guid.NewGuid().ToString("N")+".jpg");
            try{
                var info=await Probe(file.FullName,ct);
                string filter=(info.LegacyRgbTags?DesktopYuv+",":"")+"scale=480:-2";
                await Proc.Run("ffmpeg",["-v","error","-y","-i",file.FullName,"-frames:v","1","-vf",filter,"-q:v","5",tmp],ct);
                try{File.Move(tmp,path);}catch(IOException) when(File.Exists(path)){}
            }finally{File.Delete(tmp);}
        }
        var bi=new System.Windows.Media.Imaging.BitmapImage();bi.BeginInit();bi.CacheOption=System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
        bi.UriSource=new Uri(path);bi.EndInit();bi.Freeze();return bi;
    }
    public static async Task<ClipInfo> Probe(string path,CancellationToken ct=default)
    {
        string json=await Proc.Run("ffprobe",["-v","error","-show_streams","-show_format","-of","json",path],ct);
        using var doc=JsonDocument.Parse(json);var root=doc.RootElement;
        var streams=root.GetProperty("streams").EnumerateArray().ToArray();
        var v=streams.First(s=>s.GetProperty("codec_type").GetString()=="video");
        var tracks=new List<Track>();
        foreach(var s in streams.Where(s=>s.GetProperty("codec_type").GetString()=="audio")){
            string name="Track "+(tracks.Count+1);
            if(s.TryGetProperty("tags",out var tags)){
                if(tags.TryGetProperty("title",out var title))name=title.GetString()??name;
                else if(tags.TryGetProperty("handler_name",out var handler))name=handler.GetString()??name;
            }
            tracks.Add(new(s.GetProperty("index").GetInt32(),name,s.GetProperty("codec_name").GetString()??""));
        }
        var rate=(v.GetProperty("r_frame_rate").GetString()??"").Split('/');
        double fps=rate.Length==2 && double.TryParse(rate[0],CultureInfo.InvariantCulture,out double num) && double.TryParse(rate[1],CultureInfo.InvariantCulture,out double den) && den>0?num/den:0;
        return new(double.Parse(root.GetProperty("format").GetProperty("duration").GetString()!,CultureInfo.InvariantCulture),
            v.GetProperty("width").GetInt32(),v.GetProperty("height").GetInt32(),v.GetProperty("codec_name").GetString()!,tracks,fps,HasOldCaptureTags(v));
    }
    public static string TrackLabel(string name)=>name switch{"System"=>"System audio","Microphone"=>"Microphone",_=>name};
    public static List<Interval> Kept(double duration,IEnumerable<Interval> removed)
    {
        List<Interval> result=[];double at=0;
        foreach(var r in removed.OrderBy(r=>r.Start)){
            double s=Math.Clamp(r.Start,0,duration),e=Math.Clamp(r.End,0,duration);if(e<=s)continue;
            if(s>at+0.001)result.Add(new(at,s));at=Math.Max(e,at);
        }
        if(duration-at>0.001)result.Add(new(at,duration));return result;
    }
    public static async Task Remux(string[] inputs,string output,CancellationToken ct=default,bool encodePcm=false,double? duration=null)
    {
        string source=inputs[^1];
        if(source.EndsWith(".ffconcat",StringComparison.OrdinalIgnoreCase)){
            var line=File.ReadLines(source).First();
            source=Path.Combine(Path.GetDirectoryName(source)!,line[6..^1]);
        }
        var info=await Probe(source,ct);
        List<string> args=["-v","error","-y",..inputs,"-map","0:v:0","-map","0:a?","-c","copy"];
        if(duration.HasValue)args.AddRange(["-t",Home.N(duration.Value)]);
        if(encodePcm && info.Tracks.Count>0)args.AddRange(["-c:a",output.EndsWith(".webm")?"libopus":"aac","-b:a","160k"]);
        AddTrackNames(args,info.Tracks);
        if(output.EndsWith(".mp4"))args.AddRange(["-movflags","+faststart"]);
        args.Add(output);await Proc.Run("ffmpeg",args,ct);
    }
    static void AddTrackNames(List<string> args,IReadOnlyList<Track> tracks)
    {
        for(int i=0;i<tracks.Count;i++)args.AddRange([$"-metadata:s:a:{i}","title="+tracks[i].Name,$"-metadata:s:a:{i}","handler_name="+tracks[i].Name]);
    }
    public static async Task<string> Export(string path,string title,string ext,IReadOnlyList<Interval> kept,IReadOnlyList<Track> tracks,Action<int,int> progress,CancellationToken ct)
    {
        if(kept.Count==0)throw new IOException("Keep at least one clip in the edit.");
        if(ext is not ("mp4" or "mkv" or "webm"))throw new ArgumentException("Unknown format");
        string folder=Home.Temp("edit");
        try{
            List<string> parts=[];var sourceInfo=await Probe(path,ct);
            for(int i=0;i<kept.Count;i++){
                ct.ThrowIfCancellationRequested();progress(i,kept.Count);
                var r=kept[i];string part=Path.Combine(folder,$"{i:D5}.mkv");
                List<string> args=["-v","error","-y","-ss",Home.N(r.Start),"-i",path,"-t",Home.N(r.End-r.Start),"-map","0:v:0"];
                if(sourceInfo.LegacyRgbTags)args.AddRange(["-vf",DesktopYuv]);
                foreach(var t in tracks)args.AddRange(["-map","0:"+t.Index]);
                if(ext=="webm")args.AddRange(["-c:v","libvpx-vp9","-row-mt","1","-cpu-used","6","-crf","34","-b:v","0"]);
                else args.AddRange(["-c:v","libx264","-preset","veryfast","-crf","19","-bf","0"]);
                args.AddRange(["-c:a","pcm_s16le","-pix_fmt","yuv420p","-threads","6"]);
                AddTrackNames(args,tracks);args.Add(part);
                await Proc.Run("ffmpeg",args,ct);parts.Add(part);
            }
            string list=Path.Combine(folder,"edit.ffconcat");File.WriteAllLines(list,parts.Select(p=>"file '"+Path.GetFileName(p)+"'"),new UTF8Encoding(false));
            string tmp=Path.Combine(folder,"result."+ext);
            await Remux(["-f","concat","-safe","1","-i",list],tmp,ct,true);
            var dest=Home.Unique(Home.Exports,string.IsNullOrWhiteSpace(title)?"Edit":title,ext);File.Move(tmp,dest);return dest;
        }finally{Home.Clear(folder);}
    }
    public static async Task<(string File,double[] Peaks)> PreviewAudio(string path,Track track,string folder,double duration,CancellationToken ct)
    {
        var output=Path.Combine(folder,"track"+track.Index+".m4a");
        string map="0:"+track.Index;
        var si=Proc.Info("ffmpeg",["-v","error","-y","-i",path,"-map",map,"-c:a",track.Codec=="aac"?"copy":"aac",output,
            "-map",map,"-c:a","pcm_s16le","-ac","1","-ar","8000","-f","s16le","pipe:1"]);
        using var process=Process.Start(si)??throw new IOException("Could not open the audio track.");
        ChildJob.Track(process);
        using var reg=ct.Register(()=>{try{process.Kill(true);}catch(InvalidOperationException){}});
        var error=process.StandardError.ReadToEndAsync();
        double[] peaks=new double[960];byte[] data=new byte[32768];
        long sample=0;int carry=0;
        double samplesPerBin=Math.Max(1,duration*8000/peaks.Length);
        int read;
        while((read=await process.StandardOutput.BaseStream.ReadAsync(data.AsMemory(carry),ct).ConfigureAwait(false))>0){
            int count=read+carry;
            for(int pos=0;pos+1<count;pos+=2,sample++){
                int bin=Math.Min(peaks.Length-1,(int)(sample/samplesPerBin));
                double amp=Math.Abs((int)BitConverter.ToInt16(data,pos))/32768d;
                peaks[bin]=Math.Max(peaks[bin],amp);
            }
            carry=count%2;if(carry!=0)data[0]=data[count-1];
        }
        await process.WaitForExitAsync(ct).ConfigureAwait(false);var err=await error.ConfigureAwait(false);
        if(process.ExitCode!=0)throw new IOException(err);
        return (output,peaks);
    }
}


