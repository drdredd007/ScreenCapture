global using System;
global using System.Collections.Generic;
global using System.Diagnostics;
global using System.IO;
global using System.Linq;
global using System.Threading;
global using System.Threading.Tasks;
using System.Globalization;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Text;
using Forms = System.Windows.Forms;

namespace Moments;
internal static class Home
{
    public static readonly string Root = AppContext.BaseDirectory;
    static readonly string Storage = Environment.GetCommandLineArgs().Contains("--self-test") ? Path.Combine(Root,"build","checks") : Root;
    public static readonly string Videos = Path.Combine(Storage, "Videos");
    public static readonly string Exports = Path.Combine(Videos, "Монтаж");
    public static readonly string Work = Path.Combine(Storage, "Cache");
    public static string Tool(string name) => Path.Combine(Root, "tools", name + ".exe");
    public static void Init()
    {
        foreach (var dir in new[]{Videos,Exports,Work}) Directory.CreateDirectory(dir);
    }
    public static string Temp(string kind)
    {
        string dir = Path.Combine(Work, kind + "_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
    public static void Clear(string dir)
    {
        if (Path.GetDirectoryName(Path.GetFullPath(dir)) != Work) return;
        try { Directory.Delete(dir,true); } catch(IOException) {} catch(UnauthorizedAccessException) {}
    }
    public static string Unique(string dir,string name,string ext)
    {
        string clean = new(name.Select(c=>Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).Take(84).ToArray());
        return Path.Combine(dir, $"{DateTime.Now:yyyy-MM-dd HH-mm-ss} · {clean.Trim()} · {Guid.NewGuid().ToString("N")[..6]}.{ext}");
    }
    public static string N(double value) => value.ToString("0.######",CultureInfo.InvariantCulture);
    public static string ClipTitle(string path)
    {
        string name=Path.GetFileNameWithoutExtension(path);var parts=name.Split(" · ");
        return parts.Length>=3 && DateTime.TryParseExact(parts[0],"yyyy-MM-dd HH-mm-ss",CultureInfo.InvariantCulture,DateTimeStyles.None,out _)
            ? string.Join(" · ",parts[1..^1]) : name;
    }
    public static string Time(double s) => TimeSpan.FromSeconds(Math.Max(0,s)).ToString(s>=3600 ? @"h\:mm\:ss" : @"mm\:ss");
    public static long Free => new DriveInfo(Path.GetPathRoot(Root)!).AvailableFreeSpace;
    public static void Log(string msg)
    {
        lock (typeof(Home)) try {
            string log=Path.Combine(Storage,"Moments.log");
            if(File.Exists(log) && new FileInfo(log).Length>2_000_000) File.Move(log,log+".1",true);
            File.AppendAllText(log,$"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {msg}{Environment.NewLine}");
        } catch(IOException) {}
    }
}
internal sealed record Profile(string Name,int Width,int Height,int Fps,int Mbps)
{
    public override string ToString()=>Name;
    public static readonly Profile[] All = [
        new("Compact   ·   720p / 30 FPS",1280,720,30,4),
        new("Standard   ·   1080p / 60 FPS",1920,1080,60,10),
        new("High   ·   1440p / 60 FPS",2560,1440,60,18)];
}
internal sealed class Settings
{
    public int Quality {get;set;}=1;
    public int Minutes {get;set;}=5;
    public string Hotkey {get;set;}="Ctrl + Shift + F8";
    public bool SystemAudio {get;set;}=true;
    public bool SeparateApps {get;set;}=true;
    public bool Mic {get;set;}=true;
    public bool Auto {get;set;}=true;
    public List<string> Games {get;set;}=[];
    public LolAutoSaveOptions LolAutoSave {get;set;}=new();
    public bool LibraryTiles {get;set;}=false;
    static string FileName=>Path.Combine(Home.Root,"settings.json");
    public static Settings Load()
    {
        try {var s=JsonSerializer.Deserialize<Settings>(File.ReadAllText(FileName))??new();
            s.Quality=Math.Clamp(s.Quality,0,Profile.All.Length-1); s.Minutes=s.Minutes==10?10:5;
            s.Games??=[]; s.LolAutoSave??=new(); return s;}
        catch(Exception ex) when (ex is IOException or JsonException){return new();}
    }
    public void Save(){File.WriteAllText(FileName+".tmp",JsonSerializer.Serialize(this,new JsonSerializerOptions{WriteIndented=true}));File.Move(FileName+".tmp",FileName,true);}
}
internal sealed record Target(nint Handle, string Title, System.Drawing.Rectangle Bounds, bool IsWindow=false, System.Drawing.Rectangle? Region=null)
{
    public override string ToString()=>Title;
}
internal static class Native
{
    internal delegate bool WindowCallback(nint hwnd,nint data);
    [DllImport("user32.dll")] static extern bool EnumWindows(WindowCallback cb,nint data);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(nint h,StringBuilder text,int max);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint h,out int pid);
    [DllImport("kernel32.dll")] static extern nint OpenProcess(uint access,bool inherit,int pid);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern bool QueryFullProcessImageName(nint process,uint flags,StringBuilder path,ref int length);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(nint handle);
    [DllImport("kernel32.dll")] static extern bool GetProcessTimes(nint h,out long created,out long exited,out long kernel,out long user);
    internal static long ProcessBirth(int pid){
        nint h=OpenProcess(0x1000,false,pid);if(h==0)return 0;
        try{return GetProcessTimes(h,out long created,out _,out _,out _)?created:0;}finally{CloseHandle(h);}
    }
    internal static string? ImagePath(int pid)
    {
        var h=OpenProcess(0x1000,false,pid); // PROCESS_QUERY_LIMITED_INFORMATION
        if(h==0)return null;
        try{int n=32768;var path=new StringBuilder(n);return QueryFullProcessImageName(h,0,path,ref n)?path.ToString():null;}
        finally{CloseHandle(h);}
    }
    [DllImport("user32.dll")] static extern nint MonitorFromPoint(System.Drawing.Point p,uint flags);
    [DllImport("user32.dll")] static extern bool GetClientRect(nint h,out Rect r);
    [StructLayout(LayoutKind.Sequential)] struct Rect{public int L,T,R,B;}
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(nint h,int attr,out int val,int size);
    [DllImport("user32.dll",SetLastError=true)] public static extern bool RegisterHotKey(nint h,int id,uint mod,uint vk);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(nint h,int id);
    public static Target[] Monitors()=>Forms.Screen.AllScreens.OrderByDescending(s=>s.Primary).Select((s,i)=>new Target(MonitorFromPoint(s.Bounds.Location,2),$"Display {i+1}   ·   {s.Bounds.Width} × {s.Bounds.Height}"+(s.Primary?"   ·   primary":""),s.Bounds)).ToArray();
    public static Target[] Windows(int? onlyPid=null)
    {
        List<Target> items=[];
        EnumWindows((h,_)=>{
            GetWindowThreadProcessId(h,out int pid);
            if(pid==Environment.ProcessId || (onlyPid.HasValue && onlyPid!=pid) || !IsWindowVisible(h) || IsIconic(h)) return true;
            if(DwmGetWindowAttribute(h,14,out int cloaked,4)==0 && cloaked!=0) return true;
            StringBuilder title=new(512); GetWindowText(h,title,title.Capacity);
            if(title.Length>0 && GetClientRect(h,out var r) && r.R>=32 && r.B>=32)
                items.Add(new(h,title.ToString(),new(0,0,r.R,r.B),true));
            return true;
        },0); return items.ToArray();
    }
}
internal static class Proc
{
    public static ProcessStartInfo Info(string exe,IEnumerable<string> args)
    {
        var si=new ProcessStartInfo(Home.Tool(exe)){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,RedirectStandardOutput=true,WorkingDirectory=Home.Root};
        si.Environment["TEMP"]=si.Environment["TMP"]=Home.Work;
        foreach(var a in args) si.ArgumentList.Add(a);
        return si;
    }
    public static async Task<string> Run(string exe,IEnumerable<string> args,CancellationToken ct=default)
    {
        using var p=Process.Start(Info(exe,args))??throw new IOException("Could not start "+exe);
        ChildJob.Track(p);
        using var reg=ct.Register(()=>{try{p.Kill(true);}catch(InvalidOperationException){}});
        var output=p.StandardOutput.ReadToEndAsync();var error=p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync(); string err=await error;string result=await output;
        ct.ThrowIfCancellationRequested();
        if(p.ExitCode!=0){Home.Log(err);throw new IOException(err.Trim().Length>1000?err.Trim()[^1000..]:err.Trim());}
        return result;
    }
}


