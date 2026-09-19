using System.Windows;
namespace ZapretDesktop;
public partial class MainWindow {
 bool startupHandled,startupSettingsBusy;
 void LoadStartupOptions(){
  startupSettingsBusy=true;
  try{StartWithWindows.IsChecked=StartupRegistration.Enabled;}catch(Exception e){AddLog("Автозапуск: "+e.Message);}
  StartInTray.IsChecked=preferences.StartHidden;ConnectOnStart.IsChecked=preferences.AutoConnect;
  startupSettingsBusy=false;
  if(!(AppContext.TryGetSwitch("ZapretDesktop.TestMode",out var testing)&&testing)&&StartupRegistration.Enabled){try{StartupRegistration.Set(true);}catch(Exception ex){AddLog("Автозапуск: "+ex.Message);}}
 }
 async Task StartApplication(){
  if(startupHandled)return;startupHandled=true;
  if(AppContext.TryGetSwitch("ZapretDesktop.TestMode",out var testing)&&testing)return;
  bool login=Environment.GetCommandLineArgs().Contains("--startup");
  if(login&&preferences.StartHidden){trayNoticeShown=true;HideToTray();}
  try{
   for(int attempt=0;attempt<3;attempt++){
    await AttachService();
    if(serviceAvailable)break;
    if(attempt<2)await Task.Delay(2000,lifetime.Token);
   }
   if(preferences.AutoConnect&&serviceAvailable&&!engine.State.Running&&!engine.State.Busy&&!engine.State.RecoveryPending&&!closingRequested){AddLog("Автоподключение при запуске приложения.");ToggleEngine(this,new RoutedEventArgs());}
  }catch(OperationCanceledException){}
 }
 void StartupOptionChanged(object sender,RoutedEventArgs e){
  if(!ready||startupSettingsBusy)return;
  preferences.StartHidden=StartInTray.IsChecked==true;preferences.AutoConnect=ConnectOnStart.IsChecked==true;Save();
 }
 void WindowsStartupChanged(object sender,RoutedEventArgs e){
  if(!ready||startupSettingsBusy)return;
  try{StartupRegistration.Set(StartWithWindows.IsChecked==true);StartupNote.Text=StartWithWindows.IsChecked==true?"Приложение запустится при входе в Windows.":"Запуск с Windows отключён.";}
  catch(Exception ex){StartupNote.Text="Не удалось изменить автозапуск: "+ex.Message;startupSettingsBusy=true;StartWithWindows.IsChecked=false;startupSettingsBusy=false;}
 }
}


