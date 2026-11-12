using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace Moments;

internal sealed record UpdatePlan(string Target,string Stage,Dictionary<string,string> Hashes,int ParentId,long ParentStarted);
internal static class UpdatePackage
{
    public static readonly string[] HelperFiles=["Moments.Updater.exe","Moments.Updater.dll","Moments.Updater.deps.json","Moments.Updater.runtimeconfig.json"];
    public static readonly string[] Files=["Moments.exe","Moments.dll","Moments.deps.json","Moments.runtimeconfig.json","README.md",
        "tools/ffmpeg.exe","tools/ffprobe.exe","tools/LICENSE","tools/README.txt",..HelperFiles];
    public static string Hash(string file){using var stream=File.OpenRead(file);return Convert.ToHexString(SHA256.HashData(stream));}
    public static void NoLinks(string path){
        for(string? p=Path.GetFullPath(path);p!=null;p=Path.GetDirectoryName(p))
            if((File.Exists(p)||Directory.Exists(p)) && (File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)
                throw new IOException("Updating through a symbolic link or junction is not supported: "+p);
    }
    public static void Extract(string archive,string stage,CancellationToken token){
        Directory.CreateDirectory(stage);NoLinks(stage);
        using var zip=ZipFile.OpenRead(archive);var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);long total=0;
        foreach(var entry in zip.Entries){
            token.ThrowIfCancellationRequested();string name=entry.FullName.Replace('\\','/');
            if(name=="tools/" && entry.Length==0)continue;
            if(!Files.Contains(name,StringComparer.Ordinal) || !seen.Add(name) || entry.Length<=0 ||
                ((entry.ExternalAttributes>>16)&0xF000)==0xA000 || (entry.ExternalAttributes&0x400)!=0)
                throw new InvalidDataException("Unexpected or duplicate update entry: "+entry.FullName);
            total=checked(total+entry.Length);if(total>1024L*1024*1024)throw new InvalidDataException("Update expands beyond the allowed size.");
            string path=Path.Combine(stage,name);Directory.CreateDirectory(Path.GetDirectoryName(path)!);NoLinks(path);
            using var input=entry.Open();using var output=new FileStream(path,FileMode.CreateNew);byte[] buffer=new byte[65536];int count;long written=0;
            while((count=input.Read(buffer))>0){token.ThrowIfCancellationRequested();written+=count;
                if(written>entry.Length)throw new InvalidDataException("Invalid update entry size.");output.Write(buffer,0,count);}
            if(written!=entry.Length)throw new InvalidDataException("Incomplete update entry.");
        }
        if(seen.Count!=Files.Length)throw new InvalidDataException("The update package is incomplete.");
    }
    public static void Validate(UpdatePlan plan){
        if(!Path.IsPathFullyQualified(plan.Target)||!Path.IsPathFullyQualified(plan.Stage))throw new InvalidDataException("Invalid update paths.");
        NoLinks(plan.Target);NoLinks(plan.Stage);
        string target=Path.TrimEndingDirectorySeparator(Path.GetFullPath(plan.Target))+Path.DirectorySeparatorChar;
        if((Path.TrimEndingDirectorySeparator(Path.GetFullPath(plan.Stage))+Path.DirectorySeparatorChar).StartsWith(target,StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Update staging must be outside the installation.");
        if(plan.Hashes.Count!=Files.Length || Files.Any(f=>!plan.Hashes.ContainsKey(f)))throw new InvalidDataException("Invalid update manifest.");
        foreach(string file in Files){
            NoLinks(Path.Combine(plan.Target,file));NoLinks(Path.Combine(plan.Stage,file));
            if(Hash(Path.Combine(plan.Stage,file))!=plan.Hashes[file])throw new InvalidDataException("Prepared update was modified: "+file);
        }
    }
    public static UpdatePlan Read(string file)=>JsonSerializer.Deserialize<UpdatePlan>(File.ReadAllText(file))??throw new InvalidDataException("Missing update plan.");
    public static void Install(UpdatePlan plan,Action restart,Action<int>? beforeReplace=null){
        Validate(plan);string backup=Path.Combine(Path.GetDirectoryName(plan.Stage)!,"backup");
        Directory.CreateDirectory(backup);var existed=new HashSet<string>();var changed=new List<string>();
        // Back up every existing program file before making any changes. Never
        // enumerate/delete user folders or settings; only the fixed manifest.
        foreach(string file in Files){string dest=Path.Combine(plan.Target,file);if(File.Exists(dest)){
            string copy=Path.Combine(backup,file);Directory.CreateDirectory(Path.GetDirectoryName(copy)!);File.Copy(dest,copy,false);existed.Add(file);}}
        try{
            foreach(string file in Files){
                beforeReplace?.Invoke(changed.Count);string dest=Path.Combine(plan.Target,file);NoLinks(dest);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);string temp=dest+"."+Guid.NewGuid().ToString("N")+".update";
                try{File.Copy(Path.Combine(plan.Stage,file),temp);File.Move(temp,dest,true);changed.Add(file);}
                finally{if(File.Exists(temp))File.Delete(temp);}
            }
            restart();
        }catch(Exception failure){
            var errors=new List<Exception>{failure};
            foreach(string file in changed.AsEnumerable().Reverse())try{
                string dest=Path.Combine(plan.Target,file);NoLinks(dest);
                if(existed.Contains(file))File.Copy(Path.Combine(backup,file),dest,true);else File.Delete(dest);
            }catch(Exception ex){errors.Add(ex);}
            if(errors.Count>1)throw new AggregateException("Update and rollback failed. Backups are retained at "+backup,errors);
            throw new IOException("Update failed; previous program files were restored. "+failure.Message,failure);
        }
        // Cleanup failure must not undo an already running, healthy update.
        try{Directory.Delete(backup,true);Directory.Delete(plan.Stage,true);}catch(IOException){}catch(UnauthorizedAccessException){}
    }
}
