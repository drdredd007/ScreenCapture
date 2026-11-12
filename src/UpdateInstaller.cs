using System.Reflection;
using System.Text.Json;
namespace Moments;

internal interface IUpdateInstaller
{
    Task<string> Prepare(string archive,Version version,CancellationToken token);
    Task Launch(string plan,CancellationToken token);
}
internal sealed class UpdateInstaller : IUpdateInstaller
{
    public async Task<string> Prepare(string archive,Version version,CancellationToken token)=>await Task.Run(()=>{
        string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Moments","Updates");
        Directory.CreateDirectory(root);UpdatePackage.NoLinks(root);
        // Completed helper jobs contain only the small updater binaries. Keep
        // failed jobs and their backups for diagnosis/recovery.
        foreach(string old in Directory.GetDirectories(root))if(File.Exists(Path.Combine(old,"complete")))
            try{Directory.Delete(old,true);}catch(IOException){}catch(UnauthorizedAccessException){}
        string job=Path.Combine(root,Guid.NewGuid().ToString("N")),stage=Path.Combine(job,"stage");Directory.CreateDirectory(job);
        try{
            UpdatePackage.Extract(archive,stage,token);
            var actual=AssemblyName.GetAssemblyName(Path.Combine(stage,"Moments.dll")).Version!;
            if(new Version(actual.Major,actual.Minor,actual.Build)!=version)throw new InvalidDataException("Update assembly version does not match its release.");
            var process=Process.GetCurrentProcess();
            var plan=new UpdatePlan(Path.GetFullPath(Home.Root),stage,UpdatePackage.Files.ToDictionary(f=>f,f=>UpdatePackage.Hash(Path.Combine(stage,f))),process.Id,process.StartTime.ToUniversalTime().Ticks);
            UpdatePackage.Validate(plan);
            // Fail while Moments is still running if its folder is read-only.
            foreach(string dir in new[]{plan.Target,Path.Combine(plan.Target,"tools")}){
                string probe=Path.Combine(dir,Guid.NewGuid().ToString("N")+".update");using(File.Create(probe)){}File.Delete(probe);
            }
            foreach(string file in UpdatePackage.HelperFiles)File.Copy(Path.Combine(Home.Root,file),Path.Combine(job,file));
            string path=Path.Combine(job,"plan.json");File.WriteAllText(path,JsonSerializer.Serialize(plan));token.ThrowIfCancellationRequested();return path;
        }catch{Directory.Delete(job,true);throw;}
    },token);
    public async Task Launch(string plan,CancellationToken token){
        string job=Path.GetDirectoryName(plan)!;
        var si=new ProcessStartInfo(Path.Combine(job,"Moments.Updater.exe")){UseShellExecute=false,WorkingDirectory=job};si.ArgumentList.Add(plan);
        using var helper=Process.Start(si)??throw new IOException("Could not start the updater.");
        // Not tracked by ChildJob: the updater must outlive Moments.
        try{
            var deadline=Stopwatch.StartNew();
            while(!File.Exists(Path.Combine(job,"ready"))){
                token.ThrowIfCancellationRequested();if(helper.HasExited)throw new IOException("Updater could not prepare. "+(File.Exists(Path.Combine(job,"error.txt"))?File.ReadAllText(Path.Combine(job,"error.txt")):""));
                if(deadline.Elapsed>TimeSpan.FromSeconds(15))throw new TimeoutException("Updater did not become ready.");
                await Task.Delay(100,token);
            }
            token.ThrowIfCancellationRequested();File.WriteAllText(Path.Combine(job,"proceed"),"");
        }catch{File.WriteAllText(Path.Combine(job,"cancel"),"");throw;}
    }
}
