using System.Windows;
namespace ZapretDesktop;
public partial class App : Application {
 SingleInstance? instance;
 Mutex? setupGuard;
 protected override void OnStartup(StartupEventArgs e){
  instance=new SingleInstance();
  if(!instance.IsPrimary){if(!e.Args.Contains("--startup"))instance.NotifyPrimary();Shutdown();return;}
  setupGuard=new Mutex(false,@"Local\ZapretDesktop.SetupGuard");
  instance.Listen(()=>{if(!Dispatcher.HasShutdownStarted)_=Dispatcher.BeginInvoke(()=>{if(MainWindow is MainWindow window)window.ShowFromTray();});});
  base.OnStartup(e);
 }
 protected override void OnExit(ExitEventArgs e){setupGuard?.Dispose();instance?.Dispose();base.OnExit(e);}
}

