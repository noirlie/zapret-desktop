using ZapretDesktop;
using System.Diagnostics;
class Program {
 static int n;static void Check(bool ok,string label){if(!ok)throw new Exception(label);Console.WriteLine("PASS "+label);n++;}
 static int Main(string[] args) {
  if(args.Length==2&&args[0]=="--secondary"){using var client=new SingleInstance(args[1]);if(client.IsPrimary)return 2;client.NotifyPrimary();return 0;}
  string name=@"Local\ZapretDesktop.Tests."+Guid.NewGuid().ToString("N");
  using(var first=new SingleInstance(name)){
   Check(first.IsPrimary,"first launch owns instance");
   using var notified=new ManualResetEventSlim();first.Listen(()=>notified.Set());
   using(var process=Process.Start(new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true,ArgumentList={"--secondary",name}})!){process.WaitForExit(5000);Check(process.ExitCode==0,"second process does not become primary");}
   Check(notified.Wait(2000),"second launch signals original instance");
  }
  using(var replacement=new SingleInstance(name))Check(replacement.IsPrimary,"exit releases instance lock");
  Check(StartupRegistration.Command(@"C:\Program Files\zapret\ZapretDesktop.exe")=="\"C:\\Program Files\\zapret\\ZapretDesktop.exe\" --startup","startup command quotes paths");
  try{StartupRegistration.Command("relative.exe");throw new Exception("accepted relative path");}catch(ArgumentException){Check(true,"invalid startup path rejected");}
  var store=new SettingsStore(Path.Combine(AppContext.BaseDirectory,"settings"));store.Save(new UserSettings{StartHidden=true,AutoConnect=true});var saved=store.Load();Check(saved.StartHidden&&saved.AutoConnect,"startup preferences persist");
  Console.WriteLine($"TOTAL {n} tests; Windows autorun was not modified.");return 0;
 }
}
