using System.Diagnostics;
using System.Security.Principal;
using ZapretDesktop;
if(!args.Contains("--run")){Console.WriteLine("Use --run from an elevated terminal after closing the app. Restarts the real installed service and restores boot settings.");return;}
if(!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))throw new Exception("Administrator required");
if(Process.GetProcessesByName("ZapretDesktop").Length!=0)throw new Exception("Close the GUI before this test");
var client=new ServiceClient();var initial=await client.Send(new("status"));
if(initial.Running||initial.Busy||initial.RecoveryPending)throw new Exception("Existing connection is active: not modified");
if(initial.Features<1)throw new Exception("Install the new service first");
var config=ServicePaths.Components+".desktop.json";var before=File.Exists(config)?File.ReadAllBytes(config):null;
var lists=await client.Send(new("domains-read"));if(lists.Domains is null||lists.Error is not null)throw new Exception("Cannot read domains");
static async Task Sc(string verb){using var p=Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,"sc.exe"),verb+" "+ServicePaths.Name){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true})!;var text=await p.StandardOutput.ReadToEndAsync();await p.WaitForExitAsync();if(p.ExitCode!=0)throw new Exception(text);}
static async Task WaitStopped(){for(int i=0;i<100;i++){var processes=Process.GetProcessesByName("ZapretService");bool done=processes.Length==0;foreach(var p in processes)p.Dispose();if(done)return;await Task.Delay(100);}throw new Exception("Service did not stop");}
try{
 var configured=await client.Send(new("configure-boot",false,"general (ALT)",BootEnabled:true));if(configured.Error is not null)throw new Exception(configured.Error);
 await Sc("stop");await WaitStopped();var timer=Stopwatch.StartNew();await Sc("start");
 ServiceSnapshot? running=null;
 for(int i=0;i<60;i++){try{running=await client.Send(new("status"),timeoutSeconds:1);if(running.Running&&!running.Busy)break;}catch(OperationCanceledException){}await Task.Delay(100);}
 if(running?.Running!=true||running.Strategy!="general (ALT)"||running.Verified)throw new Exception("Autonomous manual startup failed");
 Console.WriteLine($"PASS real service starts ALT without GUI or diagnostics in {timer.Elapsed.TotalSeconds:F2}s");
 await client.StopAsync();await client.Send(new("configure-boot",true,"general (ALT)",BootEnabled:true));
 // Remember a known mode once, then restart just as Windows would.
 var start=await client.Send(new("start",true,Preferred:"general (ALT)"));for(int i=0;start.Busy&&i<30;i++){await Task.Delay(100);start=await client.Send(new("status"));}if(!start.Running)throw new Exception("Cached start failed");
 await client.StopAsync();await Sc("stop");await WaitStopped();timer.Restart();await Sc("start");
 for(int i=0;i<60;i++){try{running=await client.Send(new("status"),timeoutSeconds:1);if(running.Running&&!running.Busy)break;}catch(OperationCanceledException){}await Task.Delay(100);}
 if(running?.Running!=true||running.Strategy!="general (ALT)")throw new Exception("Autonomous automatic startup failed");
 Console.WriteLine($"PASS real automatic service starts cached ALT without GUI in {timer.Elapsed.TotalSeconds:F2}s");
 await Task.Delay(12000);var health=await client.Send(new("status"));Console.WriteLine($"INFO background verification: running={health.Running}, verified={health.Verified}");
 var after=await client.Send(new("domains-read"));if(after.Domains?.Revision!=lists.Domains.Revision)throw new Exception("Lists changed");Console.WriteLine("PASS actual user lists unchanged");
}finally{
 try{await client.StopAsync();}finally{
  await Sc("stop");await WaitStopped();
  if(before is null){if(File.Exists(config))File.Delete(config);}else File.WriteAllBytes(config,before);
  await Sc("start");Console.WriteLine("RESTORED original boot configuration");
 }
}
