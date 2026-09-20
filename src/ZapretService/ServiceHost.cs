using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
namespace ZapretDesktop;
sealed partial class ServiceHost(string root,SecurityIdentifier owner,IEngine engine,IProbe probe,string pipeName=ServicePaths.Pipe) {
 readonly object sync=new();
 ServiceSnapshot state=new(false,false,"Служба готова");
 CancellationTokenSource? selection;
 Task task=Task.CompletedTask;
 public ServiceSnapshot Snapshot {get{lock(sync)return state with{Running=engine.Running,Version="0.1.2",BootEnabled=boot?.Enabled,Features=1};}}
 void Set(ServiceSnapshot value){lock(sync)state=value;}
 public async Task<ServiceSnapshot> Handle(ServiceRequest request,CancellationToken lifetime){
  await commands.WaitAsync(lifetime);
  try{LoadBoot();return await HandleCore(request,lifetime);}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException){return Snapshot with{Error=ex.Message};}finally{commands.Release();}
 }
 async Task<ServiceSnapshot> HandleCore(ServiceRequest request,CancellationToken lifetime){
  switch(request.Command){
   case "status":return Snapshot;
   case "configure-boot":
    if(request.BootEnabled is null)return Snapshot with{Error="Не задан режим автоподключения"};
    if(request.Strategy is not null&&!Directory.GetFiles(root,"general*.bat").Any(f=>Path.GetFileNameWithoutExtension(f)==request.Strategy))return Snapshot with{Error="Неизвестная стратегия"};
    SaveBoot(new(request.BootEnabled.Value,request.Automatic,request.Strategy,boot?.LastWorking,boot?.Working));return Snapshot;
   case "domains-read":return Snapshot with{Domains=new DomainStore(root).Load()};
   case "domains-save":case "domains-reset":case "domains-restore":
    if(engine.Running||Snapshot.Busy||Snapshot.RecoveryPending)return Snapshot with{Error="Отключите подключение перед изменением списков"};
    if(request.Domains is null)return Snapshot with{Error="Нет данных списка"};
    return Snapshot with{Domains=new DomainStore(root).Save(request.Domains,request.Command=="domains-reset",request.Command=="domains-restore")};
   case "stop":generation++;desired=false;recovery.Disarm();selection?.Cancel();await task;await engine.StopAsync();Set(new(false,false,"Отключено"));return Snapshot;
   case "start":
    new DomainStore(root).Recover();
    if(Snapshot.Busy||engine.Running)return Snapshot with{Error="Сначала остановите текущее подключение"};
    var candidates=Directory.GetFiles(root,"general*.bat").OrderBy(f=>Path.GetFileName(f)=="general.bat"?0:1).ThenBy(f=>f).Select(f=>{try{return StrategyImporter.Read(f);}catch{return null;}}).Where(s=>s is not null).Cast<Strategy>().ToList();
    if(!request.Automatic)candidates=candidates.Where(s=>s.Name==request.Strategy).ToList();
    if(candidates.Count==0)return Snapshot with{Error="Нет совместимых стратегий в установленном пакете"};
    generation++;desired=true;lastRequest=request;recovery.Arm(NetworkKey(),DateTimeOffset.UtcNow);
    selection?.Dispose();selection=CancellationTokenSource.CreateLinkedTokenSource(lifetime);selection.CancelAfter(TimeSpan.FromMinutes(8));
    Set(new(false,true,"Подбираем подключение"));
    task=request.Preferred is not null&&candidates.Any(s=>s.Name==request.Preferred)?StartPreferred(candidates,request,selection.Token):Select(candidates,request,selection.Token);return Snapshot;
   default:return Snapshot with{Error="Неизвестная команда"};
  }
 }
 async Task StartPreferred(List<Strategy> candidates,ServiceRequest request,CancellationToken token){
  try{
   var chosen=candidates.First(s=>s.Name==request.Preferred);
   await engine.StartAsync(chosen,token);token.ThrowIfCancellationRequested();
   if(!engine.Running)throw new IOException("Движок завершился при запуске");
   Set(new(true,false,"Подключение запущено. Проверка выполняется в фоне.",chosen.Name));
   RememberStrategy(chosen.Name);
  }catch(OperationCanceledException){await engine.StopAsync();Set(new(false,false,"Подключение отменено"));}
  catch(Exception ex)when(ex is IOException){await engine.StopAsync();await Select(candidates,request,token);}
  catch(Exception ex){await engine.StopAsync();Set(new(false,false,"Не удалось запустить режим",Error:ex.Message));}
 }
 async Task Select(List<Strategy> candidates,ServiceRequest request,CancellationToken token){
  try{
   if(probe is WebProbe web)web.Reset();
   var result=await new AutoSelector(engine,probe).SelectAsync(candidates,request.Preferred,request.Automatic,s=>Set(Snapshot with{Message=s}),token);
   RememberStrategy(result.Strategy?.Name);
   Set(new(engine.Running,false,result.Verified?"Конфигурация подобрана":"Движок запущен вручную",result.Strategy?.Name,result.Probe,result.Verified));
  }catch(OperationCanceledException){Set(new(false,false,"Подбор отменён"));}
  catch(Exception ex){Set(new(engine.Running,false,"Не удалось подключиться",Probe:(ex as ProbeFailureException)?.Result,Error:ex.Message));}
 }
 public async Task Run(CancellationToken token){
  using var monitorStop=CancellationTokenSource.CreateLinkedTokenSource(token);
  new DomainStore(root).Recover();
  var startup=StartAtBoot(token);
  var monitoring=RecoveryLoop(monitorStop.Token);
  var security=new PipeSecurity();security.SetAccessRuleProtection(true,false);
  security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid,null),PipeAccessRights.FullControl,AccessControlType.Deny));
  foreach(var sid in new[]{owner,new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null),new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid,null)})security.AddAccessRule(new PipeAccessRule(sid,PipeAccessRights.FullControl,AccessControlType.Allow));
  try{while(!token.IsCancellationRequested){
   using var pipe=NamedPipeServerStreamAcl.Create(pipeName,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous,4096,4096,security);
   await pipe.WaitForConnectionAsync(token);
   using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(10));
   try{var request=await ServiceWire.Read<ServiceRequest>(pipe,timeout.Token);var result=await Handle(request,token);await ServiceWire.Write(pipe,result,timeout.Token);}
   catch(OperationCanceledException)when(!token.IsCancellationRequested){}
   catch(Exception ex)when(ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException){ }
  }}catch(OperationCanceledException)when(token.IsCancellationRequested){}
  finally{monitorStop.Cancel();await startup;await monitoring;selection?.Cancel();await task;await engine.StopAsync();selection?.Dispose();}
 }
}
