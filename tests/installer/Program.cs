using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using Moments;

static class Checks
{
    static void Require(bool ok,string message){if(!ok)throw new Exception(message);}
    static void Reject(Action action,string message){try{action();}catch(Exception ex) when(ex is IOException or InvalidDataException or OperationCanceledException){return;}throw new Exception(message);}
    static void Until(Func<bool> ready){var watch=Stopwatch.StartNew();while(!ready()){if(watch.Elapsed.TotalSeconds>20)throw new Exception("Timed out");Thread.Sleep(50);}}
    static Process Start(string exe,params string[] args){var si=new ProcessStartInfo(exe){UseShellExecute=false};foreach(string arg in args)si.ArgumentList.Add(arg);return Process.Start(si)!;}
    static void Populate(string stage,string helper,bool real=false){
        Directory.CreateDirectory(stage);
        foreach(string file in UpdatePackage.Files){string dest=Path.Combine(stage,file);Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            if(UpdatePackage.HelperFiles.Contains(file))File.Copy(Path.Combine(helper,file),dest);
            else if(file.StartsWith("Moments."))File.Copy(Path.Combine(real?helper:AppContext.BaseDirectory,file),dest);
            else File.WriteAllText(dest,"new "+file);
        }
    }
    static UpdatePlan Plan(string target,string stage,Process parent)=>new(target,stage,UpdatePackage.Files.ToDictionary(f=>f,f=>UpdatePackage.Hash(Path.Combine(stage,f))),parent.Id,parent.StartTime.ToUniversalTime().Ticks);
    public static int Main(string[] args){
        if(args[0]=="--parent"){Until(()=>File.Exists(args[1]));return 0;}
        if(args[0]=="--updated"){
            using var singleton=new Mutex(true,@"Local\MomentsStudio_FMoments",out bool first);if(!first)return 4;
            if(File.Exists(Path.Combine(AppContext.BaseDirectory,"fail-start")))return 2;
            File.WriteAllText(args[1],"ready");return 0;
        }
        if(args[0]=="--update-failed"){using var singleton=new Mutex(true,@"Local\MomentsStudio_FMoments",out bool first);if(!first)return 4;File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"restored"),"yes");return 0;}
        string root=Path.Combine(Path.GetTempPath(),"Moments update тест ' & "+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try{
            ArchiveChecks(root);
            TransactionChecks(root);
            ProcessCheck(root,args[0],false,false);
            ProcessCheck(root,args[0],true,false);
            ProcessCheck(root,args[0],false,true);
            ProcessCheck(root,args[0],false,false,true);
            Console.WriteLine("PASS: updater archive validation, tamper detection, rollback, user-data preservation, actual parent exit/replace/restart handshake, failed startup rollback and cancellation.");return 0;
        }finally{Directory.Delete(root,true);}
    }
    static void ArchiveChecks(string root){
        int index=0;
        void Zip(string extra,bool duplicate=false,bool symlink=false){
            string archive=Path.Combine(root,"input.zip");using(var zip=ZipFile.Open(archive,ZipArchiveMode.Create)){
                foreach(string file in UpdatePackage.Files){using var text=new StreamWriter(zip.CreateEntry(file).Open());text.Write("payload");}
                if(extra.Length>0){var entry=zip.CreateEntry(extra);if(symlink)entry.ExternalAttributes=0xA000<<16;using var text=new StreamWriter(entry.Open());text.Write("bad");}
            }
            string stage=Path.Combine(root,"extract"+(index++));
            if(extra.Length>0)Reject(()=>UpdatePackage.Extract(archive,stage,default),"unsafe archive accepted: "+extra);
            else {UpdatePackage.Extract(archive,stage,default);Require(UpdatePackage.Files.All(f=>File.Exists(Path.Combine(stage,f))),"missing extraction");}
            File.Delete(archive);
        }
        Zip("");foreach(string bad in new[]{"../outside","/absolute","C:/absolute","settings.json","Videos/clip.mp4","Moments.exe","MOMENTS.exe","tools/../Moments.exe","Moments.exe:stream"})Zip(bad);
        Zip("README.md",symlink:true);
        string incomplete=Path.Combine(root,"incomplete.zip");using(var zip=ZipFile.Open(incomplete,ZipArchiveMode.Create)){using var text=new StreamWriter(zip.CreateEntry("Moments.exe").Open());text.Write("test");}
        Reject(()=>UpdatePackage.Extract(incomplete,Path.Combine(root,"incomplete"),default),"incomplete archive accepted");
        using var cancel=new CancellationTokenSource();cancel.Cancel();Reject(()=>UpdatePackage.Extract(incomplete,Path.Combine(root,"canceled"),cancel.Token),"cancellation ignored");
    }
    static void TransactionChecks(string root){
        foreach(bool locked in new[]{false,true}){
            string job=Path.Combine(root,"transaction"+locked),target=Path.Combine(job,"target"),stage=Path.Combine(job,"stage");Directory.CreateDirectory(target);Directory.CreateDirectory(stage);
            foreach(string file in UpdatePackage.Files){foreach(string dir in new[]{target,stage})Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(dir,file))!);
                File.WriteAllText(Path.Combine(stage,file),"new");File.WriteAllText(Path.Combine(target,file),"old");}
            File.WriteAllText(Path.Combine(target,"settings.json"),"keep");using var parent=Process.GetCurrentProcess();var plan=Plan(target,stage,parent);
            File.WriteAllText(Path.Combine(stage,"README.md"),"tampered");Reject(()=>UpdatePackage.Install(plan,()=>{}),"stage tampering accepted");File.WriteAllText(Path.Combine(stage,"README.md"),"new");
            using(var fileLock=locked?new FileStream(Path.Combine(target,"README.md"),FileMode.Open,FileAccess.Read,FileShare.Read):null){
                Reject(()=>UpdatePackage.Install(plan,()=>{throw new IOException("restart failure");}),"failed installation succeeded");
                Require(UpdatePackage.Files.All(f=>File.ReadAllText(Path.Combine(target,f))=="old"),"rollback left partial files");
            }
            Require(File.ReadAllText(Path.Combine(target,"settings.json"))=="keep","settings changed");
        }
    }
    static void ProcessCheck(string root,string helper,bool fail,bool cancel,bool real=false){
        string job=Path.Combine(root,$"process-{fail}-{cancel}-{real}"),target=Path.Combine(root,$"installed-{fail}-{cancel}-{real}"),stage=Path.Combine(job,"stage");Directory.CreateDirectory(job);
        Populate(target,helper,real);Populate(stage,helper,real);File.WriteAllText(Path.Combine(target,"README.md"),"old");
        File.WriteAllText(Path.Combine(target,"settings.json"),"keep");Directory.CreateDirectory(Path.Combine(target,"Videos"));File.WriteAllText(Path.Combine(target,"Videos","clip.mp4"),"keep video");
        if(fail)File.WriteAllText(Path.Combine(target,"fail-start"),"");
        foreach(string file in UpdatePackage.HelperFiles)File.Copy(Path.Combine(helper,file),Path.Combine(job,file));
        string exit=Path.Combine(job,"exit-parent");using var parent=Start(Environment.ProcessPath!,"--parent",exit);
        try{
            var plan=Plan(target,stage,parent);string manifest=Path.Combine(job,"plan.json");File.WriteAllText(manifest,JsonSerializer.Serialize(plan));
            using var updater=Start(Path.Combine(job,"Moments.Updater.exe"),manifest);
            Until(()=>File.Exists(Path.Combine(job,"ready"))||updater.HasExited);
            Require(!updater.HasExited,"updater preparation failed");
            File.WriteAllText(Path.Combine(job,cancel?"cancel":"proceed"),"");Thread.Sleep(250);
            Require(File.ReadAllText(Path.Combine(target,"README.md"))=="old","files replaced before parent exit");
            File.WriteAllText(exit,"");Require(updater.WaitForExit(20000),"updater hung");
            if(cancel)Require(updater.ExitCode==3 && File.ReadAllText(Path.Combine(target,"README.md"))=="old","canceled install changed files");
            else if(fail){Until(()=>File.Exists(Path.Combine(target,"restored")));Require(updater.ExitCode==1 && File.ReadAllText(Path.Combine(target,"README.md"))=="old","failed startup did not restore old version");}
            else Require(updater.ExitCode==0 && File.Exists(Path.Combine(job,"started")) && File.ReadAllText(Path.Combine(target,"README.md"))=="new README.md","update/restart failed");
            Require(File.ReadAllText(Path.Combine(target,"settings.json"))=="keep" && File.ReadAllText(Path.Combine(target,"Videos","clip.mp4"))=="keep video","update changed user data");
            if(real){
                using var app=Process.GetProcessById(int.Parse(File.ReadAllText(Path.Combine(job,"started"))));
                try{Require(app.CloseMainWindow(),"updated WPF window did not open");Require(app.WaitForExit(15000),"updated WPF application did not close normally");}
                finally{if(!app.HasExited){app.Kill(true);app.WaitForExit();}}
                Console.WriteLine("PASS: production Moments.exe restarted, acknowledged startup, opened its WPF window and closed normally after installation.");
            }
        }finally{if(!parent.HasExited){parent.Kill();parent.WaitForExit();}}
    }
}
