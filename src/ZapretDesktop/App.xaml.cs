using System.Windows;
namespace ZapretDesktop;
public partial class App : Application {
 SingleInstance? instance;
 Mutex? setupGuard;
 protected override void OnStartup(StartupEventArgs e){
  WindowRendering.Initialize();
  DispatcherUnhandledException+=(_,args)=>WindowRendering.Log("Unhandled UI exception: "+args.Exception);
  instance=new SingleInstance();
  if(!instance.IsPrimary){if(!e.Args.Contains("--startup"))instance.NotifyPrimary();Shutdown();return;}
  setupGuard=new Mutex(false,@"Local\ZapretDesktop.SetupGuard");
  instance.Listen(()=>{if(!Dispatcher.HasShutdownStarted)_=Dispatcher.BeginInvoke(()=>{if(MainWindow is MainWindow window)window.ShowFromTray();});});
  base.OnStartup(e);
  MainWindow=new MainWindow();
  MainWindow.Show();
 }
 protected override void OnExit(ExitEventArgs e){setupGuard?.Dispose();instance?.Dispose();base.OnExit(e);}
}
