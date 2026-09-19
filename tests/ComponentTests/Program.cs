using ZapretDesktop;
var root=Path.Combine(AppContext.BaseDirectory,"case-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);int count=0,starts=0,stops=0;
void Check(bool ok,string title){if(!ok)throw new Exception(title);count++;Console.WriteLine("PASS "+title);}
string Create(string path,string version){
 Directory.CreateDirectory(Path.Combine(path,"bin"));Directory.CreateDirectory(Path.Combine(path,"lists"));
 foreach(var name in new[]{"winws.exe","WinDivert.dll","WinDivert64.sys","cygwin1.dll"})File.WriteAllText(Path.Combine(path,"bin",name),"test fixture only");
 File.WriteAllText(Path.Combine(path,"general.bat"),"start test \"%BIN%winws.exe\" --wf-tcp=443");
 File.WriteAllText(Path.Combine(path,"version"),version);
 foreach(var name in new[]{"list-general-user.txt","list-exclude-user.txt","ipset-exclude-user.txt"})File.WriteAllText(Path.Combine(path,"lists",name),version+"-user-data");
 return path;
}
string Version(string path)=>File.ReadAllText(Path.Combine(path,"version"));
Task Stop(){stops++;return Task.CompletedTask;}
Task Start(){starts++;return Task.CompletedTask;}
var tx=new ComponentTransaction(root);Create(tx.Current,"old");
var package=Create(Path.Combine(root,"staged"),"new");
await tx.Apply(package,Stop,Start);
Check(Version(tx.Current)=="new"&&Version(tx.Previous())=="old","successful replacement with backup");
Check(File.ReadAllText(Path.Combine(tx.Current,"lists","list-general-user.txt"))=="old-user-data","user domain list preserved");
Check(File.ReadAllText(Path.Combine(tx.Current,"lists","ipset-exclude-user.txt"))=="old-user-data","user IP exclusions preserved");
Check(starts==1&&stops==1,"one service stop/start");
File.WriteAllText(Path.Combine(tx.Current,"lists","list-general-user.txt"),"edited after upgrade");
await tx.Apply(tx.Previous(),Stop,Start);
Check(Version(tx.Current)=="old"&&Version(tx.Previous())=="new","manual rollback and reverse backup");
Check(File.ReadAllText(Path.Combine(tx.Current,"lists","list-general-user.txt"))=="edited after upgrade","manual rollback keeps current user edits");
package=Create(Path.Combine(root,"broken-start"),"bad");int attempt=0;
try{await tx.Apply(package,Stop,()=>{if(++attempt==1)throw new IOException("simulated engine failure");return Start();});throw new Exception("accepted failure");}
catch(IOException ex){Check(ex.Message.Contains("восстановлены")&&Version(tx.Current)=="old"&&attempt==2,"failed engine startup automatically rolls back");}
Check(Version(tx.Previous())=="new"&&Version(package)=="bad","prior rollback pointer and staged package retained");
attempt=0;var previous=tx.Previous();
try{await tx.Apply(previous,Stop,()=>{if(++attempt==1)throw new IOException("simulated rollback startup failure");return Start();});throw new Exception("accepted failure");}
catch(IOException){Check(Version(tx.Current)=="old"&&Version(tx.Previous())=="new","failed manual rollback retains both versions");}
package=Create(Path.Combine(root,"invalid"),"invalid");File.Delete(Path.Combine(package,"bin","winws.exe"));int before=stops;
try{await tx.Apply(package,Stop,Start);throw new Exception("invalid accepted");}catch(IOException){Check(stops==before&&Version(tx.Current)=="old","invalid package rejected before interruption");}
File.WriteAllText(Path.Combine(root,"previous-components.txt"),"../outside");
try{tx.Previous();throw new Exception("bad pointer");}catch(IOException){Check(true,"external backup pointer rejected");}
try{await tx.Apply(Path.Combine(root,"..","outside"),Stop,Start);throw new Exception("external stage");}catch(IOException){Check(true,"external staging path rejected");}
for(int stage=0;stage<3;stage++){
 var crashRoot=Path.Combine(root,"crash"+stage);Directory.CreateDirectory(crashRoot);
 var crash=new ComponentTransaction(crashRoot);
 Create(crash.Current,"old");var ready=Create(Path.Combine(crashRoot,"prepared"),"new");
 var backupName="components-backup-"+Guid.NewGuid().ToString("N");var backup=Path.Combine(crashRoot,backupName);
 Directory.Move(crash.Current,backup);
 if(stage>0)Directory.Move(ready,crash.Current);
 File.WriteAllText(Path.Combine(crashRoot,"component-transaction.json"),System.Text.Json.JsonSerializer.Serialize(new{Backup=backupName,Prepared="prepared",Previous=(string?)null,Committed=stage==2}));
 crash.RecoverInterrupted();
 Check(Version(crash.Current)==(stage==2?"new":"old")&&!crash.HasInterrupted,"crash recovery phase "+stage);
 if(stage==2)Check(Version(crash.Previous())=="old","committed recovery completes backup pointer");
}
Console.WriteLine("TOTAL "+count+" component transaction tests; no real Windows service changed.");

