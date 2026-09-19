using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
namespace ZapretDesktop;
sealed partial class ServiceHost(string root,SecurityIdentifier owner,IEngine engine,IProbe probe,string pipeName=ServicePaths.Pipe) {
 readonly object sync=new();
 ServiceSnapshot state=new(false,false,"Служба готова");
 CancellationTokenSource? selection;
 Task task=Task.CompletedTask;
 public ServiceSnapshot Snapshot {get{lock(sync)return state with{Running=engine.Running,Version="0.1.0"};}}
 void Set(ServiceSnapshot value){lock(sync)state=value;}
 public async Task<ServiceSnapshot> Handle(ServiceRequest request,CancellationToken lifetime){
  await commands.WaitAsync(lifetime);
  try{return await HandleCore(request,lifetime);}finally{commands.Release();}
 }
 async Task<ServiceSnapshot> HandleCore(ServiceRequest request,CancellationToken lifetime){
  switch(request.Command){
   case "status":return Snapshot;
   case "stop":generation++;desired=false;recovery.Disarm();selection?.Cancel();await task;await engine.StopAsync();Set(new(false,false,"Отключено"));return Snapshot;
   case "start":
    if(Snapshot.Busy||engine.Running)return Snapshot with{Error="Сначала остановите текущее подключение"};
    var candidates=Directory.GetFiles(root,"general*.bat").OrderBy(f=>Path.GetFileName(f)=="general.bat"?0:1).ThenBy(f=>f).Select(f=>{try{return StrategyImporter.Read(f);}catch{return null;}}).Where(s=>s is not null).Cast<Strategy>().ToList();
    if(!request.Automatic)candidates=candidates.Where(s=>s.Name==request.Strategy).ToList();
    if(candidates.Count==0)return Snapshot with{Error="Нет совместимых стратегий в установленном пакете"};
    generation++;desired=true;lastRequest=request;recovery.Arm(NetworkKey(),DateTimeOffset.UtcNow);
    selection?.Dispose();selection=CancellationTokenSource.CreateLinkedTokenSource(lifetime);selection.CancelAfter(TimeSpan.FromMinutes(8));
    Set(new(false,true,"Подбираем подключение"));
    task=Select(candidates,request,selection.Token);return Snapshot;
   default:return Snapshot with{Error="Неизвестная команда"};
  }
 }
 async Task Select(List<Strategy> candidates,ServiceRequest request,CancellationToken token){
  try{
   if(probe is WebProbe web)web.Reset();
   var result=await new AutoSelector(engine,probe).SelectAsync(candidates,request.Preferred,request.Automatic,s=>Set(Snapshot with{Message=s}),token);
   Set(new(engine.Running,false,result.Verified?"Конфигурация подобрана":"Движок запущен вручную",result.Strategy?.Name,result.Probe,result.Verified));
  }catch(OperationCanceledException){Set(new(false,false,"Подбор отменён"));}
  catch(Exception ex){Set(new(engine.Running,false,"Не удалось подключиться",Probe:(ex as ProbeFailureException)?.Result,Error:ex.Message));}
 }
 public async Task Run(CancellationToken token){
  using var monitorStop=CancellationTokenSource.CreateLinkedTokenSource(token);
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
  finally{monitorStop.Cancel();await monitoring;selection?.Cancel();await task;await engine.StopAsync();selection?.Dispose();}
 }
}



