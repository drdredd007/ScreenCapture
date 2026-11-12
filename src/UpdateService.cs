using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Moments;

internal sealed record UpdateRelease(Version Version,string Tag,string FileName,Uri Download,Uri Checksum,long Size,Uri Page);
internal sealed class UpdateService : IDisposable
{
    internal const string Repository="drdredd007/ScreenCapture";
    internal static readonly Uri Latest=new($"https://api.github.com/repos/{Repository}/releases/latest");
    internal static Version Current {
        get{var v=typeof(UpdateService).Assembly.GetName().Version??new Version(1,0,0);return new(v.Major,v.Minor,Math.Max(0,v.Build));}
    }
    const long MaximumZip=512*1024*1024;
    readonly HttpClient http;
    public UpdateService():this(new HttpClient(new HttpClientHandler{AllowAutoRedirect=true}){Timeout=Timeout.InfiniteTimeSpan}){}
    internal UpdateService(HttpClient client){
        http=client;
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Moments/"+Current);
    }
    internal static UpdateRelease Parse(string json){
        using var doc=JsonDocument.Parse(json);var root=doc.RootElement;
        if(root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean())throw new InvalidDataException("No stable update is available.");
        string tag=root.GetProperty("tag_name").GetString()??"";
        if(!Regex.IsMatch(tag,@"^v?\d+\.\d+\.\d+$") || !Version.TryParse(tag.TrimStart('v'),out var version))
            throw new InvalidDataException("The release has an unsupported version number.");
        string name=$"Moments-{version}-win-x64.zip";
        var assets=root.GetProperty("assets").EnumerateArray().ToArray();
        JsonElement Asset(string file){
            var matches=assets.Where(a=>a.GetProperty("name").GetString()==file && a.GetProperty("state").GetString()=="uploaded").ToArray();
            return matches.Length==1?matches[0]:throw new InvalidDataException("This release does not have a complete Windows x64 download yet. Try again later.");
        }
        Uri Url(JsonElement asset,string file){
            var expected=new Uri($"https://github.com/{Repository}/releases/download/{tag}/{file}");
            if(asset.GetProperty("browser_download_url").GetString()!=expected.AbsoluteUri)throw new InvalidDataException("Unexpected release download address.");
            return expected;
        }
        var zip=Asset(name);var checksum=Asset(name+".sha256");long size=zip.GetProperty("size").GetInt64();
        if(size<=0 || size>MaximumZip)throw new InvalidDataException("Unexpected update archive size.");
        return new(version,tag,name,Url(zip,name),Url(checksum,name+".sha256"),size,new Uri($"https://github.com/{Repository}/releases/tag/{tag}"));
    }
    static void CheckResponse(HttpResponseMessage response){
        if(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            throw new IOException("GitHub is temporarily limiting requests. Try again later.");
        if(response.StatusCode==HttpStatusCode.NotFound)throw new IOException("No published update was found on GitHub.");
        response.EnsureSuccessStatusCode();
    }
    async Task<string> SmallText(Uri uri,int limit,CancellationToken token){
        using var response=await http.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,token).ConfigureAwait(false);CheckResponse(response);
        if(response.Content.Headers.ContentLength>limit)throw new InvalidDataException("The update response is too large.");
        using var input=await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);using var output=new MemoryStream();
        var bytes=new byte[8192];int count;
        while((count=await input.ReadAsync(bytes,token).ConfigureAwait(false))>0){
            if(output.Length+count>limit)throw new InvalidDataException("The update response is too large.");
            output.Write(bytes,0,count);
        }
        return System.Text.Encoding.UTF8.GetString(output.ToArray());
    }
    public async Task<UpdateRelease> Check(CancellationToken token){
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(20));
        return Parse(await SmallText(Latest,1024*1024,timeout.Token).ConfigureAwait(false));
    }
    internal static string ExpectedHash(string text,string filename){
        var fields=text.Trim().Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);
        if(fields.Length!=2 || !Regex.IsMatch(fields[0],"^[0-9a-fA-F]{64}$") || fields[1].TrimStart('*')!=filename)
            throw new InvalidDataException("The release checksum is missing or invalid.");
        return fields[0];
    }
    public async Task<string> Download(UpdateRelease release,string folder,IProgress<double>? progress,CancellationToken token){
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromMinutes(30));var ct=timeout.Token;
        string hash=ExpectedHash(await SmallText(release.Checksum,4096,ct).ConfigureAwait(false),release.FileName);
        Directory.CreateDirectory(folder);
        string target=Path.Combine(folder,release.FileName),partial=target+"."+Guid.NewGuid().ToString("N")+".part";
        try{
            using var response=await http.GetAsync(release.Download,HttpCompletionOption.ResponseHeadersRead,ct).ConfigureAwait(false);CheckResponse(response);
            if(response.Content.Headers.ContentLength is long length && length!=release.Size)throw new InvalidDataException("The update download has an unexpected size.");
            using var digest=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using(var output=new FileStream(partial,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,true)){
                using var input=await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);var buffer=new byte[65536];long total=0;int count;progress?.Report(0);
                while((count=await input.ReadAsync(buffer,ct).ConfigureAwait(false))>0){
                    total+=count;if(total>release.Size || total>MaximumZip)throw new InvalidDataException("The update download exceeded its expected size.");
                    digest.AppendData(buffer,0,count);await output.WriteAsync(buffer.AsMemory(0,count),ct).ConfigureAwait(false);progress?.Report(total*100d/release.Size);
                }
                if(total!=release.Size)throw new InvalidDataException("The update download was interrupted. Please download it again.");
                await output.FlushAsync(ct).ConfigureAwait(false);
            }
            if(!Convert.ToHexString(digest.GetHashAndReset()).Equals(hash,StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The update did not pass its integrity check. Please download it again.");
            ct.ThrowIfCancellationRequested();File.Move(partial,target,true);return target;
        }finally{if(File.Exists(partial))File.Delete(partial);}
    }
    public void Dispose()=>http.Dispose();
}
