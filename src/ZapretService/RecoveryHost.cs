using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
namespace ZapretDesktop;
sealed partial class ServiceHost {
 readonly SemaphoreSlim commands=new(1,1);
 readonly RecoveryPolicy recovery=new();
 bool desired;
 long generation;
 ServiceRequest? lastRequest;
 int networkEpoch;
 public void NotifyResume()=>Interlocked.Increment(ref networkEpoch);
 static string NetworkKey(){
  try {var parts=NetworkInterface.GetAllNetworkInterfaces().Where(n=>n.OperationalStatus==OperationalStatus.Up&&n.NetworkInterfaceType!=NetworkInterfaceType.Loopback).OrderBy(n=>n.Id).Select(n=>n.Id+"|"+string.Join(",",n.GetIPProperties().GatewayAddresses.Select(a=>a.Address.ToString()))+"|"+string.Join(",",n.GetIPProperties().DnsAddresses.Select(a=>a.ToString())));return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(";",parts))));}
  catch(NetworkInformationException){return "unknown";}
 }
 async Task RecoveryLoop(CancellationToken token){
  var lastTick=DateTimeOffset.UtcNow;var nextHealth=lastTick+TimeSpan.FromSeconds(10);int observedEpoch=networkEpoch;
  NetworkAddressChangedEventHandler changed=(_,_)=>Interlocked.Increment(ref networkEpoch);
  NetworkChange.NetworkAddressChanged+=changed;
  try {while(!token.IsCancellationRequested){
   await Task.Delay(5000,token);
   var now=DateTimeOffset.UtcNow;int epoch=Volatile.Read(ref networkEpoch);bool resumed=now-lastTick>TimeSpan.FromSeconds(45)||epoch!=observedEpoch;observedEpoch=epoch;lastTick=now;
   long version;bool check;bool active;bool busy;
   await commands.WaitAsync(token);
   try{version=generation;active=desired;busy=Snapshot.Busy;check=active&&!busy&&engine.Running&&lastRequest?.Automatic==true&&NetworkInterface.GetIsNetworkAvailable()&&now>=nextHealth;}
   finally{commands.Release();}
   if(!active||busy)continue;
   bool? healthy=null;ProbeResult? healthResult=null;
   if(check){
    try{healthResult=await probe.CheckAsync(token);healthy=healthResult.Passed;}
    catch(OperationCanceledException)when(token.IsCancellationRequested){throw;}
    catch{healthy=false;}
    nextHealth=DateTimeOffset.UtcNow+TimeSpan.FromSeconds(45);
   }
   await commands.WaitAsync(token);
   try{
    if(!desired||version!=generation||Snapshot.Busy)continue;
    if(healthResult is not null){Set(Snapshot with{Probe=healthResult,Verified=healthResult.Passed,Message=healthResult.Passed?"Подключение проверено":"Проверка не подтвердила доступность. Повторим в фоне."});if(healthResult.Passed)RememberStrategy(Snapshot.Strategy);}
    var decision=recovery.Observe(DateTimeOffset.UtcNow,NetworkInterface.GetIsNetworkAvailable(),NetworkKey(),engine.Running,false,healthy,resumed);
    if(decision.Kind is "wait" or "limited"){Set(Snapshot with{Message=decision.Message,RecoveryPending=true});continue;}
    if(decision.Kind!="recover"||lastRequest is null)continue;
    var request=lastRequest with{Preferred=Snapshot.Strategy??lastRequest.Preferred};
    // Only the process owned by this service is stopped; a user stop invalidates this generation.
    await engine.StopAsync();
    var candidates=Directory.GetFiles(root,"general*.bat").OrderBy(f=>Path.GetFileName(f)=="general.bat"?0:1).ThenBy(f=>f).Select(f=>{try{return StrategyImporter.Read(f);}catch{return null;}}).Where(s=>s is not null).Cast<Strategy>().ToList();
    if(!request.Automatic)candidates=candidates.Where(s=>s.Name==request.Strategy).ToList();
    if(candidates.Count==0){desired=false;recovery.Disarm();Set(Snapshot with{Error="Нет стратегий для восстановления",Message="Требуется проверка компонентов"});continue;}
    selection?.Dispose();selection=CancellationTokenSource.CreateLinkedTokenSource(token);selection.CancelAfter(TimeSpan.FromMinutes(8));
    Set(new(false,true,"Восстановление: "+decision.Message));
    task=Select(candidates,request,selection.Token);
   }catch(Exception ex)when(ex is not OperationCanceledException){Set(Snapshot with{Message="Восстановление: "+ex.Message});}
   finally{commands.Release();}
  }}catch(OperationCanceledException)when(token.IsCancellationRequested){}
  finally{NetworkChange.NetworkAddressChanged-=changed;}
 }
}
