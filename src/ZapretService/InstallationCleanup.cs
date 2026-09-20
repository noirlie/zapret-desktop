using System.Security.Principal;
using System.Text.RegularExpressions;
using Microsoft.Win32;
namespace ZapretDesktop;

public static class InstallationCleanup {
 public static string ValidateAppDirectory(string value){
  if(!Path.IsPathFullyQualified(value)||value.StartsWith(@"\\")||value.Contains('"'))throw new IOException("Укажите локальную папку приложения.");
  var path=Path.GetFullPath(value).TrimEnd(Path.DirectorySeparatorChar);
  if(string.Equals(path,Path.GetPathRoot(path)?.TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase))throw new IOException("Нельзя устанавливать приложение в корень диска.");
  return path;
 }
 public static void ValidateTree(string path){
  for(var p=Path.GetFullPath(path);!string.IsNullOrEmpty(p);p=Path.GetDirectoryName(p))
   if((Directory.Exists(p)||File.Exists(p))&&(File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)throw new IOException("Очистка отменена: обнаружена ссылка в пути "+p);
  if(!Directory.Exists(path))return;
  foreach(var entry in Directory.EnumerateFileSystemEntries(path)){
   if((File.GetAttributes(entry)&FileAttributes.ReparsePoint)!=0)throw new IOException("Очистка отменена: обнаружена файловая ссылка "+entry);
   if(Directory.Exists(entry))ValidateTree(entry);
  }
 }
 public static string[] Plan(string serviceRoot,string profileRoot,string owner){
  var root=Path.GetFullPath(serviceRoot).TrimEnd(Path.DirectorySeparatorChar);
  if(Path.GetFileName(root)!="ZapretDesktopService")throw new IOException("Неизвестный каталог службы.");
  var sid=new SecurityIdentifier(owner);if(!sid.IsAccountSid())throw new IOException("Некорректный владелец установки.");
  var profile=Path.GetFullPath(profileRoot).TrimEnd(Path.DirectorySeparatorChar);
  if(profile==Path.GetPathRoot(profile)?.TrimEnd(Path.DirectorySeparatorChar))throw new IOException("Некорректная папка профиля.");
  var targets=new List<string>();
  if(Directory.Exists(root)){
   ValidateTree(root);
   if(File.ReadAllText(Path.Combine(root,"owner.sid")).Trim()!=owner)throw new IOException("Владелец службы не совпадает.");
   targets.Add(root);
  }
  var parent=Path.GetDirectoryName(root)!;
  foreach(var backup in Directory.GetDirectories(parent,"ZapretDesktopService.backup-*")){
   if(!Regex.IsMatch(Path.GetFileName(backup),@"^ZapretDesktopService\.backup-[a-f0-9]{32}$"))continue;
   ValidateTree(backup);
   var marker=Path.Combine(backup,"owner.sid");
   if(File.Exists(marker)&&File.ReadAllText(marker).Trim()==owner)targets.Add(backup);
  }
  var userData=Path.GetFullPath(Path.Combine(profile,"AppData","Local","ZapretDesktop.Alpha"));
  if(!userData.StartsWith(profile+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Путь настроек выходит за профиль пользователя.");
  ValidateTree(userData);if(Directory.Exists(userData))targets.Add(userData);
  return targets.OrderBy(p=>p.Equals(root,StringComparison.OrdinalIgnoreCase)?1:p.Equals(userData,StringComparison.OrdinalIgnoreCase)?2:0).ToArray();
 }
 public static void Purge(string serviceRoot,string profileRoot,string owner){
  // Compute and validate every absolute target before deleting anything.
  var targets=Plan(serviceRoot,profileRoot,owner);
  var root=Path.GetFullPath(serviceRoot).TrimEnd(Path.DirectorySeparatorChar);
  // Remove service files before user settings, but retain owner.sid until every target is gone.
  foreach(var target in targets){
   ValidateTree(target);
   DeleteTree(target,target.Equals(root,StringComparison.OrdinalIgnoreCase));
   if(!target.Equals(root,StringComparison.OrdinalIgnoreCase)&&Directory.Exists(target))throw new IOException("Не удалось удалить "+target);
  }
  if(Directory.Exists(root)){
   var marker=Path.Combine(root,"owner.sid");
   if(Directory.EnumerateFileSystemEntries(root).Any(p=>!p.Equals(marker,StringComparison.OrdinalIgnoreCase)))throw new IOException("Не удалось полностью удалить файлы службы.");
   File.Delete(marker);Directory.Delete(root,false);
  }
 }
 static void DeleteTree(string path,bool keepOwner=false){
  foreach(var directory in Directory.GetDirectories(path))DeleteTree(directory);
  // Keep installation identity until all other files were successfully removed.
  foreach(var file in Directory.GetFiles(path))if(!keepOwner||!Path.GetFileName(file).Equals("owner.sid",StringComparison.OrdinalIgnoreCase))File.Delete(file);
  if(!keepOwner)Directory.Delete(path,false);
 }
 public static string OwnerProfile(string sid){
  using var key=Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\"+sid);
  var value=key?.GetValue("ProfileImagePath") as string;
  if(string.IsNullOrWhiteSpace(value))throw new IOException("Не найдена папка владельца установки. Настройки не удалены.");
  return Path.GetFullPath(Environment.ExpandEnvironmentVariables(value));
 }
}
