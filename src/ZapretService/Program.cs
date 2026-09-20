using System.Runtime.InteropServices;
using System.Security.Principal;
namespace ZapretDesktop;
static class Program {
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int MessageBox(IntPtr window,string text,string title,uint flags);
 static FileStream AcquireInstallerLock(){
  if(!EngineController.IsAdmin)throw new UnauthorizedAccessException("Требуются права администратора");
  Directory.CreateDirectory(ServicePaths.Root);ComponentTransaction.NoLinks(ServicePaths.Root);
  var path=ServicePaths.Root+".installer.lock";
  if(File.Exists(path)&&(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("Ссылка вместо файла блокировки");
  return new FileStream(path,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
 }
 static void RecoverOnServiceStart(){
  FileStream? guard=null;
  try{guard=AcquireInstallerLock();}catch(IOException ex)when((ex.HResult&0xFFFF)==32){return;}
  using(guard)new ComponentTransaction(ServicePaths.Root).RecoverInterrupted();
 }
 static int Main(string[] args){
  try{
   if(args.Length==2&&args[0]=="--migrate-startup"){if(StartupRegistration.Enabled)StartupRegistration.Set(true,args[1]);return 0;}
   if(args.Length==3&&args[0]=="--capture-owner"){SetupIdentity.Capture(args[1],args[2]).GetAwaiter().GetResult();return 0;}
   if(args.Length==2&&args[0]=="--identify-owner"){SetupIdentity.Identify(args[1]).GetAwaiter().GetResult();return 0;}
   if(args.Length==1&&args[0]=="--version"){Console.WriteLine("0.1.2");return 0;}
   if(args.Length is 2 or 3&&args[0]=="--remove-installation"){
    if(args.Length==3&&args[2]!="--purge")throw new ArgumentException("Неизвестный режим очистки");
    using(var cleanupLock=Directory.Exists(ServicePaths.Root)?AcquireInstallerLock():null)
     Installer.RemoveInstallation(args[1],args.Length==3).GetAwaiter().GetResult();
    if(args.Length==3&&!Directory.Exists(ServicePaths.Root)){
     var lockPath=ServicePaths.Root+".installer.lock";
     if(File.Exists(lockPath))File.Delete(lockPath);
    }
    return 0;
   }
   using var installerLock=args.Length>0?AcquireInstallerLock():null;
   if(args.Length>0)Installer.RecoverComponents().GetAwaiter().GetResult();
   if(args.Length==2&&args[0]=="--components"){Installer.UpdateComponents(args[1]).GetAwaiter().GetResult();return 0;}
   if(args.Length==1&&args[0]=="--rollback-components"){Installer.RollbackComponents().GetAwaiter().GetResult();return 0;}
   if(args.Length is 3 or 4&&args[0]=="--setup"){Installer.Setup(args[1],args[2],args.Length==4?args[3]:null).GetAwaiter().GetResult();return 0;}
   if(args.Length==3&&args[0]=="--install"){Installer.Install(args[1],args[2]).GetAwaiter().GetResult();return 0;}

   if(args.Length==1&&args[0]=="--uninstall"){Installer.Uninstall().GetAwaiter().GetResult();return 0;}
   if(args.Length==1&&args[0]=="--upgrade"){Installer.Upgrade().GetAwaiter().GetResult();return 0;}
   if(args.Length!=0)throw new ArgumentException("Неизвестные параметры");
   var owner=new SecurityIdentifier(File.ReadAllText(Path.Combine(ServicePaths.Root,"owner.sid")).Trim());
   NativeService.Run(async token=>{RecoverOnServiceStart();using var job=new EngineJob();var engine=new EngineController(_=>{},job.Attach);var host=new ServiceHost(ServicePaths.Components,owner,engine,new WebProbe());NativeService.Resumed=host.NotifyResume;try{await host.Run(token);}finally{NativeService.Resumed=null;}});return 0;
  }catch(Exception ex){
   if(args.Length>0){
    try{File.WriteAllText(Path.Combine(ServicePaths.Root,"installer-error.log"),DateTimeOffset.Now+"\n"+ex);}catch{}
    if(args[0] is not ("--setup" or "--capture-owner" or "--identify-owner"))MessageBox(IntPtr.Zero,UpdateHttp.Describe(ex),"zapret — установка службы",0x10);
   }
   return 1;
  }
 }
}




