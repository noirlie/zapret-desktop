using System.Net.Http;
using System.Text.Json;
namespace ZapretDesktop;
static partial class Installer {
 public static async Task RecoverComponents(){
  var transaction=new ComponentTransaction(ServicePaths.Root);
  if(!transaction.HasInterrupted)return;
  VerifyInstalledService();await StopComponentsService();transaction.RecoverInterrupted();
  if(await Run("start",ServicePaths.Name)!=0)throw new IOException("Файлы восстановлены после прерванной установки, но служба не запустилась");
 }
 static void VerifyInstalledService(){
  if(!EngineController.IsAdmin)throw new UnauthorizedAccessException();
  NoLinks(ServicePaths.Root);
  using var key=Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\"+ServicePaths.Name);
  if(!string.Equals(key?.GetValue("ImagePath") as string,"\""+Path.Combine(ServicePaths.Root,"ZapretService.exe")+"\"",StringComparison.OrdinalIgnoreCase))throw new IOException("Не найдена служба приложения");
  var acl=new DirectoryInfo(ServicePaths.Root).GetAccessControl();
  foreach(System.Security.AccessControl.FileSystemAccessRule rule in acl.GetAccessRules(true,true,typeof(System.Security.Principal.SecurityIdentifier))){
   if(rule.AccessControlType!=System.Security.AccessControl.AccessControlType.Allow)continue;
   var sid=rule.IdentityReference.Value;
   var write=System.Security.AccessControl.FileSystemRights.Write|System.Security.AccessControl.FileSystemRights.Delete|System.Security.AccessControl.FileSystemRights.ChangePermissions|System.Security.AccessControl.FileSystemRights.TakeOwnership;
   if((rule.FileSystemRights&write)!=0&&sid!="S-1-5-18"&&sid!="S-1-5-32-544")throw new IOException("Папка службы доступна для записи неадминистраторам");
  }
 }
 static async Task StopComponentsService(){
  await Run("stop",ServicePaths.Name);await WaitStopped();
 }
 static async Task StartComponentsService(ServiceSnapshot before){
  if(await Run("start",ServicePaths.Name)!=0)throw new IOException("Не удалось запустить службу");
  using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(9));
  var client=new ServiceClient();ServiceSnapshot? state=null;
  for(int i=0;i<8;i++){try{state=await client.Send(new("status"),timeout.Token);break;}catch(Exception ex)when(ex is IOException or OperationCanceledException){await Task.Delay(500,timeout.Token);}}
  if(state is null)throw new IOException("Служба не ответила после запуска");
  // Exercise actual winws startup, not just the service pipe. Restore the previous mode when connected.
  var strategy=before.Strategy??"general";
  state=await client.Send(new("start",before.Running&&before.Verified,strategy,strategy),timeout.Token);
  while(state.Busy){await Task.Delay(500,timeout.Token);state=await client.Send(new("status"),timeout.Token);}
  if(!state.Running||state.Error is not null)throw new IOException("Проверка запуска движка: "+(state.Error??state.Message));
  if(!before.Running)await client.StopAsync();
 }
 public static async Task UpdateComponents(string tag){
  VerifyInstalledService();
  var installed=await new ServiceClient().Send(new("status"));
  if(installed.Version!="0.1.0")throw new IOException("Сначала обновите службу до 0.1.0 в настройках.");
  if(installed.Busy||installed.RecoveryPending)throw new IOException("Сначала отмените подбор или восстановление.");
  using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(5));
  using var http=UpdateHttp.Create();
  var updater=new ReleaseUpdater(http);var release=await updater.Check(timeout.Token);
  if(release.Tag!=tag)throw new IOException("Выпуск на GitHub изменился. Повторите проверку в приложении.");
  // Download again directly into protected storage: never trust user-writable extracted files or hashes.
  var package=await updater.Download(release,Path.Combine(ServicePaths.Root,"packages"),null,timeout.Token);
  await File.WriteAllTextAsync(Path.Combine(package.Components,"release.json"),JsonSerializer.Serialize(new{release.Tag,release.Sha256,InstalledAt=DateTimeOffset.UtcNow}),timeout.Token);
  var before=await new ServiceClient().Send(new("status"));
  if(before.Busy||before.RecoveryPending)throw new IOException("Сначала отмените подбор или восстановление.");
  var transaction=new ComponentTransaction(ServicePaths.Root);
  await transaction.Apply(package.Components,StopComponentsService,()=>StartComponentsService(before));
 }
 public static async Task RollbackComponents(){
  VerifyInstalledService();
  var before=await new ServiceClient().Send(new("status"));
  if(before.Busy||before.RecoveryPending)throw new IOException("Сначала отмените подбор или восстановление.");
  var transaction=new ComponentTransaction(ServicePaths.Root);
  await transaction.Apply(transaction.Previous(),StopComponentsService,()=>StartComponentsService(before));
 }
}





