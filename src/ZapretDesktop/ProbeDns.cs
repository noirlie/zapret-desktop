using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
namespace ZapretDesktop;
public record ResolvedHost(IPAddress[] Addresses,bool Fallback);
public sealed class ProbeDns {
 readonly ConcurrentDictionary<string,(ResolvedHost value,DateTime until)> cache=new();
 public void Reset()=>cache.Clear();
 public async Task<ResolvedHost> Resolve(string host,CancellationToken token) {
  if(cache.TryGetValue(host,out var saved)&&saved.until>DateTime.UtcNow)return saved.value;
  using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(token)) {
   timeout.CancelAfter(TimeSpan.FromSeconds(2));
   try {var addresses=(await Dns.GetHostAddressesAsync(host,timeout.Token)).Where(IsPublic).ToArray();if(addresses.Length>0)return Remember(host,new(addresses,false));}
   catch(SocketException){}catch(OperationCanceledException)when(!token.IsCancellationRequested){}
  }
  // Diagnostic requests only. Never changes the OS DNS, hosts file, or browser settings.
  foreach(var server in new[]{"1.1.1.1","1.0.0.1"}) {
   token.ThrowIfCancellationRequested();
   try {
    using var handler=new SocketsHttpHandler{UseProxy=false,AllowAutoRedirect=false};
    using var client=new HttpClient(handler){Timeout=TimeSpan.FromSeconds(4),MaxResponseContentBufferSize=65536};
    client.DefaultRequestHeaders.Accept.ParseAdd("application/dns-json");
    var json=await client.GetStringAsync("https://"+server+"/dns-query?name="+Uri.EscapeDataString(host)+"&type=A",token);
    var parsed=Parse(json);
    if(parsed.Length>0)return Remember(host,new(parsed,true));
   }catch(HttpRequestException){}catch(JsonException){}catch(OperationCanceledException)when(!token.IsCancellationRequested){}
  }
  throw new SocketException((int)SocketError.HostNotFound);
 }
 ResolvedHost Remember(string host,ResolvedHost value){cache[host]=(value,DateTime.UtcNow.AddSeconds(60));return value;}
 public static IPAddress[] Parse(string json) {
  using var doc=JsonDocument.Parse(json);
  if(!doc.RootElement.TryGetProperty("Status",out var status)||status.GetInt32()!=0||!doc.RootElement.TryGetProperty("Answer",out var answers)||answers.ValueKind!=JsonValueKind.Array)return [];
  return answers.EnumerateArray().Where(a=>a.TryGetProperty("type",out var t)&&t.TryGetInt32(out int type)&&type==1)
   .Select(a=>a.TryGetProperty("data",out var d)&&d.ValueKind==JsonValueKind.String&&IPAddress.TryParse(d.GetString(),out var ip)?ip:null)
   .Where(ip=>ip is not null&&IsPublic(ip)).Cast<IPAddress>().Distinct().ToArray();
 }
 static bool IsPublic(IPAddress ip){if(IPAddress.IsLoopback(ip)||ip.IsIPv6LinkLocal||ip.IsIPv6SiteLocal||ip.IsIPv6Multicast)return false;if(ip.IsIPv4MappedToIPv6)ip=ip.MapToIPv4();var b=ip.GetAddressBytes();if(b.Length==16)return (b[0]&0xfe)!=0xfc && !ip.Equals(IPAddress.IPv6Any);return b[0]!=0&&b[0]!=10&&b[0]!=127&&b[0]<224&&!(b[0]==169&&b[1]==254)&&!(b[0]==172&&b[1]>=16&&b[1]<=31)&&!(b[0]==192&&b[1]==168);}
 public static async ValueTask<System.IO.Stream> Connect(IPAddress[] addresses,int port,CancellationToken token) {
  foreach(var ip in addresses.Take(4)) {
   var socket=new Socket(ip.AddressFamily,SocketType.Stream,ProtocolType.Tcp){NoDelay=true};
   using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(2));
   try{await socket.ConnectAsync(new IPEndPoint(ip,port),timeout.Token);return new NetworkStream(socket,true);}
   catch(SocketException){socket.Dispose();}catch(OperationCanceledException)when(!token.IsCancellationRequested){socket.Dispose();}
   catch{socket.Dispose();throw;}
  }
  throw new SocketException((int)SocketError.HostUnreachable);
 }
}

