using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using ZapretDesktop;
class Program{
 [STAThread] static int Main(string[] args){
  if(args.Length==0){
   foreach(var mode in new[]{"connected","lost-status"}){
    using var child=Process.Start(new ProcessStartInfo(Environment.ProcessPath!,mode){UseShellExecute=false,CreateNoWindow=true})!;
    if(!child.WaitForExit(25000)){child.Kill();throw new Exception("Exit hung: "+mode);}
    if(child.ExitCode!=0)throw new Exception("Exit failed: "+mode);
    Console.WriteLine("PASS: actual child process exits through tray ("+mode+"), stop acknowledged, hidden window closed");
   }
   return 0;
  }
  AppContext.SetSwitch("ZapretDesktop.TestMode",true);
  var pipe="Zapret.Exit.Test."+Guid.NewGuid().ToString("N");bool stopped=false;
  var server=Task.Run(async()=>{
   using var socket=new NamedPipeServerStream(pipe,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous);
   await socket.WaitForConnectionAsync();
   var request=await ServiceWire.Read<ServiceRequest>(socket,default);
   if(request.Command!="stop")throw new Exception("Missing stop request");
   stopped=true;
   await ServiceWire.Write(socket,new ServiceSnapshot(false,false,"Отключено"),default);
  });
  var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
  SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
  var window=new MainWindow{Left=-10000,Top=-10000,ShowInTaskbar=false,WindowStartupLocation=WindowStartupLocation.Manual};
  var flags=BindingFlags.Instance|BindingFlags.NonPublic;
  typeof(MainWindow).GetField("engine",flags)!.SetValue(window,new ServiceClient(pipe));
  typeof(MainWindow).GetField("serviceAvailable",flags)!.SetValue(window,args[0]=="connected");
  var hidden=new Window{Left=-10000,Top=-10000,ShowInTaskbar=false};hidden.Show();hidden.Hide();
  window.Loaded+=(_,_)=>window.Dispatcher.BeginInvoke(()=>typeof(MainWindow).GetMethod("ExitFromTray",flags)!.Invoke(window,null));
  window.Show();app.Run();
  server.GetAwaiter().GetResult();
  return stopped?0:1;
 }
}
