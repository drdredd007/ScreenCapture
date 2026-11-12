using Moments;
using System.Net;
using System.Security.Cryptography;

static class Checks
{
    static int count;
    static void Require(bool ok,string why){count++;if(!ok)throw new Exception(why);}
    static async Task Reject(Func<Task> work,string why){bool failed=false;try{await work();}catch(Exception ex) when(ex is IOException or OperationCanceledException or HttpRequestException){failed=true;}Require(failed,why);}
    public static async Task Main(){
        string folder=Path.Combine(Path.GetTempPath(),"moments_update_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try{
            var fake=new FakeReleases();using var service=new UpdateService(new HttpClient(fake));
            var release=await service.Check(default);Require(release.Version==new Version(1,2,0),"release version");
            fake.Tag="v1.10.0";Require((await service.Check(default)).Version>new Version(1,9,9),"numeric version comparison");fake.Tag="v1.2.0";
            fake.Prerelease=true;await Reject(()=>service.Check(default),"prerelease accepted");fake.Prerelease=false;
            fake.MissingChecksum=true;await Reject(()=>service.Check(default),"incomplete release accepted");fake.MissingChecksum=false;
            fake.Status=HttpStatusCode.Forbidden;await Reject(()=>service.Check(default),"GitHub rate limit hidden");fake.Status=HttpStatusCode.OK;
            fake.Tag="v1.2.0-beta";await Reject(()=>service.Check(default),"malformed tag accepted");fake.Tag="v1.2.0";
            await Reject(()=>Task.FromResult(UpdateService.Parse(fake.Json().Replace("https://github.com/","https://evil.example/"))),"external download address accepted");
            string downloaded=await service.Download(release,folder,null,default);
            Require(File.ReadAllBytes(downloaded).SequenceEqual(fake.Bytes),"download content changed");
            fake.Corrupt=true;await Reject(()=>service.Download(release,folder,null,default),"corrupt ZIP accepted");fake.Corrupt=false;
            Require(File.ReadAllBytes(downloaded).SequenceEqual(fake.Bytes),"bad download replaced verified archive");
            Require(!Directory.GetFiles(folder,"*.part").Any(),"failed download left partial files");
            using(var canceled=new CancellationTokenSource()){
                await Reject(()=>service.Download(release,folder,new ProgressNow(v=>{if(v>0)canceled.Cancel();}),canceled.Token),"cancellation ignored");
            }
            Require(!Directory.GetFiles(folder,"*.part").Any(),"canceled download left partial files");
            Require(File.ReadAllBytes(downloaded).SequenceEqual(fake.Bytes),"cancellation damaged previous download");
            Require(UpdateService.ExpectedHash(Convert.ToHexString(SHA256.HashData(fake.Bytes))+" *"+release.FileName,release.FileName).Length==64,"binary checksum format");
            await Reject(()=>Task.FromResult(UpdateService.ExpectedHash(new string('0',64)+" other.zip",release.FileName)),"checksum filename ignored");
            Console.WriteLine($"PASS: {count} release/version, download, checksum, cancellation and failure checks.");
        }finally{Directory.Delete(folder,true);}
    }
    sealed class ProgressNow(Action<double> report):IProgress<double>{public void Report(double value)=>report(value);}
}
