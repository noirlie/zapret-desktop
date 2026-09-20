using System.Reflection;
using ZapretDesktop;
var installer=typeof(ServicePaths).Assembly.GetType("ZapretDesktop.Installer",true)!;
var replace=installer.GetMethod("ReplaceFile",BindingFlags.NonPublic|BindingFlags.Static)!;
var root=Path.Combine(Path.GetTempPath(),"zapret-installer-test-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try{
 var source=Path.Combine(root,"source.exe");var target=Path.Combine(root,"target.exe");
 File.WriteAllText(source,"new");File.WriteAllText(target,"old");
 using var handle=new FileStream(target,FileMode.Open,FileAccess.Read,FileShare.Read);
 var task=(Task)replace.Invoke(null,[source,target])!;
 await Task.Delay(100);
 if(task.IsFaulted)await task; if(task.IsCompleted)throw new Exception("Replacement did not wait for file lock");
 if(File.ReadAllText(target)!="old")throw new Exception("Old file corrupted before replacement");
 handle.Dispose();await task.WaitAsync(TimeSpan.FromSeconds(5));
 if(File.ReadAllText(target)!="new")throw new Exception("Replacement not completed");
 if(Directory.GetFiles(root,"*.incoming-*").Length!=0)throw new Exception("Temporary stage leaked");
 Console.WriteLine("PASS: locked executable waits; old content stays intact; atomic replacement succeeds after release");
 await (Task)replace.Invoke(null,[source,target])!;
 Console.WriteLine("PASS: repeat replacement of same version");
 var payload=Path.GetFullPath("installer/payload/components");
 var clean=Path.Combine(root,"clean");
 foreach(var sourceFile in Directory.GetFiles(payload,"*",SearchOption.AllDirectories)){
  var dest=Path.Combine(clean,Path.GetRelativePath(payload,sourceFile));Directory.CreateDirectory(Path.GetDirectoryName(dest)!);File.Copy(sourceFile,dest);
 }
 ComponentTransaction.EnsureUserLists(clean);ComponentTransaction.Validate(clean);
 Console.WriteLine("PASS: clean bundled components with all 22 strategies validate after user-list initialization");
 var userList=Path.Combine(clean,"lists","list-general-user.txt");
 File.WriteAllText(userList,"example.com");ComponentTransaction.EnsureUserLists(clean);
 if(File.ReadAllText(userList)!="example.com")throw new Exception("Existing user list overwritten");
 Console.WriteLine("PASS: repeated initialization preserves user lists");


 var cleanupRoot=Path.Combine(root,"cleanup");Directory.CreateDirectory(cleanupRoot);
 var owned=Path.Combine(cleanupRoot,"ZapretDesktopService");var profile=Path.Combine(cleanupRoot,"profile");
 var userData=Path.Combine(profile,"AppData","Local","ZapretDesktop.Alpha");
 var owner=System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value;
 Directory.CreateDirectory(owned);Directory.CreateDirectory(userData);
 File.WriteAllText(Path.Combine(owned,"owner.sid"),owner);File.WriteAllText(Path.Combine(owned,"components.desktop.json"),"configuration");
 File.WriteAllText(Path.Combine(userData,"preferences.json"),"settings");
 var archive=owned+".backup-"+new string('a',32);Directory.CreateDirectory(archive);File.WriteAllText(Path.Combine(archive,"owner.sid"),owner);File.WriteAllText(Path.Combine(archive,"private-list.txt"),"example.com");
 var unrelated=Path.Combine(cleanupRoot,"unrelated");Directory.CreateDirectory(unrelated);File.WriteAllText(Path.Combine(unrelated,"keep.txt"),"keep");
 var notOwned=owned+".backup-not-owned";Directory.CreateDirectory(notOwned);File.WriteAllText(Path.Combine(notOwned,"keep.txt"),"keep");
 var planned=InstallationCleanup.Plan(owned,profile,owner);
 if(planned.Length!=3||!File.Exists(Path.Combine(userData,"preferences.json")))throw new Exception("Planning must not erase data");
 Console.WriteLine("PASS: cleanup planning preserves settings until explicit purge");
 var lockedDriver=Path.Combine(owned,"components","bin","WinDivert64.sys");Directory.CreateDirectory(Path.GetDirectoryName(lockedDriver)!);File.WriteAllText(lockedDriver,"driver");
 using(var lockDriver=new FileStream(lockedDriver,FileMode.Open,FileAccess.Read,FileShare.Read)){
  try{InstallationCleanup.Purge(owned,profile,owner);throw new Exception("Locked driver silently skipped");}catch(IOException){}
  if(!File.Exists(Path.Combine(userData,"preferences.json")))throw new Exception("Settings removed before driver lock was resolved");
  if(!File.Exists(Path.Combine(owned,"owner.sid")))throw new Exception("Owner identity lost after driver lock");
 }
 Console.WriteLine("PASS: locked driver preserves settings and owner identity");
 using(var lockedSettings=new FileStream(Path.Combine(userData,"preferences.json"),FileMode.Open,FileAccess.Read,FileShare.Read)){
  try{InstallationCleanup.Purge(owned,profile,owner);throw new Exception("Locked settings silently skipped");}catch(IOException){}
  if(!File.Exists(Path.Combine(owned,"owner.sid")))throw new Exception("Identity lost before settings cleanup succeeded");
 }
 Console.WriteLine("PASS: locked settings report failure and retain identity for retry");
 InstallationCleanup.Purge(owned,profile,owner);
 if(Directory.Exists(owned)||Directory.Exists(userData)||Directory.Exists(archive))throw new Exception("Full cleanup left data");
 if(!File.Exists(Path.Combine(unrelated,"keep.txt"))||!Directory.Exists(notOwned))throw new Exception("Cleanup affected unrelated files");
 Console.WriteLine("PASS: full cleanup removes settings, configuration and owned backups only");
 if(InstallationCleanup.ValidateAppDirectory(@"D:\Apps\zapret")!=@"D:\Apps\zapret")throw new Exception("Custom drive not accepted");
 foreach(var invalid in new[]{@"C:\",@"\\server\share\zapret","relative"}){
  try{InstallationCleanup.ValidateAppDirectory(invalid);throw new Exception("Unsafe install directory accepted");}catch(IOException){}
 }
 Console.WriteLine("PASS: custom disk accepted; drive root, UNC and relative destinations rejected");
 Directory.CreateDirectory(owned);File.WriteAllText(Path.Combine(owned,"owner.sid"),"S-1-5-21-1-2-3-1001");
 try{InstallationCleanup.Purge(owned,profile,owner);throw new Exception("Foreign owner accepted");}catch(IOException){}
 if(!Directory.Exists(owned))throw new Exception("Foreign data removed");
 Console.WriteLine("PASS: foreign owner prevents cleanup");
 var pipe="Zapret.Setup.Test."+Guid.NewGuid().ToString("N");
 var identityFile=Path.Combine(root,"owner.sid");
 var capture=SetupIdentity.Capture(pipe,identityFile);
 await SetupIdentity.Identify(pipe);await capture;
 if(File.ReadAllText(identityFile)!=System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value)throw new Exception("Captured wrong Windows identity");
 Console.WriteLine("PASS: Windows-authenticated owner transfer; no client file write");

}finally{Directory.Delete(root,true);}

