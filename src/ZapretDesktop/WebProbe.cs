using System.Net;
using System.Net.Http;
using System.Text.Json;
namespace ZapretDesktop;
public sealed class WebProbe(Func<HttpMessageHandler>? handlerFactory=null) : IProbe {
 readonly ProbeDns dns=new();
 public void Reset()=>dns.Reset();
 public async Task<ProbeResult> CheckAsync(CancellationToken token) {
  using var handler=handlerFactory?.Invoke()??new SocketsHttpHandler {
   UseProxy=false, AllowAutoRedirect=false, ConnectTimeout=TimeSpan.FromSeconds(6),
   AutomaticDecompression=DecompressionMethods.GZip|DecompressionMethods.Deflate|DecompressionMethods.Brotli,
   ConnectCallback=async (context,ct)=>await ProbeDns.Connect((await dns.Resolve(context.DnsEndPoint.Host,ct)).Addresses,context.DnsEndPoint.Port,ct)
  };
  using var client=new HttpClient(handler){Timeout=TimeSpan.FromSeconds(12),DefaultRequestVersion=HttpVersion.Version20,DefaultVersionPolicy=HttpVersionPolicy.RequestVersionOrLower};
  client.DefaultRequestHeaders.UserAgent.ParseAdd("ZapretDesktop/0.1.0");
  var yt=CheckedRequest(client,"https://www.youtube.com/",false,token);
  var dc=CheckedRequest(client,"https://discord.com/api/v10/gateway",true,token);
  await Task.WhenAll(yt,dc);
  return new(yt.Result.ok,dc.Result.ok,yt.Result.detail,dc.Result.detail);
 }
 async Task<(bool ok,string detail)> CheckedRequest(HttpClient client,string url,bool json,CancellationToken token) {
  bool fallback=false;
  if(handlerFactory is null) {
   string host=new Uri(url).Host;
   try{fallback=(await dns.Resolve(host,token)).Fallback;}
   catch(System.Net.Sockets.SocketException){return(false,"DNS: системный и резервный резолверы недоступны для "+host);}
  }
  var result=await Check(client,url,json,token);
  return (result.ok,result.detail+(fallback?" · резервный DNS":""));
 }
 internal static async Task<(bool ok,string detail)> Check(HttpClient client,string url,bool json,CancellationToken token) {
  using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);
  deadline.CancelAfter(TimeSpan.FromSeconds(12));
  string host=new Uri(url).Host;
  try {
   using var response=await client.GetAsync(url,HttpCompletionOption.ResponseHeadersRead,deadline.Token);
   if((int)response.StatusCode is >=300 and <400) return(false,"HTTP "+(int)response.StatusCode+": перенаправление, проверка неполная");
   if(!response.IsSuccessStatusCode)return(false,"HTTP "+(int)response.StatusCode);
   await using var stream=await response.Content.ReadAsStreamAsync(deadline.Token);
   var bytes=new byte[524288];int count=0;
   while(count<bytes.Length) {
    int n=await stream.ReadAsync(bytes.AsMemory(count,Math.Min(8192,bytes.Length-count)),deadline.Token);
    if(n==0)break;count+=n;
    var partial=System.Text.Encoding.UTF8.GetString(bytes,0,count);
    // Stop as soon as identity is confirmed; do not wait for a full streaming home page.
    if(!json && IsYouTube(partial))return(true,"HTTPS доступен");
   }
   var body=System.Text.Encoding.UTF8.GetString(bytes,0,count);
   if(json) {
    using var doc=JsonDocument.Parse(body);
    var valid=doc.RootElement.TryGetProperty("url",out var u)&&u.ValueKind==JsonValueKind.String&&Uri.TryCreate(u.GetString(),UriKind.Absolute,out var uri)&&uri.Scheme=="wss"&&(uri.Host=="gateway.discord.gg"||uri.Host.EndsWith(".discord.gg",StringComparison.OrdinalIgnoreCase));
    return valid?(true,"API доступен"):(false,"Ответ API не подтверждён");
   }
   return(false,"Страница не распознана · проверка неполная");
  }catch(OperationCanceledException) when(!token.IsCancellationRequested){return(false,"Тайм-аут проверки "+host);}
  catch(HttpRequestException ex) {
   return ex.HttpRequestError switch {
    HttpRequestError.NameResolutionError=>(false,"DNS: не удалось разрешить "+host),
    HttpRequestError.SecureConnectionError=>(false,"TLS: защищённое соединение не установлено"),
    HttpRequestError.ConnectionError=>(false,"TCP: соединение с "+host+" не установлено"),
    _=>(false,"HTTP: "+ex.HttpRequestError)
   };
  }catch(JsonException){return(false,"Ответ API не распознан");}
  catch(System.IO.IOException){return(false,"Чтение ответа прервано");}
 }
 static bool IsYouTube(string text)=>text.Contains("ytInitialData",StringComparison.Ordinal)||text.Contains("ytcfg.set",StringComparison.Ordinal);
}

