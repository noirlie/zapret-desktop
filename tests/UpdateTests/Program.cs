using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using ZapretDesktop;
if(args.Contains("--diagnose")){
 Console.WriteLine("DNS: "+string.Join(", ",(await System.Net.Dns.GetHostAddressesAsync("api.github.com")).Select(x=>x.ToString())));
 foreach(var mode in new[]{"default","direct","ipv4","tls12"}){
  using var h=new SocketsHttpHandler{UseProxy=mode=="default",ConnectTimeout=TimeSpan.FromSeconds(8)};
  if(mode=="tls12")h.SslOptions.EnabledSslProtocols=System.Security.Authentication.SslProtocols.Tls12;
  if(mode=="ipv4")h.ConnectCallback=async(ctx,ct)=>{
   var ips=(await System.Net.Dns.GetHostAddressesAsync(ctx.DnsEndPoint.Host,ct)).Where(a=>a.AddressFamily==System.Net.Sockets.AddressFamily.InterNetwork);
   foreach(var ip in ips){var socket=new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork,System.Net.Sockets.SocketType.Stream,System.Net.Sockets.ProtocolType.Tcp);try{await socket.ConnectAsync(ip,ctx.DnsEndPoint.Port,ct);return new System.Net.Sockets.NetworkStream(socket,true);}catch{socket.Dispose();}}
   throw new IOException("No IPv4 connection");
  };
  using var client=new HttpClient(h){Timeout=TimeSpan.FromSeconds(15)};
  try{var release=await new ReleaseUpdater(client).Check(default);Console.WriteLine(mode+" OK "+release.Tag);}catch(Exception ex){Console.WriteLine(mode+" "+ex.ToString());}
 }
 return;
}
var root=Path.Combine(AppContext.BaseDirectory,"cases-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
int passed=0;
void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);passed++;}
byte[] Zip(params (string Name,string Text)[] files){using var ms=new MemoryStream();using(var zip=new ZipArchive(ms,ZipArchiveMode.Create,true))foreach(var f in files){using var writer=new StreamWriter(zip.CreateEntry(f.Name).Open());writer.Write(f.Text);}return ms.ToArray();}
ComponentRelease Release(byte[] bytes)=>new("1.10.2","zapret-discord-youtube-1.10.2.zip",new("https://github.com/Flowseal/zapret-discord-youtube/releases/download/1.10.2/zapret-discord-youtube-1.10.2.zip"),bytes.Length,Convert.ToHexString(SHA256.HashData(bytes)));
foreach(var bad in new[]{"../escape.txt","/root.txt","C:/escape.txt","folder/file:stream","CON.txt","folder./file","folder//file"}){
 var file=Path.Combine(root,Guid.NewGuid()+".zip");File.WriteAllBytes(file,Zip((bad,"x")));
 var dest=Path.Combine(root,Guid.NewGuid().ToString());Directory.CreateDirectory(dest);
 try{ReleaseUpdater.Extract(file,dest,default);throw new Exception("accepted "+bad);}catch(InvalidDataException){Check(true,"reject "+bad);}
}
var duplicate=Path.Combine(root,"duplicate.zip");File.WriteAllBytes(duplicate,Zip(("file.txt","x"),("FILE.txt","y")));
var dd=Path.Combine(root,"duplicate");Directory.CreateDirectory(dd);
try{ReleaseUpdater.Extract(duplicate,dd,default);throw new Exception("duplicate");}catch(InvalidDataException){Check(true,"reject case-insensitive duplicate");}
var linkZip=Path.Combine(root,"link.zip");
using(var zip=ZipFile.Open(linkZip,ZipArchiveMode.Create)){var entry=zip.CreateEntry("link");entry.ExternalAttributes=unchecked((int)0xA1FF0000);}
var ld=Path.Combine(root,"links");Directory.CreateDirectory(ld);
try{ReleaseUpdater.Extract(linkZip,ld,default);throw new Exception("link");}catch(InvalidDataException){Check(true,"reject symlink");}
Check(UpdateHttp.ParseSocksProxy("socks=127.0.0.1:10808")?.AbsoluteUri=="socks5://127.0.0.1:10808/","Windows SOCKS entry maps to SOCKS5");
Check(UpdateHttp.ParseSocksProxy("https=proxy:8080;socks=127.0.0.1:10808")==null,"explicit HTTPS proxy takes precedence");
Check(UpdateHttp.ParseSocksProxy(null)==null,"absent proxy uses defaults");
try{UpdateHttp.ParseSocksProxy("socks=user:secret@proxy:1080");throw new Exception("credentials accepted");}catch(IOException){Check(true,"ambiguous credential syntax rejected");}
var data=Zip(("general.bat","start test \"%BIN%winws.exe\" --wf-tcp=443"),("bin/winws.exe","fixture"),("bin/WinDivert.dll","fixture"),("bin/WinDivert64.sys","fixture"),("bin/cygwin1.dll","fixture"));
using var http=new HttpClient(new Handler(data));var updater=new ReleaseUpdater(http);var cache=Path.Combine(root,"cache");
var prepared=await updater.Download(Release(data),cache,null,default);
Check(prepared.Strategies==1&&File.Exists(Path.Combine(prepared.Directory,"verified.json")),"valid package prepared, never executed");
foreach(var bad in new[]{Release(data) with{Sha256=new string('0',64)},Release(data) with{Size=data.Length+1}}){
 var before=Directory.GetDirectories(cache).Length;
 try{await updater.Download(bad,cache,null,default);throw new Exception("invalid download accepted");}catch(InvalidDataException){Check(Directory.GetDirectories(cache).Length==before,"bad digest/size cleaned up");}
}
try{ReleaseUpdater.Validate(Release(data) with{Url=new Uri("https://example.com/evil.zip")});throw new Exception("foreign URL");}catch(InvalidDataException){Check(true,"foreign source rejected");}
using(var stop=new CancellationTokenSource()){stop.Cancel();try{await updater.Download(Release(data),cache,null,stop.Token);throw new Exception("cancellation");}catch(OperationCanceledException){Check(true,"cancel download");}}
if(args.Contains("--live")){
 using var live=UpdateHttp.Create();live.Timeout=TimeSpan.FromSeconds(60);
 var actual=new ReleaseUpdater(live);var release=await actual.Check(default);
 var package=await actual.Download(release,Path.Combine(root,"live"),null,default);
 Check(package.Strategies>0,"LIVE GitHub "+release.Tag+": SHA256, archive and "+package.Strategies+" strategies");
 Console.WriteLine("LIVE PACKAGE "+package.Directory);
}
Console.WriteLine("TOTAL "+passed+" update checks");
class Handler(byte[] data):HttpMessageHandler{
 protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){token.ThrowIfCancellationRequested();return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(data)});}
}





