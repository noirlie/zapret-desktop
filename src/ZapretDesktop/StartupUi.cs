using System.Windows;
namespace ZapretDesktop;
public partial class MainWindow {
 bool startupHandled,startupSettingsBusy,bootSyncReady;
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
  if(login&&preferences.StartHidden){WindowRendering.Log("Startup: first frame complete, hiding to tray");HideToTray();}
  try{
   for(int attempt=0;attempt<3;attempt++){
    await AttachService();
    if(serviceAvailable)break;
    if(attempt<2)await Task.Delay(2000,lifetime.Token);
   }
   bool legacyConnect=serviceAvailable&&(engine.State.Features<1||engine.State.BootEnabled is null);
   if(serviceAvailable&&engine.State.Features>=1){
    bootSyncReady=true;
    if(engine.State.BootEnabled is null)await SyncBootConfiguration();
    else{startupSettingsBusy=true;preferences.AutoConnect=engine.State.BootEnabled==true;ConnectOnStart.IsChecked=preferences.AutoConnect;startupSettingsBusy=false;Save();}
   }
   if(legacyConnect&&preferences.AutoConnect&&serviceAvailable&&!engine.State.Running&&!engine.State.Busy&&!engine.State.RecoveryPending&&!closingRequested){AddLog("Автоподключение при запуске приложения.");ToggleEngine(this,new RoutedEventArgs());}
  }catch(OperationCanceledException){}
 }
 async void StartupOptionChanged(object sender,RoutedEventArgs e){
  if(!ready||startupSettingsBusy)return;
  preferences.StartHidden=StartInTray.IsChecked==true;preferences.AutoConnect=ConnectOnStart.IsChecked==true;Save();await SyncBootConfiguration();
 }
 readonly SemaphoreSlim bootSettingsLock=new(1,1);
 async Task SyncBootConfiguration(){
  if(!serviceAvailable||!ready||!bootSyncReady)return;
  await bootSettingsLock.WaitAsync();
  try{
   if(engine.State.Features<1){StartupNote.Text="Для автономного автоподключения обновите приложение установщиком.";return;}
   var result=await engine.Send(new("configure-boot",preferences.Automatic,preferences.SelectedStrategy,BootEnabled:preferences.AutoConnect),lifetime.Token);
   if(result.Error is not null)throw new System.IO.IOException(result.Error);
   StartupNote.Text=preferences.AutoConnect?"Служба подключится при включении ПК, даже если окно не запущено.":"Автоподключение службы отключено. Можно подключиться вручную.";
  }catch(OperationCanceledException){}catch(Exception ex){StartupNote.Text="Не удалось сохранить автоподключение: "+ex.Message;AddLog(StartupNote.Text);}
  finally{bootSettingsLock.Release();}
 }
 void WindowsStartupChanged(object sender,RoutedEventArgs e){
  if(!ready||startupSettingsBusy)return;
  try{StartupRegistration.Set(StartWithWindows.IsChecked==true);StartupNote.Text=StartWithWindows.IsChecked==true?"Приложение запустится при входе в Windows.":"Запуск с Windows отключён.";}
  catch(Exception ex){StartupNote.Text="Не удалось изменить автозапуск: "+ex.Message;startupSettingsBusy=true;StartWithWindows.IsChecked=false;startupSettingsBusy=false;}
 }
}
