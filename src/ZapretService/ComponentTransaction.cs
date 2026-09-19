using System.Text.Json;
namespace ZapretDesktop;

// All paths are children of the installer-owned directory. Callers serialize transactions.
public sealed class ComponentTransaction(string root) {
 public string Current=>Path.Combine(root,"components");
 string Pointer=>Path.Combine(root,"previous-components.txt");
 string Journal=>Path.Combine(root,"component-transaction.json");
 public bool HasInterrupted=>File.Exists(Journal);
 sealed record TransactionState(string Backup,string Prepared,string? Previous,bool Committed=false);
 static void AtomicWrite(string file,string value){File.WriteAllText(file+".tmp",value);File.Move(file+".tmp",file,true);}
 string Child(string relative){
  var full=Path.GetFullPath(Path.Combine(root,relative));
  if(Path.IsPathRooted(relative)||!full.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Некорректный путь журнала установки");
  return full;
 }
 public void RecoverInterrupted(){
  if(!HasInterrupted)return;NoLinks(root);
  if((File.GetAttributes(Journal)&FileAttributes.ReparsePoint)!=0)throw new IOException("Ссылка вместо журнала установки");
  var state=JsonSerializer.Deserialize<TransactionState>(File.ReadAllText(Journal))??throw new IOException("Повреждён журнал установки");
  if(!System.Text.RegularExpressions.Regex.IsMatch(state.Backup,@"^components-backup-[a-f0-9]{32}$"))throw new IOException("Некорректная резервная копия");
  var backup=Child(state.Backup);var prepared=Child(state.Prepared);
  if(prepared.Equals(Current,StringComparison.OrdinalIgnoreCase)||prepared.StartsWith(Current+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||prepared.Equals(backup,StringComparison.OrdinalIgnoreCase))throw new IOException("Некорректный путь подготовки в журнале");
  if(state.Committed){Validate(Current);Validate(backup);AtomicWrite(Pointer,state.Backup);}
  else{
   if(Directory.Exists(backup)){
    Validate(backup);
    if(Directory.Exists(Current)){
     NoLinks(Current);NoLinks(Path.GetDirectoryName(prepared)!);
     if(Directory.Exists(prepared))throw new IOException("Папка подготовки занята. Резервная копия сохранена: "+backup);
     Directory.Move(Current,prepared);
    }
    Directory.Move(backup,Current);
   }
   Validate(Current);
   if(state.Previous is null){if(File.Exists(Pointer))File.Delete(Pointer);}
   else{if(!System.Text.RegularExpressions.Regex.IsMatch(state.Previous,@"^components-backup-[a-f0-9]{32}$"))throw new IOException("Некорректный прежний указатель");AtomicWrite(Pointer,state.Previous);}
  }
  File.Delete(Journal);
 }
 static readonly string[] UserLists=["list-general-user.txt","list-exclude-user.txt","ipset-exclude-user.txt"];
 public static void NoLinks(string path){for(var d=new DirectoryInfo(Path.GetFullPath(path));d is not null;d=d.Parent)if((d.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Путь содержит ссылку");}
 public static void Validate(string directory){
  NoLinks(directory);
  foreach(var file in Directory.GetFileSystemEntries(directory,"*",SearchOption.AllDirectories))if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)throw new IOException("Ссылки в компонентах запрещены");
  foreach(var name in new[]{"winws.exe","WinDivert.dll","WinDivert64.sys","cygwin1.dll"})if(!File.Exists(Path.Combine(directory,"bin",name)))throw new IOException("Нет компонента "+name);
  StrategyImporter.Read(Path.Combine(directory,"general.bat"));
  foreach(var file in Directory.GetFiles(directory,"general*.bat"))StrategyImporter.Read(file);
 }
 public static void EnsureUserLists(string directory){
  NoLinks(directory);
  var lists=Path.Combine(directory,"lists");NoLinks(lists);Directory.CreateDirectory(lists);
  foreach(var name in UserLists){
   var file=Path.Combine(lists,name);
   if(File.Exists(file)){
    if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)throw new IOException("Ссылка вместо пользовательского списка");
    continue;
   }
   using var created=new FileStream(file,FileMode.CreateNew,FileAccess.Write,FileShare.None);
  }
 }
 public static void PreserveLists(string current,string target){
  NoLinks(current);NoLinks(target);Directory.CreateDirectory(Path.Combine(target,"lists"));
  foreach(var name in UserLists){var from=Path.Combine(current,"lists",name);var to=Path.Combine(target,"lists",name);if(File.Exists(from)){NoLinks(Path.GetDirectoryName(from)!);if((File.GetAttributes(from)&FileAttributes.ReparsePoint)!=0||(File.Exists(to)&&(File.GetAttributes(to)&FileAttributes.ReparsePoint)!=0))throw new IOException("Ссылка вместо пользовательского списка");File.Copy(from,to,true);}}
 }
 public string Previous(){
  NoLinks(root);if(!File.Exists(Pointer))throw new IOException("Предыдущая версия отсутствует");
  if((File.GetAttributes(Pointer)&FileAttributes.ReparsePoint)!=0)throw new IOException("Ссылка вместо указателя версии");
  var name=File.ReadAllText(Pointer).Trim();if(!System.Text.RegularExpressions.Regex.IsMatch(name,@"^components-backup-[a-f0-9]{32}$"))throw new IOException("Некорректный указатель резервной копии");
  var result=Path.Combine(root,name);Validate(result);return result;
 }
 public async Task Apply(string prepared,Func<Task> stop,Func<Task> startAndVerify){
  root=Path.GetFullPath(root);prepared=Path.GetFullPath(prepared);NoLinks(root);
  if(!prepared.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||prepared.Equals(Current,StringComparison.OrdinalIgnoreCase)||prepared.StartsWith(Current+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Пакет вне защищённой папки");
  if(HasInterrupted)throw new IOException("Сначала требуется восстановить прерванную установку");
  Validate(prepared);Validate(Current);PreserveLists(Current,prepared);Validate(prepared);
  var backup=Path.Combine(root,"components-backup-"+Guid.NewGuid().ToString("N"));
  var failed=Path.Combine(root,"components-failed-"+Guid.NewGuid().ToString("N"));
  bool movedOld=false,movedNew=false;
  await stop();
  var journal=new TransactionState(Path.GetFileName(backup),Path.GetRelativePath(root,prepared),File.Exists(Pointer)?File.ReadAllText(Pointer).Trim():null);
  try{
   AtomicWrite(Journal,JsonSerializer.Serialize(journal));
   Directory.Move(Current,backup);movedOld=true;
   Directory.Move(prepared,Current);movedNew=true;
   await startAndVerify();
   AtomicWrite(Journal,JsonSerializer.Serialize(journal with{Committed=true}));
   AtomicWrite(Pointer,Path.GetFileName(backup));File.Delete(Journal);
  }catch(Exception failure){
   try{
    AtomicWrite(Journal,JsonSerializer.Serialize(journal));
    await stop();
    if(movedNew)Directory.Move(Current,failed);
    if(movedOld)Directory.Move(backup,Current);
    if(movedNew)Directory.Move(failed,prepared);
    await startAndVerify();
    if(journal.Previous is null){if(File.Exists(Pointer))File.Delete(Pointer);}else AtomicWrite(Pointer,journal.Previous);
    if(File.Exists(Journal))File.Delete(Journal);
   }catch(Exception rollback){throw new IOException("Не удалось завершить откат. Резервная копия: "+backup+". "+rollback.Message,failure);}
   throw new IOException("Обновление отменено. Предыдущие компоненты восстановлены, служба отвечает. "+failure.Message,failure);
  }
 }
}



