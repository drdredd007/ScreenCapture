using System.Net.Http;
namespace Moments;

internal sealed class LolClient : IDisposable
{
    static readonly Uri endpoint=new("https://127.0.0.1:2999/liveclientdata/allgamedata");
    readonly HttpClient http;
    public LolClient(){
        var handler=new HttpClientHandler{UseProxy=false,AllowAutoRedirect=false,UseCookies=false};
        // Riot's local game endpoint uses a self-signed certificate. This exception
        // is private to this client and this exact loopback endpoint, never global.
        handler.ServerCertificateCustomValidationCallback=(request,cert,chain,errors)=>request.RequestUri==endpoint && cert!=null;
        http=new(handler){Timeout=TimeSpan.FromSeconds(2),MaxResponseContentBufferSize=4*1024*1024};
    }
    public async Task<LolSnapshot> Read(CancellationToken cancel)=>LolEvents.Parse(await http.GetStringAsync(endpoint,cancel));
    public void Dispose()=>http.Dispose();
}
