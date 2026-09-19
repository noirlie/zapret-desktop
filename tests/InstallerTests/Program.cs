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

 var pipe="Zapret.Setup.Test."+Guid.NewGuid().ToString("N");
 var identityFile=Path.Combine(root,"owner.sid");
 var capture=SetupIdentity.Capture(pipe,identityFile);
 await SetupIdentity.Identify(pipe);await capture;
 if(File.ReadAllText(identityFile)!=System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value)throw new Exception("Captured wrong Windows identity");
 Console.WriteLine("PASS: Windows-authenticated owner transfer; no client file write");

}finally{Directory.Delete(root,true);}

