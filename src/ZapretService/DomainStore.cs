using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace ZapretDesktop;

// Only fixed user-list paths are writable. Base vendor lists are never modified.
sealed class DomainStore(string root){
 string Included=>Path.Combine(root,"lists","list-general-user.txt");
 string Excluded=>Path.Combine(root,"lists","list-exclude-user.txt");
 string Backup=>root.TrimEnd(Path.DirectorySeparatorChar)+".domains-backup.json";
 string Journal=>root.TrimEnd(Path.DirectorySeparatorChar)+".domains-journal.json";
 static void CheckPath(string path){for(var p=Path.GetFullPath(path);!string.IsNullOrEmpty(p);p=Path.GetDirectoryName(p))if((File.Exists(p)||Directory.Exists(p))&&(File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)throw new IOException("Ссылки в папке списков не поддерживаются");}
 static void Write(string path,string data){CheckPath(path);var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";try{using(var f=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){using var writer=new StreamWriter(f,new UTF8Encoding(false));writer.Write(data);writer.Flush();f.Flush(true);}File.Move(temp,path,true);}finally{if(File.Exists(temp))File.Delete(temp);}}
 static string Read(string path,int limit=12000){CheckPath(path);if(!File.Exists(path))return "";if(new FileInfo(path).Length>limit)throw new IOException("Список слишком большой для встроенного редактора (до 6000 символов на список).");return File.ReadAllText(path);}
 public void Recover(){if(!File.Exists(Journal))return;var previous=JsonSerializer.Deserialize<DomainDocument>(Read(Journal,100000))??throw new IOException("Повреждён журнал доменов");Write(Included,previous.Included);Write(Excluded,previous.Excluded);File.Delete(Journal);}
 public DomainDocument Load(){
  CheckPath(root);Recover();var a=Read(Included);var b=Read(Excluded);
  if(a.Length>6001||b.Length>6001)throw new IOException("Список слишком большой для редактора.");
  if(JsonSerializer.SerializeToUtf8Bytes(new DomainDocument(a,b)).Length>24000)throw new IOException("Список слишком большой для передачи. Сократите комментарии.");
  return new(a,b,Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(a+"\0"+b))),File.Exists(Backup));
 }
 public DomainDocument Save(DomainDocument requested,bool reset=false,bool restore=false){
  var old=Load();if(requested.Revision!=old.Revision)throw new IOException("Списки изменились. Перезагрузите редактор, чтобы не перезаписать чужие изменения.");
  var source=restore?(JsonSerializer.Deserialize<DomainDocument>(Read(Backup,100000))??throw new IOException("Нет резервной копии")):requested;
  var next=new DomainDocument(reset?"":DomainValidation.Normalize(source.Included),reset?"":DomainValidation.Normalize(source.Excluded));
  if(next.Included.Split('\n',StringSplitOptions.RemoveEmptyEntries).Intersect(next.Excluded.Split('\n',StringSplitOptions.RemoveEmptyEntries),StringComparer.OrdinalIgnoreCase).Any())throw new IOException("Один домен не может одновременно находиться в дополнениях и исключениях.");
  Write(Backup,JsonSerializer.Serialize(old));Write(Journal,JsonSerializer.Serialize(old));
  try{Write(Included,next.Included);Write(Excluded,next.Excluded);File.Delete(Journal);}
  catch{Recover();throw;}
  return Load();
 }
}
