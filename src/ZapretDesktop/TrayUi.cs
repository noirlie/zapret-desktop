using System.Windows;
using System.Windows.Threading;
using Forms=System.Windows.Forms;
namespace ZapretDesktop;
public partial class MainWindow {
 Forms.NotifyIcon? tray;
 System.Drawing.Icon? trayIcon;
 Forms.ContextMenuStrip? trayMenu;
 Forms.ToolStripMenuItem? trayConnect;
 bool exitRequested,trayNoticeShown,trayExiting;
 void InitializeTray(){
  using var resource=Application.GetResourceStream(new Uri("pack://application:,,,/ZapretDesktop;component/Assets/zapret.ico"))!.Stream;
  using var loaded=new System.Drawing.Icon(resource);
  trayIcon=(System.Drawing.Icon)loaded.Clone();
  trayMenu=new Forms.ContextMenuStrip();
  trayMenu.Items.Add("Открыть",null,(_,_)=>Dispatcher.BeginInvoke(ShowFromTray));
  trayConnect=new Forms.ToolStripMenuItem("Подключить");
  trayConnect.Click+=(_,_)=>Dispatcher.BeginInvoke(()=>ToggleEngine(this,new RoutedEventArgs()));
  trayMenu.Items.Add(trayConnect);trayMenu.Items.Add(new Forms.ToolStripSeparator());
  trayMenu.Items.Add("Выйти",null,(_,_)=>Dispatcher.BeginInvoke(ExitFromTray));
  trayMenu.Opening+=(_,_)=>{trayConnect.Text=operation is not null||(engine.State.Busy||engine.State.RecoveryPending)?"Отменить подбор":connected?"Отключить":"Подключить";trayConnect.Enabled=!trayExiting;};
  tray=new Forms.NotifyIcon{Icon=trayIcon,Text="zapret — открыть двойным щелчком",ContextMenuStrip=trayMenu,Visible=true};
  tray.DoubleClick+=(_,_)=>Dispatcher.BeginInvoke(ShowFromTray);
  Closed+=(_,_)=>{tray.Visible=false;tray.Dispose();trayMenu.Dispose();trayIcon.Dispose();};
 }
 internal void ShowFromTray(){Show();if(WindowState==WindowState.Minimized)WindowState=WindowState.Normal;Activate();}
 void HideToTray(){
  if(tray is null){ShowFromTray();return;}
  Hide();
  if(!trayNoticeShown){trayNoticeShown=true;tray.ShowBalloonTip(2500,"zapret","Приложение свёрнуто в трей. Двойной щелчок по Z откроет окно.",Forms.ToolTipIcon.Info);}
 }
 async void ExitFromTray(){
  if(installerBusy){ShowFromTray();Progress.Text="Дождитесь завершения установки, затем повторите выход.";return;}
  if(trayExiting)return;trayExiting=true;monitor.Stop();
  try{
   operation?.Cancel();
   if(operationTask is not null)await operationTask.WaitAsync(TimeSpan.FromSeconds(20));
   // A lost status connection must not skip the stop command.
   await engine.StopAsync();
  }catch(Exception ex){
   AddLog("Выход: "+ex.Message);
   using var installed=Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\"+ServicePaths.Name);
   if(serviceAvailable||connected||installed is not null){
    ShowFromTray();
    if(MessageBox.Show("Служба не подтвердила отключение. Закрыть только приложение? Подключение может остаться активным.\n\n"+ex.Message,"zapret — выход",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes){
     trayExiting=false;monitor.Start();return;
    }
   }
  }
  // Explicitly stop the WPF message loop, even if another hidden window still exists.
  exitRequested=true;closingRequested=true;closing=true;lifetime.Cancel();operation?.Cancel();
  Application.Current.Shutdown();
 }
}

