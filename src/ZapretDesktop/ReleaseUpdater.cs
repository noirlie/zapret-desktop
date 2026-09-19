using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ZapretDesktop;

public record ComponentRelease(string Tag, string Name, Uri Url, long Size, string Sha256);
public record PreparedRelease(string Directory, string Components, int Strategies);

// Downloads data only. No BAT, executable or installer is launched by this class.
public sealed class ReleaseUpdater(HttpClient http) {
 public const string Endpoint="https://api.github.com/repos/Flowseal/zapret-discord-youtube/releases/latest";
 public const long MaximumArchive=32*1024*1024;
 const long MaximumExpanded=128*1024*1024;
 public async Task<ComponentRelease> Check(CancellationToken token) {
  using var request=new HttpRequestMessage(HttpMethod.Get,Endpoint);
  request.Headers.UserAgent.ParseAdd("ZapretDesktop/0.1.0");
  request.Headers.Accept.ParseAdd("application/vnd.github+json");
  using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token);
  response.EnsureSuccessStatusCode();
  await using var input=await response.Content.ReadAsStreamAsync(token);
  using var data=new MemoryStream();var buffer=new byte[8192];int read;
  while((read=await input.ReadAsync(buffer,token))>0){if(data.Length+read>1024*1024)throw new InvalidDataException("Слишком большой ответ GitHub");data.Write(buffer,0,read);}
  using var doc=JsonDocument.Parse(data.ToArray());var root=doc.RootElement;
  if(root.GetProperty("draft").GetBoolean()||root.GetProperty("prerelease").GetBoolean())throw new InvalidDataException("Ожидался стабильный выпуск");
  var tag=root.GetProperty("tag_name").GetString()??"";
  if(!Regex.IsMatch(tag,@"^v?\d+(\.\d+){1,3}$"))throw new InvalidDataException("Неподдерживаемый номер выпуска");
  var assets=root.GetProperty("assets").EnumerateArray().Where(a=>a.GetProperty("name").GetString()==$"zapret-discord-youtube-{tag}.zip").ToArray();
  if(assets.Length!=1)throw new InvalidDataException("В выпуске нет однозначного ZIP-пакета");
  var asset=assets[0];var digest=asset.TryGetProperty("digest",out var d)?d.GetString():null;
  if(digest is null||!Regex.IsMatch(digest,@"^sha256:[0-9a-fA-F]{64}$"))throw new InvalidDataException("GitHub не предоставил SHA-256. Загрузка отменена.");
  var result=new ComponentRelease(tag,asset.GetProperty("name").GetString()!,new Uri(asset.GetProperty("browser_download_url").GetString()!),asset.GetProperty("size").GetInt64(),digest[7..]);
  Validate(result);return result;
 }
 public static void Validate(ComponentRelease release) {
  if(!Regex.IsMatch(release.Tag,@"^v?\d+(\.\d+){1,3}$")||release.Name!=$"zapret-discord-youtube-{release.Tag}.zip"||!Regex.IsMatch(release.Sha256,@"^[0-9a-fA-F]{64}$")||release.Size<=0||release.Size>MaximumArchive)throw new InvalidDataException("Некорректные метаданные пакета");
  var expected=$"https://github.com/Flowseal/zapret-discord-youtube/releases/download/{release.Tag}/{release.Name}";
  if(release.Url.AbsoluteUri!=expected)throw new InvalidDataException("Источник пакета не соответствует репозиторию Flowseal");
 }
 public async Task<PreparedRelease> Download(ComponentRelease release,string cache,IProgress<int>? progress,CancellationToken token) {
  Validate(release);Directory.CreateDirectory(cache);NoLinks(cache);
  var stage=Path.Combine(cache,"package-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
  var partial=Path.Combine(stage,"download.part");
  try {
   using var request=new HttpRequestMessage(HttpMethod.Get,release.Url);request.Headers.UserAgent.ParseAdd("ZapretDesktop/0.1.0");
   using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token);response.EnsureSuccessStatusCode();
   if(response.Content.Headers.ContentLength is long size&&size!=release.Size)throw new InvalidDataException("Размер пакета отличается от метаданных GitHub");
   await using(var input=await response.Content.ReadAsStreamAsync(token))
   await using(var output=new FileStream(partial,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,true)){
    using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);var buffer=new byte[65536];long total=0;int read;
    while((read=await input.ReadAsync(buffer,token))>0){total+=read;if(total>release.Size)throw new InvalidDataException("Превышен размер пакета");hash.AppendData(buffer,0,read);await output.WriteAsync(buffer.AsMemory(0,read),token);progress?.Report((int)(total*100/release.Size));}
    if(total!=release.Size||!Convert.ToHexString(hash.GetHashAndReset()).Equals(release.Sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Пакет не прошёл проверку SHA-256 или размера");
   }
   var archive=Path.Combine(stage,"package.zip");File.Move(partial,archive);
   var unpacked=Path.Combine(stage,"unpacked");Directory.CreateDirectory(unpacked);
   await Task.Run(()=>Extract(archive,unpacked,token),token);
   var roots=Directory.GetFiles(unpacked,"general.bat",SearchOption.AllDirectories).Select(Path.GetDirectoryName).ToArray();
   if(roots.Length!=1)throw new InvalidDataException("Не найдена единственная папка компонентов");
   var components=roots[0]!;
   foreach(var file in new[]{"winws.exe","WinDivert.dll","WinDivert64.sys","cygwin1.dll"})if(!File.Exists(Path.Combine(components,"bin",file)))throw new InvalidDataException("В пакете нет "+file);
   // Mirror the three first-run defaults from upstream service.bat, without executing it.
   var lists=Path.Combine(components,"lists");Directory.CreateDirectory(lists);
   foreach(var item in new Dictionary<string,string>{
    ["ipset-exclude-user.txt"]="203.0.113.113/32\n",
    ["list-general-user.txt"]="# Never leave this file empty\ndomain.example.abc\n",
    ["list-exclude-user.txt"]="domain.example.abc\n"
   }){var path=Path.Combine(lists,item.Key);if(!File.Exists(path))await File.WriteAllTextAsync(path,item.Value,token);}
   int strategies=0;
   foreach(var file in Directory.GetFiles(components,"general*.bat")){token.ThrowIfCancellationRequested();StrategyImporter.Read(file);strategies++;}
   if(strategies==0)throw new InvalidDataException("Нет совместимых стратегий");
   await File.WriteAllTextAsync(Path.Combine(stage,"verified.json"),JsonSerializer.Serialize(new{release.Tag,release.Name,release.Sha256,Strategies=strategies}),token);
   return new(stage,components,strategies);
  }catch {
   // Delete only files inside this operation's validated, generated staging directory.
   if(Path.GetDirectoryName(Path.GetFullPath(stage))==Path.GetFullPath(cache).TrimEnd(Path.DirectorySeparatorChar)){
    NoLinks(stage);Directory.Delete(stage,true);
   }
   throw;
  }
 }
 static void NoLinks(string path){for(var d=new DirectoryInfo(Path.GetFullPath(path));d is not null;d=d.Parent)if((d.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Папка загрузки содержит ссылку");}
 public static void Extract(string archive,string destination,CancellationToken token) {
  NoLinks(destination);var root=Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
  using var zip=ZipFile.OpenRead(archive);if(zip.Entries.Count>2048)throw new InvalidDataException("Слишком много файлов в архиве");
  long total=0;var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  foreach(var entry in zip.Entries){
   token.ThrowIfCancellationRequested();var name=entry.FullName.Replace('\\','/');var pieces=name.TrimEnd('/').Split('/');
   if(name.StartsWith('/')||pieces.Any(p=>p.Length==0||p=="."||p==".."||p.EndsWith('.')||p.EndsWith(' ')||p.IndexOfAny(Path.GetInvalidFileNameChars())>=0||Regex.IsMatch(p,@"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])($|\.)",RegexOptions.IgnoreCase)))throw new InvalidDataException("Небезопасное имя в архиве");
   if(((entry.ExternalAttributes>>16)&0xF000)==0xA000||(entry.ExternalAttributes&(int)FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Ссылки в архиве запрещены");
   var path=Path.GetFullPath(Path.Combine(root,name));if(!path.StartsWith(root,StringComparison.OrdinalIgnoreCase)||!names.Add(path.TrimEnd(Path.DirectorySeparatorChar)))throw new InvalidDataException("Дублирующийся или внешний путь");
   total=checked(total+entry.Length);if(total>MaximumExpanded||entry.Length>MaximumArchive)throw new InvalidDataException("Распакованный пакет слишком большой");
   if(name.EndsWith('/')){Directory.CreateDirectory(path);continue;}
   Directory.CreateDirectory(Path.GetDirectoryName(path)!);
   using var input=entry.Open();using var output=new FileStream(path,FileMode.CreateNew);var buffer=new byte[65536];long copied=0;int read;
   while((read=input.Read(buffer))>0){token.ThrowIfCancellationRequested();copied+=read;if(copied>entry.Length)throw new InvalidDataException("Размер файла не совпадает");output.Write(buffer,0,read);}
   if(copied!=entry.Length)throw new InvalidDataException("Файл в архиве обрезан");
  }
 }
}




