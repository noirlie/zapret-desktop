using System.Security.Principal;
using System.Text.Json;
using ZapretDesktop;
class Program {
 static int checks;
 static void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS "+message);checks++;}
 static void Reject(Action action,string label){try{action();}catch(IOException){Check(true,label);return;}throw new Exception(label);}
 static async Task Main(){
  var parent=Path.Combine(Path.GetTempPath(),"Zapret.BootDomains."+Guid.NewGuid().ToString("N"));var root=Path.Combine(parent,"components");Directory.CreateDirectory(root);
  try{
   var source=Path.GetFullPath("installer/payload/components");
   foreach(var file in Directory.GetFiles(source,"*",SearchOption.AllDirectories)){var target=Path.Combine(root,Path.GetRelativePath(source,file));Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(file,target);}
   var domains=new DomainStore(root);var original=domains.Load();
   var saved=domains.Save(original with{Included="Example.COM\nexample.com\nпример.рф",Excluded="excluded.example"});
   Check(saved.Included=="example.com\nxn--e1afmkfd.xn--p1ai\n","normalize IDN, casing and duplicates");
   Check(saved.HasBackup,"backup created");
   Reject(()=>domains.Save(original with{Included="stale.example"}),"stale revision rejected");
   foreach(var bad in new[]{"https://example.com","example.com/path","*.example.com","127.0.0.1","-bad.com","example.com:443","a..com","foo bar.com","localhost","example.com\n../../escape"})Reject(()=>DomainValidation.Normalize(bad),"reject "+bad.Replace('\n',' '));
   Reject(()=>domains.Save(saved with{Included="same.example",Excluded="same.example"}),"contradictory lists rejected");
   var stock=File.ReadAllText(Path.Combine(root,"lists","list-general.txt"));
   var reset=domains.Save(saved,reset:true);Check(reset.Included==""&&reset.Excluded=="","reset removes custom additions only");
   Check(File.ReadAllText(Path.Combine(root,"lists","list-general.txt"))==stock,"base list unchanged");
   var restored=domains.Save(reset,restore:true);Check(restored.Included==saved.Included&&restored.Excluded==saved.Excluded,"restore previous lists");
   File.WriteAllText(root+".domains-journal.json",JsonSerializer.Serialize(restored));File.WriteAllText(Path.Combine(root,"lists","list-general-user.txt"),"partial.example\n");
   Check(domains.Load().Included==restored.Included,"interrupted two-file transaction recovered");
   var owner=WindowsIdentity.GetCurrent().User!;var engine=new FakeEngine();var probe=new FakeProbe();var host=new ServiceHost(root,owner,engine,probe);
   var config=await host.Handle(new("configure-boot",false,"general (ALT)",BootEnabled:true),default);Check(config.BootEnabled==true,"boot preference persisted");
   await host.Handle(new("start",false,"general (ALT)"),default);await host.Handle(new("stop"),default);
   engine=new();probe=new();host=new(root,owner,engine,probe);
   await host.StartAtBoot(default);Check(engine.Running&&engine.Starts==1&&host.Snapshot.Strategy=="general (ALT)","service starts manual strategy without GUI");Check(probe.Calls==0,"boot does not wait for diagnostics");
   var duplicate=await host.Handle(new("start",false,"general (ALT)"),default);Check(duplicate.Error is not null&&engine.Starts==1,"duplicate engine prevented");
   var blocked=await host.Handle(new("domains-reset",Domains:restored),default);Check(blocked.Error is not null,"domain mutation blocked while engine running");
   await host.Handle(new("configure-boot",true,"general (ALT)",BootEnabled:true),default);await host.Handle(new("stop"),default);
   engine=new();probe=new();host=new(root,owner,engine,probe);await host.StartAtBoot(default);
   Check(engine.Running&&probe.Calls==0,"automatic boot uses saved mode before first probe");
   await host.Handle(new("stop"),default);await host.Handle(new("configure-boot",true,"general",BootEnabled:false),default);
   engine=new();host=new(root,owner,engine,probe);await host.StartAtBoot(default);Check(!engine.Running,"disabled boot never starts engine");
   var fast=await host.Handle(new("start",true,Preferred:"general (ALT)"),default);Check(fast.Running&&!fast.Busy&&!fast.Verified&&probe.Calls==0,"cached start returns immediately with honest pending verification");await host.Handle(new("stop"),default);
   var invalid=await host.Handle(new("configure-boot",false,"../evil",BootEnabled:true),default);Check(invalid.Error is not null,"unknown boot strategy rejected");
   var before=domains.Load();var throughPipe=await host.Handle(new("domains-save",Domains:before with{Included="api.example.com"}),default);Check(throughPipe.Domains?.Included=="api.example.com\n","domain save through service command");
   Console.WriteLine($"TOTAL {checks} boot/domain checks passed. Real service and user lists untouched.");
  }finally{if(Path.GetFullPath(parent).StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase))Directory.Delete(parent,true);}
 }
 class FakeEngine:IEngine{public bool Running{get;private set;}public int Starts;public Task StartAsync(Strategy s,CancellationToken t){t.ThrowIfCancellationRequested();Starts++;Running=true;return Task.CompletedTask;}public Task StopAsync(){Running=false;return Task.CompletedTask;}}
 class FakeProbe:IProbe{public int Calls;public Task<ProbeResult> CheckAsync(CancellationToken t){Calls++;throw new Exception("Unexpected startup probe");}}
}
