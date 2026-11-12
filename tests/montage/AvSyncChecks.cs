using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Moments;

// Measure decoded flash/tone onsets, not merely equal stream durations. No audio
// device/GPU is needed: the video fixture flashes against the wall clock while
// PCM and application stems contain the same events on the capture sample clock.
internal static class AvSyncChecks
{
    static readonly double[] Events=[2.5,4.5];
    static string N(double n)=>n.ToString("F7",CultureInfo.InvariantCulture);
    static void Check(bool ok,string why){if(!ok)throw new Exception(why);}
    public static async Task Run(Action<string> note)
    {
        string folder=Home.Temp("avsync");
        try{
            foreach(var (delay,corrected) in new[]{(900,false),(50,true),(1250,true)}){
                string dir=Path.Combine(folder,$"{delay}-{corrected}");Directory.CreateDirectory(dir);
                var archive=AppAudioArchive.Create(dir,true,"Fixture");
                string pcm=Path.Combine(dir,"source.pcm");
                using(var writer=new BinaryWriter(File.Create(pcm))){
                    var block=new int[960];
                    for(int first=0;first<6*48000;first+=480){
                        for(int i=0;i<480;i++){
                            double t=(first+i)/48000d;
                            short sample=Events.Any(e=>t>=e && t<e+.12)?(short)(14000*Math.Sin(2*Math.PI*800*t)):(short)0;
                            block[i*2]=block[i*2+1]=sample;
                            for(int channel=0;channel<4;channel++)writer.Write(sample);
                        }
                        archive.Write("Fixture.exe",first,block);
                    }
                }
                archive.Flush(true);
                var epoch=new CaptureEpoch();
                await Task.Delay(delay); // Variable audio/video initialization skew.
                Check(Math.Abs(epoch.SamplesNow/48000d-epoch.Clock.Elapsed.TotalSeconds)<.01,"sample clock drifted from elapsed clock");
                string flashes=string.Join("+",Events.Select(e=>$"between(time(0),{N(epoch.UnixSeconds+e)},{N(epoch.UnixSeconds+e+.12)})"));
                string video=$"color=black:size=64x64:rate=30,realtime,drawbox=color=white:t=fill:enable='{flashes}'";
                if(corrected)video+=","+epoch.VideoFilter;
                string graph=video+"[v];[0:a]asplit=2[s][m];[s]pan=stereo|c0=c0|c1=c1[sys];[m]pan=stereo|c0=c2|c1=c3[mic]";
                string raw=Path.Combine(dir,"take.mkv");
                await Proc.Run("ffmpeg",["-v","error","-y","-f","s16le","-ar","48000","-ac","4","-i",pcm,
                    "-filter_complex",graph,"-map","[v]","-map","[sys]","-map","[mic]","-t","6",
                    "-c:v","libx264","-preset","ultrafast","-bf","0","-g","60","-r","30","-fps_mode","cfr",
                    "-force_key_frames","expr:gte(t,n_forced*2)","-c:a","aac","-b:a","160k",raw]);
                var baseline=await Measure(raw,0);
                if(!corrected){
                    Check(baseline.All(d=>d>.6),"fixture failed to reproduce the old late-audio bug");
                    note("Old independent origins reproduced: audio late by "+string.Join(", ",baseline.Select(d=>N(d)+" s")));continue;
                }
                await Verify(raw,2,note,$"shared epoch / startup {delay} ms");
                string saved=Path.Combine(dir,"saved.mp4");await Media.Remux(["-i",raw],saved);
                await Verify(saved,2,note,"saved MP4");
                string apps=Path.Combine(dir,"apps.mp4");await archive.Mux(["-i",raw],apps,0,6);
                await Verify(apps,2,note,"application stem + microphone MP4");
                // Exercise reset timestamps, concat boundaries and non-keyframe replay seek.
                await Proc.Run("ffmpeg",["-v","error","-y","-i",raw,"-map","0","-c","copy","-f","segment","-segment_time","2","-reset_timestamps","1",Path.Combine(dir,"part%03d.mkv")]);
                string list=Path.Combine(dir,"replay.ffconcat");
                File.WriteAllLines(list,Directory.GetFiles(dir,"part*.mkv").Order().Select(f=>"file '"+Path.GetFileName(f)+"'\nduration 2"));
                string[] input=["-f","concat","-safe","1","-ss","1.25","-i",list];
                string replay=Path.Combine(dir,"replay.mp4");await Media.Remux(input,replay,duration:4.25);
                await Verify(replay,2,note,"replay MP4 across segment boundary");
                string appReplay=Path.Combine(dir,"app-replay.mp4");await archive.Mux(input,appReplay,1.25,4.25);
                await Verify(appReplay,2,note,"application replay + microphone");
            }
            // The anchor must only be evaluated once. A wall-clock adjustment or
            // a slow encoder later in the recording cannot retime individual frames.
            Check(CaptureEpoch.VideoFilterAt(123).Contains("if(eq(N,0),st(0,") && CaptureEpoch.VideoFilterAt(123).EndsWith("ld(0))'"),"video clock is not anchored once");
        }finally{Home.Clear(folder);}
    }
    static async Task Verify(string file,int tracks,Action<string> note,string label){
        for(int i=0;i<tracks;i++){
            var offsets=await Measure(file,i);
            Check(offsets.All(d=>Math.Abs(d)<.10),label+" track "+i+" A/V offset: "+string.Join(", ",offsets));
            Check(Math.Abs(offsets[^1]-offsets[0])<.07,label+" offset grew during recording");
            note(label+$" / audio {i}: "+string.Join(", ",offsets.Select(d=>(d*1000).ToString("F1",CultureInfo.InvariantCulture)+" ms")));
        }
    }
    static async Task<double[]> Times(string file,string stream){
        string json=await Proc.Run("ffprobe",["-v","error","-select_streams",stream,"-show_frames","-show_entries","frame=best_effort_timestamp_time","-of","json",file]);
        using var doc=JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("frames").EnumerateArray().Where(f=>f.TryGetProperty("best_effort_timestamp_time",out _))
            .Select(f=>double.Parse(f.GetProperty("best_effort_timestamp_time").GetString()!,CultureInfo.InvariantCulture)).ToArray();
    }
    static async Task<double[]> Measure(string file,int audio){
        string pixels=file+".gray",sound=file+".pcm";
        await Proc.Run("ffmpeg",["-v","error","-y","-i",file,"-map","0:v:0","-vf","scale=1:1,format=gray","-fps_mode","passthrough","-f","rawvideo",pixels]);
        await Proc.Run("ffmpeg",["-v","error","-y","-i",file,"-map","0:a:"+audio,"-ar","48000","-ac","1","-f","s16le",sound]);
        byte[] rgb=File.ReadAllBytes(pixels),pcm=File.ReadAllBytes(sound);
        var videoTimes=await Times(file,"v:0");var audioTimes=await Times(file,"a:"+audio);
        Check(rgb.Length==videoTimes.Length,"decoded video timestamp count mismatch");
        var visual=new List<double>();var audible=new List<double>();
        for(int i=0;i<rgb.Length;i++)if(rgb[i]>128 && (i==0 || rgb[i-1]<=128))visual.Add(videoTimes[i]);
        int last=-48000;
        for(int i=0;i<pcm.Length/2;i++)if(Math.Abs((int)BitConverter.ToInt16(pcm,i*2))>2000){
            if(i-last>2400)audible.Add(audioTimes[0]+i/48000d);last=i;
        }
        Check(visual.Count==2 && audible.Count==2,$"expected two flash/tone events: {visual.Count}/{audible.Count} in {file}");
        return visual.Zip(audible,(v,a)=>a-v).ToArray();
    }
}
