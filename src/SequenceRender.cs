using System.Text;
namespace Moments;
internal static class SequenceRender
{
    public static async Task<string> Export(Sequence edit,string ext,Action<int,int> progress,CancellationToken ct)
    {
        edit.Validate();
        if(edit.End==0)throw new InvalidOperationException("Add a video to the timeline.");
        if(ext is not ("mp4" or "mkv" or "webm"))throw new ArgumentException("Unknown format.");
        var points=edit.Clips.SelectMany(c=>new[]{c.Start,c.End}).Append(0).Distinct().Order().ToArray();
        bool withAudio=edit.Clips.Any(c=>c.Lane<0 && !edit.Muted.Contains(c.Lane));
        string folder=Home.Temp("edit");
        try{
            var metadata=new Dictionary<string,ClipInfo>(StringComparer.OrdinalIgnoreCase);
            foreach(string file in edit.Clips.Select(c=>c.File).Distinct(StringComparer.OrdinalIgnoreCase))
                metadata[file]=await Media.Probe(file,ct);
            List<string> list=[];
            for(int i=0;i<points.Length-1;i++){
                ct.ThrowIfCancellationRequested();progress(i+1,points.Length-1);
                int start=points[i],frames=points[i+1]-start;
                string duration=Home.N((double)frames/edit.Fps),part=$"{i:D6}.mkv";
                var active=edit.Clips.Where(c=>c.Start<=start && c.End>start).OrderByDescending(c=>c.Lane).ToArray();
                var video=active.FirstOrDefault(c=>c.Lane>0);
                var audio=active.Where(c=>c.Lane<0 && !edit.Muted.Contains(c.Lane)).ToArray();
                List<string> args=["-hide_banner","-v","error","-y"];
                var inputs=new Dictionary<(string,int),int>();
                int Input(TimelineClip c){
                    int offset=c.In+start-c.Start;var key=(c.File,offset);
                    if(inputs.TryGetValue(key,out int ix))return ix;
                    if(!File.Exists(c.File))throw new FileNotFoundException("Source file unavailable: "+c.File);
                    args.AddRange(["-ss",Home.N((double)offset/edit.Fps),"-t",Home.N((double)frames/edit.Fps+1),"-i",c.File]);
                    return inputs[key]=inputs.Count;
                }
                List<string> graph=[];
                string correct=video!=null && metadata[video.File].LegacyRgbTags?Media.DesktopYuv+",":"";
                string source=video==null?$"color=c=black:s={edit.Width}x{edit.Height}:r={edit.Fps}":
                    $"[{Input(video)}:v:0]{correct}setpts=PTS-STARTPTS,fps={edit.Fps},scale={edit.Width}:{edit.Height}:force_original_aspect_ratio=decrease:force_divisible_by=2:out_color_matrix=bt709:out_range=tv,pad={edit.Width}:{edit.Height}:(ow-iw)/2:(oh-ih)/2,setsar=1,tpad=stop_mode=clone:stop_duration={duration}";
                graph.Add($"{source},trim=end_frame={frames},format=yuv420p,setparams=range=limited:colorspace=bt709:color_primaries=bt709:color_trc=iec61966-2-1[v]");
                if(withAudio){
                    for(int j=0;j<audio.Length;j++){
                        var a=audio[j];graph.Add($"[{Input(a)}:{a.Stream}]asetpts=PTS-STARTPTS,aresample=48000,aformat=sample_fmts=fltp:channel_layouts=stereo,apad,atrim=duration={duration}[a{j}]");
                    }
                    if(audio.Length==0)graph.Add($"anullsrc=r=48000:cl=stereo,atrim=duration={duration}[mix]");
                    else if(audio.Length==1)graph.Add("[a0]anull[mix]");
                    else graph.Add(string.Concat(Enumerable.Range(0,audio.Length).Select(n=>$"[a{n}]"))+$"amix=inputs={audio.Length}:normalize=0[mix]");
                }
                args.AddRange(["-filter_complex",string.Join(';',graph),"-map","[v]"]);
                if(withAudio)args.AddRange(["-map","[mix]","-c:a","pcm_s16le","-metadata:s:a:0","title=Mix"]);
                if(ext=="webm")args.AddRange(["-c:v","libvpx-vp9","-cpu-used","6","-row-mt","1","-crf","32","-b:v","0"]);
                else args.AddRange(["-c:v","libx264","-preset","veryfast","-crf","19","-bf","0"]);
                args.AddRange(["-threads","6","-t",duration,Path.Combine(folder,part)]);
                await Proc.Run("ffmpeg",args,ct);
                list.Add($"file '{part}'");list.Add("duration "+duration);
            }
            string manifest=Path.Combine(folder,"sequence.ffconcat"),result=Path.Combine(folder,"sequence."+ext);
            File.WriteAllLines(manifest,list,new UTF8Encoding(false));
            await Media.Remux(["-f","concat","-safe","1","-i",manifest],result,ct,true,(double)edit.End/edit.Fps);
            ct.ThrowIfCancellationRequested();
            string dest=Home.Unique(Home.Exports,edit.Title,ext);File.Move(result,dest);return dest;
        }finally{Home.Clear(folder);}
    }
}
