using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Moments;

internal static class AudioCompatibilityChecks
{
    static void Check(bool ok,string why){if(!ok)throw new Exception(why);}
    public static async Task Run(Action<string> note)
    {
        string dir=Home.Temp("audio-compatibility");
        try{
            string input=Path.Combine(dir,"silent-first.mp4"),output=Path.Combine(dir,"compatible.mp4");
            await Proc.Run("ffmpeg",["-v","error","-y","-f","lavfi","-i","color=size=64x64:rate=30",
                "-f","lavfi","-i","anullsrc=r=44100:cl=mono","-f","lavfi","-i","sine=frequency=880:sample_rate=44100",
                "-map","0:v","-map","1:a","-map","2:a","-t","2","-c:v","libx264","-preset","ultrafast",
                "-c:a","aac","-metadata:s:a:0","handler_name=Silent system","-metadata:s:a:1","handler_name=Audible mic",input]);
            await Media.Remux(["-i",input],output);
            var info=await Media.Probe(output);
            Check(info.Tracks.Select(t=>t.Name).SequenceEqual(new[]{"Silent system","Audible mic"}),"Compatibility mix leaked into editable tracks");
            Check(info.Tracks.Select(t=>t.Index).SequenceEqual(new[]{2,3}),"Editable stream indices changed after hiding mix");
            using var doc=JsonDocument.Parse(await Proc.Run("ffprobe",["-v","error","-select_streams","a","-show_streams","-of","json",output]));
            var audio=doc.RootElement.GetProperty("streams").EnumerateArray().ToArray();
            Check(audio.Length==3,"Expected playback mix plus two original stems");
            for(int i=0;i<audio.Length;i++){
                var a=audio[i];
                Check(a.GetProperty("codec_name").GetString()=="aac" && a.GetProperty("profile").GetString()=="LC" &&
                    a.GetProperty("sample_rate").GetString()=="48000" && a.GetProperty("channels").GetInt32()==2,"AAC compatibility profile");
                Check(a.GetProperty("disposition").GetProperty("default").GetInt32()==(i==0?1:0),"Wrong default playback track");
            }
            Check(await Peak(output,1,dir)>1000,"Default track is silent when only second source has audio");
            Check(await Peak(output,2,dir)<10,"Silent stem changed");
            Check(await Peak(output,3,dir)>1000,"Audible editable stem missing");
            string second=Path.Combine(dir,"saved-again.mp4");await Media.Remux(["-i",output],second);
            var twice=await Media.Probe(second);Check(twice.Tracks.Count==2,"Remux duplicated playback mix");
            int firstPeak=await Peak(output,1,dir),secondPeak=await Peak(second,1,dir);
            Check(secondPeak<firstPeak*1.3,"Remux mixed its own playback track twice");
            var original=await Media.Probe(input);
            var preview=await Media.PreviewAudio(input,original.Tracks[1],dir,original.Duration,CancellationToken.None);
            Check(Path.GetExtension(preview.File)==".wav" && preview.Peaks.Max()>.05,"Old AAC file cannot prepare audible PCM preview");
            using var wav=JsonDocument.Parse(await Proc.Run("ffprobe",["-v","error","-show_streams","-of","json",preview.File]));
            var stream=wav.RootElement.GetProperty("streams")[0];
            Check(stream.GetProperty("codec_name").GetString()=="pcm_s16le" && stream.GetProperty("sample_rate").GetString()=="48000" &&
                stream.GetProperty("channels").GetInt32()==2,"Preview still depends on Windows AAC decoder");
            Check(await Peak(preview.File,0,dir)>1000,"PCM preview is silent");
            // Silent first stems must not hide sound, and disabling a source must
            // exclude it from both the compatibility mix and editable outputs.
            string exported=await Media.Export(input,"Audio compatibility test","mp4",[new(0,1)], [original.Tracks[0]],(_,_)=>{},CancellationToken.None);
            try{Check((await Media.Probe(exported)).Tracks.Count==1 && await Peak(exported,1,dir)<10,"Export included disabled microphone");}
            finally{File.Delete(exported);}
            note("AAC-LC stereo default mix is audible with a silent first stem; editable tracks stay separate; repeated saves do not double the mix; old AAC previews decode to PCM.");
        }finally{Home.Clear(dir);}
    }
    static async Task<int> Peak(string file,int stream,string dir){
        string raw=Path.Combine(dir,"measure.pcm");
        await Proc.Run("ffmpeg",["-v","error","-y","-i",file,"-map","0:"+stream,"-ac","1","-ar","48000","-f","s16le",raw]);
        byte[] bytes=File.ReadAllBytes(raw);int peak=0;
        for(int i=0;i+1<bytes.Length;i+=2)peak=Math.Max(peak,Math.Abs((int)BitConverter.ToInt16(bytes,i)));
        return peak;
    }
}
