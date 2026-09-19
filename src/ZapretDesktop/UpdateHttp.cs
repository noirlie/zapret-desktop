using System.IO;
using System.Net;
using System.Net.Http;
using Microsoft.Win32;
namespace ZapretDesktop;
public static class UpdateHttp {
 public static Uri? ParseSocksProxy(string? settings){
  if(string.IsNullOrWhiteSpace(settings))return null;
  var entries=settings.Split(';',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries);
  if(entries.Any(x=>x.StartsWith("https=",StringComparison.OrdinalIgnoreCase)||x.StartsWith("http=",StringComparison.OrdinalIgnoreCase)))return null;
  var socks=entries.FirstOrDefault(x=>x.StartsWith("socks=",StringComparison.OrdinalIgnoreCase));
  var value=socks is null?settings.Trim():socks[6..];
  if(socks is null&&!value.StartsWith("socks5://",StringComparison.OrdinalIgnoreCase))return null;
  if(!value.Contains("://"))value="socks5://"+value;
  if(!Uri.TryCreate(value,UriKind.Absolute,out var uri)||uri.Scheme!="socks5"||uri.Port<=0||uri.UserInfo.Length>0||uri.AbsolutePath!="/"||uri.Query.Length>0||uri.Fragment.Length>0)throw new IOException("Некорректный SOCKS-прокси в настройках Windows.");
  return uri;
 }
 public static HttpClient Create(){
  var handler=new SocketsHttpHandler{ConnectTimeout=TimeSpan.FromSeconds(12)};
  // .NET's Windows proxy auto-detection does not reliably map the legacy socks= syntax to SOCKS5.
  // Keep explicit environment overrides and HTTP/PAC configuration under the default .NET handling.
  if(OperatingSystem.IsWindows()&&string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HTTPS_PROXY"))&&string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ALL_PROXY"))){
   using var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings");
   if(key?.GetValue("ProxyEnable") is int enabled&&enabled==1){
    var uri=ParseSocksProxy(key.GetValue("ProxyServer") as string);
    if(uri is not null)handler.Proxy=new WebProxy(uri);
   }
  }
  return new HttpClient(handler){Timeout=Timeout.InfiniteTimeSpan};
 }
 public static string Describe(Exception ex){
  if(ex is OperationCanceledException)return "Время ожидания истекло или операция отменена.";
  if(ex is HttpRequestException http&&http.StatusCode is not null)return "GitHub ответил HTTP "+(int)http.StatusCode+". Повторите позже.";
  var inner=ex;while(inner.InnerException is not null)inner=inner.InnerException;
  if(inner is System.Security.Authentication.AuthenticationException)return "Не удалось подтвердить защищённое соединение. Проверьте дату Windows и сертификаты прокси. "+inner.Message;
  if(ex is HttpRequestException)return "Соединение с GitHub прервано. Проверьте подключение и включённый в Windows прокси. Подробности: "+inner.Message;
  return ex.Message;
 }
}



