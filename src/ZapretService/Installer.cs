using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
namespace ZapretDesktop;
static partial class Installer {
 static string[] PackageFiles(string root) => File.Exists(Path.Combine(root,"ZapretService.dll"))
  ? ["ZapretService.exe","ZapretService.dll","ZapretService.deps.json","ZapretService.runtimeconfig.json"]
  : ["ZapretService.exe"];
 public static async Task Setup(string source,string userName){
  using var key=Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\"+ServicePaths.Name);
  if(key is not null){await Upgrade();return;}
  var sid=new SecurityIdentifier(userName);
  await Install(source,sid.Value);
 }
 static readonly string Sc=Path.Combine(Environment.SystemDirectory,"sc.exe");
 static async Task<int> Run(params string[] args){using var p=new Process{StartInfo=new(Sc){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true}};foreach(var a in args)p.StartInfo.ArgumentList.Add(a);p.Start();var output=p.StandardOutput.ReadToEndAsync();var error=p.StandardError.ReadToEndAsync();await p.WaitForExitAsync();await Task.WhenAll(output,error);return p.ExitCode;}
 static void NoLinks(string path){for(var d=new DirectoryInfo(Path.GetFullPath(path));d is not null;d=d.Parent)if((d.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Папка содержит ссылку: "+d.FullName);}
 public static async Task Install(string source,string sidText){
  if(!EngineController.IsAdmin)throw new UnauthorizedAccessException("Требуются права администратора");
  var sid=new SecurityIdentifier(sidText);if(!sid.IsAccountSid())throw new InvalidDataException("Некорректный SID пользователя");
  if(await Run("query",ServicePaths.Name)==0)throw new IOException("Служба уже установлена. Сначала удалите её через приложение.");
  source=Path.GetFullPath(source);NoLinks(source);NoLinks(AppContext.BaseDirectory);
  if(!File.Exists(Path.Combine(source,"general.bat")))throw new IOException("Выберите исходную папку zapret");
  Directory.CreateDirectory(ServicePaths.Root);NoLinks(ServicePaths.Root);
  var acl=new DirectorySecurity();acl.SetAccessRuleProtection(true,false);
  foreach(var system in new[]{WellKnownSidType.LocalSystemSid,WellKnownSidType.BuiltinAdministratorsSid})acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(system,null),FileSystemRights.FullControl,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
  acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid,null),FileSystemRights.ReadAndExecute,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
  acl.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid,null));new DirectoryInfo(ServicePaths.Root).SetAccessControl(acl);
  string? previousComponents=null;
  if(Directory.EnumerateFileSystemEntries(ServicePaths.Root).Any()){
   var backup=Path.GetFullPath(ServicePaths.Root+".backup-"+Guid.NewGuid().ToString("N"));
   var parent=Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles))+Path.DirectorySeparatorChar;
   if(!backup.StartsWith(parent,StringComparison.OrdinalIgnoreCase)||!Path.GetFullPath(ServicePaths.Root).StartsWith(parent,StringComparison.OrdinalIgnoreCase))throw new IOException("Некорректный путь резервной копии");
   Directory.Move(ServicePaths.Root,backup);previousComponents=Path.Combine(backup,"components");Directory.CreateDirectory(ServicePaths.Root);new DirectoryInfo(ServicePaths.Root).SetAccessControl(acl);
  }
  foreach(var name in PackageFiles(AppContext.BaseDirectory))File.Copy(Path.Combine(AppContext.BaseDirectory,name),Path.Combine(ServicePaths.Root,name));
  foreach(var relative in new[]{"bin","lists"}){
   var from=Path.Combine(source,relative);NoLinks(from);var to=Path.Combine(ServicePaths.Components,relative);Directory.CreateDirectory(to);
   foreach(var file in Directory.GetFiles(from)){
    if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)throw new IOException("Файловые ссылки не поддерживаются");
    var name=Path.GetFileName(file);var ext=Path.GetExtension(file).ToLowerInvariant();
    if(relative=="lists"&&ext!=".txt")continue;
    if(relative=="bin"&&ext!=".bin"&&!new[]{"winws.exe","WinDivert.dll","WinDivert64.sys","cygwin1.dll"}.Contains(name,StringComparer.OrdinalIgnoreCase))continue;
    File.Copy(file,Path.Combine(to,name));
   }
  }
  foreach(var file in Directory.GetFiles(source,"general*.bat")){if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)throw new IOException("Файловая ссылка");File.Copy(file,Path.Combine(ServicePaths.Components,Path.GetFileName(file)));}
  if(previousComponents is not null&&Directory.Exists(previousComponents))ComponentTransaction.PreserveLists(previousComponents,ServicePaths.Components);
  ComponentTransaction.EnsureUserLists(ServicePaths.Components);
  ComponentTransaction.Validate(ServicePaths.Components);
  if(File.Exists(Path.Combine(source,"release.json")))File.Copy(Path.Combine(source,"release.json"),Path.Combine(ServicePaths.Components,"release.json"),true);
  File.WriteAllText(Path.Combine(ServicePaths.Root,"owner.sid"),sid.Value);
  string executable=Path.Combine(ServicePaths.Root,"ZapretService.exe");
  if(await Run("create",ServicePaths.Name,"binPath=","\""+executable+"\"","start=","auto","DisplayName=","zapret Desktop — background engine")!=0)throw new IOException("Не удалось зарегистрировать службу");
  if(await Run("start",ServicePaths.Name)!=0){await Run("delete",ServicePaths.Name);throw new IOException("Служба зарегистрирована, но запуск не удался");}
 }
 static async Task WaitStopped(){
  for(int i=0;i<120;i++){
   bool running=false;
   foreach(var process in Process.GetProcessesByName("ZapretService"))using(process){
    if(process.Id!=Environment.ProcessId&&ProcessInspection.IsRunningAt(process,Path.Combine(ServicePaths.Root,"ZapretService.exe")))running=true;
   }
   if(!running){
    try{
     // Process exit and release of the mapped executable are not simultaneous.
     using var handle=new FileStream(Path.Combine(ServicePaths.Root,"ZapretService.exe"),FileMode.Open,FileAccess.ReadWrite,FileShare.None);
     return;
    }catch(IOException ex)when((ex.HResult&0xffff) is 32 or 33){}
   }
   await Task.Delay(500);
  }
  throw new IOException("Служба не завершилась. Резервные файлы сохранены, замена отменена.");
 }
 static async Task ReplaceFile(string source,string target){
  var stage=target+".incoming-"+Guid.NewGuid().ToString("N");
  try{
   File.Copy(source,stage);
   for(int attempt=0;;attempt++){
    try{File.Move(stage,target,true);return;}
    catch(IOException ex)when((ex.HResult&0xffff) is 32 or 33 && attempt<80){await Task.Delay(250);}
    catch(UnauthorizedAccessException)when(attempt<80){await Task.Delay(250);}
   }
  }finally{if(File.Exists(stage))File.Delete(stage);}
 }
 public static async Task Upgrade(){
  if(!EngineController.IsAdmin)throw new UnauthorizedAccessException();
  NoLinks(ServicePaths.Root);NoLinks(AppContext.BaseDirectory);
  using var key=Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\"+ServicePaths.Name);
  var expected="\""+Path.Combine(ServicePaths.Root,"ZapretService.exe")+"\"";
  if(!string.Equals(key?.GetValue("ImagePath") as string,expected,StringComparison.OrdinalIgnoreCase))throw new IOException("Не найдена служба, установленная приложением");
  var files=PackageFiles(AppContext.BaseDirectory);
  foreach(var name in files){var file=Path.Combine(AppContext.BaseDirectory,name);if(!File.Exists(file)||(File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)throw new IOException("Повреждён пакет обновления службы");}
  var backup=Path.Combine(ServicePaths.Root,"rollback-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(backup);
  var oldFiles=PackageFiles(ServicePaths.Root);
  foreach(var name in oldFiles){var old=Path.Combine(ServicePaths.Root,name);if((File.GetAttributes(old)&FileAttributes.ReparsePoint)!=0)throw new IOException("Неподдерживаемая ссылка в установленной службе");File.Copy(old,Path.Combine(backup,name));}
  await Run("stop",ServicePaths.Name);
  bool replaced=false;
  try{
   await WaitStopped();
   foreach(var name in files){await ReplaceFile(Path.Combine(AppContext.BaseDirectory,name),Path.Combine(ServicePaths.Root,name));replaced=true;}
   if(await Run("start",ServicePaths.Name)!=0)throw new IOException("Новая служба не запустилась");
   for(int i=0;i<5;i++){try{var status=await new ServiceClient().Send(new("status"));if(status.Version=="0.1.0")return;}catch(Exception ex)when(ex is IOException or OperationCanceledException){}await Task.Delay(500);}
   throw new IOException("Новая служба не ответила на проверку состояния");
  }catch(Exception failure){
   if(!replaced){await Run("start",ServicePaths.Name);throw new IOException("Файлы службы не изменены. Не удалось дождаться освобождения файлов: "+failure.Message,failure);}
   await Run("stop",ServicePaths.Name);await WaitStopped();
   foreach(var name in oldFiles)await ReplaceFile(Path.Combine(backup,name),Path.Combine(ServicePaths.Root,name));
   if(await Run("start",ServicePaths.Name)!=0)throw new IOException("Файлы восстановлены, но прежняя служба не запустилась. Резервная копия: "+backup,failure);
   throw new IOException("Обновление не прошло проверку. Прежние файлы восстановлены, команда запуска принята: "+failure.Message);
  }
 }
 public static async Task Uninstall(){
  if(!EngineController.IsAdmin)throw new UnauthorizedAccessException();
  using var key=Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\"+ServicePaths.Name);
  var expected="\""+Path.Combine(ServicePaths.Root,"ZapretService.exe")+"\"";
  if(!string.Equals(key?.GetValue("ImagePath") as string,expected,StringComparison.OrdinalIgnoreCase))throw new IOException("Путь службы отличается от установленного приложением. Удаление отменено.");
  await Run("stop",ServicePaths.Name);
  await WaitStopped();
  if(await Run("delete",ServicePaths.Name)!=0)throw new IOException("Не удалось удалить регистрацию службы");
  // Remove only this installation's autorun entry, in its owning user's hive.
  var ownerFile=Path.Combine(ServicePaths.Root,"owner.sid");
  if(File.Exists(ownerFile)){
   var owner=new SecurityIdentifier(File.ReadAllText(ownerFile).Trim());
   using var run=Microsoft.Win32.Registry.Users.OpenSubKey(owner.Value+@"\Software\Microsoft\Windows\CurrentVersion\Run",true);
   var expectedApp=StartupRegistration.Command(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Zapret","ZapretDesktop.exe"));
   if(string.Equals(run?.GetValue("ZapretDesktop") as string,expectedApp,StringComparison.OrdinalIgnoreCase))run!.DeleteValue("ZapretDesktop",false);
  }
  // Keep protected components as a backup; never recursively delete user files.
 }
}





