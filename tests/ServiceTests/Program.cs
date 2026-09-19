using System.Diagnostics;
using System.Security.Principal;
using ZapretDesktop;
class Program {
 static int passed;
 static void Check(bool result,string name){if(!result)throw new Exception(name);Console.WriteLine("PASS "+name);passed++;}
 static async Task Main(string[] args){
  if(args.Contains("--child")){await Task.Delay(60000);return;}
  var pipe="ZapretDesktop.Test."+Guid.NewGuid().ToString("N");var engine=new FakeEngine();var probe=new FakeProbe();
  var host=new ServiceHost(Path.GetFullPath("installer/payload/components"),WindowsIdentity.GetCurrent().User!,engine,probe,pipe);
  using var lifetime=new CancellationTokenSource();var server=host.Run(lifetime.Token);
  var client=new ServiceClient(pipe);
  var s=await client.Send(new("status"));Check(!s.Running&&!s.Busy,"real local pipe status");
  s=await client.Send(new("start",false,"../../evil"));Check(s.Error is not null&&!engine.Running,"path injection rejected");
  s=await client.Send(new("shell"));Check(s.Error is not null&&!engine.Running,"arbitrary command rejected");
  s=await client.Send(new("start",false,"general (ALT)"));Check(s.Running&&!s.Verified,"manual strategy starts through pipe");
  var another=new ServiceClient(pipe);s=await another.Send(new("status"));Check(s.Running&&s.Strategy=="general (ALT)","new UI client reattaches without stopping engine");
  s=await another.Send(new("start",false,"general"));Check(s.Error is not null&&engine.Starts==1,"duplicate start rejected");
  await another.StopAsync();Check(!engine.Running,"explicit disconnect stops owned engine");
  probe.Delay=true;s=await client.Send(new("start",true));Check(s.Busy,"selection is asynchronous");
  s=await another.Send(new("status"));Check(s.Busy,"selection continues without initiating client");
  await another.StopAsync();Check(!engine.Running&&!host.Snapshot.Busy,"second client cancels selection");
  s=await client.Send(new("start",false,"general"));Check(s.Running,"restart after cancellation");
  host.NotifyResume();
  await Task.Delay(11000);
  Check(engine.Starts>=3&&host.Snapshot.Strategy=="general","resume recovery preserves manual strategy");
  Check(probe.Calls==1,"manual recovery does not use automatic health probes");
  host.NotifyResume();
  await Task.Delay(5100);
  await client.StopAsync();
  int starts=engine.Starts;
  await Task.Delay(5500);
  Check(!engine.Running&&engine.Starts==starts&&!host.Snapshot.RecoveryPending,"explicit stop cancels pending recovery");
  lifetime.Cancel();await server;Check(!engine.Running,"service shutdown stops engine");
  using(var malformed=new MemoryStream(System.Text.Encoding.UTF8.GetBytes(new string('x',32769)))){try{await ServiceWire.Read<ServiceRequest>(malformed,default);throw new Exception("limit");}catch(InvalidDataException){Check(true,"IPC message size enforced");}}
  using(var child=Process.Start(new ProcessStartInfo(Environment.ProcessPath!,"--child"){UseShellExecute=false,CreateNoWindow=true})!){
   using(var job=new EngineJob()){job.Attach(child);Check(!child.HasExited,"job owns test child");}
   using var timeout=new CancellationTokenSource(5000);await child.WaitForExitAsync(timeout.Token);Check(child.HasExited,"closing job terminates owned child");
  }
  Console.WriteLine($"TOTAL {passed} service tests passed. No Windows service installed, no winws started.");
 }
 class FakeEngine:IEngine {
  public bool Running{get;private set;}public int Starts;
  public Task StartAsync(Strategy strategy,CancellationToken t){t.ThrowIfCancellationRequested();Starts++;Running=true;return Task.CompletedTask;}
  public Task StopAsync(){Running=false;return Task.CompletedTask;}
 }
 class FakeProbe:IProbe{public bool Delay;public int Calls;public async Task<ProbeResult> CheckAsync(CancellationToken t){Calls++;if(Delay)await Task.Delay(Timeout.Infinite,t);return new(false,false,"blocked","blocked");}}
}


