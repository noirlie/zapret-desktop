using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Windows;
using System.Windows.Media;
namespace ZapretDesktop;
public partial class MainWindow {
 bool serviceAvailable,installerBusy;
 async Task AttachService(){
  try{
   var state=await engine.Send(new("status"),lifetime.Token);serviceAvailable=true;
   Folder.Text=ServicePaths.Components;connected=false;LoadStrategies(this,new RoutedEventArgs());
   ServiceStatus.Text="Служба "+(state.Version??"предыдущей версии")+" подключена."+(state.Version=="0.1.0"?" Автовосстановление доступно.":" Для установки обновлений компонентов обновите службу.");
   ApplyServiceState(state);
  }catch(OperationCanceledException){if(!closingRequested)ServiceStatus.Text="Служба не отвечает. Установите её или повторите подключение.";}
  catch(Exception ex){ServiceStatus.Text="Служба не подключена. Нужна однократная установка.";AddLog(ex.Message);}
 }
 void ApplyServiceState(ServiceSnapshot state){
  connected=state.Running;
  if(state.Busy){Heading.Text="Подбираем подключение";Caption.Text="Нажмите, чтобы отменить";Progress.Text=state.Message;Power.Opacity=.7;System.Windows.Automation.AutomationProperties.SetName(Power,"Отменить подбор");return;}
  Power.Opacity=1;
  if(state.Running){Heading.Text=state.Verified?"Конфигурация подобрана":"Движок работает в фоне";Caption.Text="Нажмите, чтобы отключить";Progress.Text="Режим: "+state.Strategy+" · "+state.Message;Power.Background=Brushes.Black;PowerIcon.Stroke=Brushes.White;System.Windows.Automation.AutomationProperties.SetName(Power,"Отключить");if(state.Probe is not null)ShowProbe(state.Probe);}
  else{ShowStopped();Progress.Text=state.Error??state.Message;if(state.Probe is not null)ShowProbe(state.Probe);}
  if(state.RecoveryPending){Heading.Text="Ожидаем восстановления";Caption.Text="Нажмите, чтобы отключить";System.Windows.Automation.AutomationProperties.SetName(Power,"Отключить");}
  UpdateMode();
 }
 async void RepairService(object sender,RoutedEventArgs e){
  if(installerBusy||operation is not null)return;
  await AttachService();
  if(serviceAvailable&&engine.State.Version=="0.1.0"){ServiceStatus.Text="Всё готово к подключению.";return;}
  using var registered=Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\"+ServicePaths.Name);
  if(registered is not null)UpgradeService(sender,e);
  else{ServiceStatus.Text="Запустите установщик zapret повторно. Он восстановит службу и компоненты, сохранив настройки.";}
 }
 async void UpgradeService(object sender,RoutedEventArgs e){
  if(installerBusy)return;
  if(operation is not null||engine.State.Busy){ServiceStatus.Text="Сначала отмените подбор.";return;}
  if(MessageBox.Show("Обновить службу? Текущее подключение остановится. После обновления подключитесь снова.","zapret",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
  if(await RunInstaller(["--upgrade"]))await AttachService();
 }
 async void ReconnectService(object sender,RoutedEventArgs e)=>await AttachService();
 async void InstallService(object sender,RoutedEventArgs e){
  if(installerBusy)return;
  if(operation is not null||connected){ServiceStatus.Text="Сначала остановите текущее подключение.";return;}
  var source=Folder.Text;
  if(string.Equals(Path.GetFullPath(source),ServicePaths.Components,StringComparison.OrdinalIgnoreCase)){ServiceStatus.Text="Выберите исходную папку zapret перед установкой.";return;}
  await RunInstaller(["--install",source,WindowsIdentity.GetCurrent().User!.Value]);
  await AttachService();
 }
 async void UninstallService(object sender,RoutedEventArgs e){
  if(installerBusy)return;
  if(operation is not null){ServiceStatus.Text="Сначала отмените подбор.";return;}
  if(MessageBox.Show("Остановить подключение и удалить службу? Копия компонентов останется в Program Files.","zapret",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
  if(await RunInstaller(["--uninstall"])){serviceAvailable=false;ShowStopped();ServiceStatus.Text="Служба удалена. Компоненты сохранены.";}
 }
 async Task<bool> RunInstaller(string[] args){
  if(installerBusy)return false;installerBusy=true;
  Power.IsEnabled=false;
  InstallServiceButton.IsEnabled=false;UpgradeServiceButton.IsEnabled=false;
  try{
   var file=Path.Combine(AppContext.BaseDirectory,"service","ZapretService.exe");
   if(!File.Exists(file))throw new IOException("В сборке отсутствует установщик службы.");
   var start=new ProcessStartInfo(file){UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Hidden};foreach(var arg in args)start.ArgumentList.Add(arg);
   using var process=Process.Start(start)??throw new IOException("Установщик не запущен");await process.WaitForExitAsync();
   if(process.ExitCode!=0)throw new IOException("Операция не завершена. См. сообщение установщика.");
   ServiceStatus.Text="Операция завершена.";return true;
  }catch(Exception ex){ServiceStatus.Text=ex.Message;AddLog(ex.Message);return false;}
  finally{installerBusy=false;Power.IsEnabled=true;InstallServiceButton.IsEnabled=true;UpgradeServiceButton.IsEnabled=true;}
 }
}







