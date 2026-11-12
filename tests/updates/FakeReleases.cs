using System;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
namespace Moments;
internal sealed class FakeReleases : HttpMessageHandler
{
    public string Tag="v1.2.0";
    public string Name=>"Moments-"+Tag.TrimStart('v')+"-win-x64.zip";
    public readonly byte[] Bytes=new byte[200000];
    public bool Corrupt,Prerelease,MissingChecksum;
    public int Delay;
    public HttpStatusCode Status=HttpStatusCode.OK;
    public FakeReleases(){new Random(17).NextBytes(Bytes);}
    public string Json()=>JsonSerializer.Serialize(new{tag_name=Tag,draft=false,prerelease=Prerelease,assets=MissingChecksum?new[]{Asset(Name)}:new[]{Asset(Name),Asset(Name+".sha256")}});
    object Asset(string name)=>new{name,state="uploaded",size=Bytes.Length,browser_download_url=$"https://github.com/{UpdateService.Repository}/releases/download/{Tag}/{name}"};
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){
        if(Delay>0)await Task.Delay(Delay,token);
        if(Status!=HttpStatusCode.OK)return new(Status);
        if(request.RequestUri==UpdateService.Latest)return new(Status){Content=new StringContent(Json())};
        if(request.RequestUri!.AbsolutePath.EndsWith(".sha256"))return new(Status){Content=new StringContent(Convert.ToHexString(SHA256.HashData(Bytes))+"  "+Name+"\n")};
        var data=(byte[])Bytes.Clone();if(Corrupt)data[0]^=1;
        return new(Status){Content=new ByteArrayContent(data)};
    }
}
