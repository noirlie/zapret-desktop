using System.Diagnostics;
using ZapretDesktop;
if(args.Contains("--child"))return;
int count=0;
void Check(bool ok,string name){if(!ok)throw new Exception(name);count++;Console.WriteLine("PASS "+name);}
using(var own=Process.GetCurrentProcess()){
 Check(ProcessInspection.IsRunningAt(own,Environment.ProcessPath!),"live process matched");
 Check(!ProcessInspection.IsRunningAt(own,Environment.ProcessPath!+".other"),"foreign executable excluded");
}
for(int i=0;i<30;i++){
 using var child=Process.Start(new ProcessStartInfo(Environment.ProcessPath!,"--child"){UseShellExecute=false,CreateNoWindow=true})!;
 await child.WaitForExitAsync();
 Check(!ProcessInspection.IsRunningAt(child,Environment.ProcessPath!),"process exited before inspection "+i);
}
Console.WriteLine("TOTAL "+count+" process tests; no actual service stopped.");
