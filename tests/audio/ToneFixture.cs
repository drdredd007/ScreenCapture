using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Windows.Forms;
class ToneFixture : Form
{
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h,int cmd);
    readonly bool chat=Process.GetCurrentProcess().ProcessName=="ToneChat";
    readonly string dir=AppDomain.CurrentDomain.BaseDirectory;
    SoundPlayer player; MemoryStream wav;
    Process child; readonly Timer tick=new Timer{Interval=60};
    ToneFixture(){
        Text=chat?"QA chat tone":"QA game tone";ClientSize=new Size(400,220);BackColor=Color.Teal;
        ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;Location=new Point(30,30);
        Load+=delegate{wav=Tone(chat?991:431);player=new SoundPlayer(wav);player.Load();player.PlayLooping();if(!chat)BeginInvoke(new Action(delegate{ShowWindow(Handle,4);}));tick.Start();};
        tick.Tick+=delegate{
            if(File.Exists(Path.Combine(dir,"quit.test")) || (chat && File.Exists(Path.Combine(dir,"leave.test")))){Close();return;}
            if(!chat && !File.Exists(Path.Combine(dir,"siblings.test")) && File.Exists(Path.Combine(dir,"join.test")) && !File.Exists(Path.Combine(dir,"leave.test")) && (child==null||child.HasExited))
                child=Process.Start(new ProcessStartInfo(Path.Combine(dir,"ToneChat.exe")){UseShellExecute=false,WindowStyle=ProcessWindowStyle.Hidden});
        };
        FormClosed+=delegate{tick.Stop();if(player!=null){player.Stop();player.Dispose();}if(wav!=null)wav.Dispose();};
    }
    static MemoryStream Tone(int hz){
        const int rate=48000;var s=new MemoryStream();var w=new BinaryWriter(s);w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));w.Write(36+rate*4);w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));w.Write(16);w.Write((short)1);w.Write((short)2);w.Write(rate);w.Write(rate*4);w.Write((short)4);w.Write((short)16);w.Write(System.Text.Encoding.ASCII.GetBytes("data"));w.Write(rate*4);
        for(int i=0;i<rate;i++){short v=(short)(350*Math.Sin(2*Math.PI*hz*i/rate));w.Write(v);w.Write(v);}s.Position=0;return s;
    }
    [STAThread] static void Main(){
        if(Process.GetCurrentProcess().ProcessName!="ToneLauncher"){Application.Run(new ToneFixture());return;}
        var dir=AppDomain.CurrentDomain.BaseDirectory;Process chat=null;
        using(var timer=new Timer{Interval=70}){
            timer.Tick+=delegate{
                if(File.Exists(Path.Combine(dir,"quit.test"))){Application.ExitThread();return;}
                if(File.Exists(Path.Combine(dir,"siblings.test")) && File.Exists(Path.Combine(dir,"join.test")) && !File.Exists(Path.Combine(dir,"leave.test")) && (chat==null||chat.HasExited))
                    chat=Process.Start(new ProcessStartInfo(Path.Combine(dir,"ToneChat.exe")){UseShellExecute=false,WindowStyle=ProcessWindowStyle.Hidden});
            };timer.Start();Application.Run();
        }
    }
}
