using System.Diagnostics;
using System.Net.Http;
using System.Windows;
namespace ZapretDesktop;
public partial class MainWindow {
 ComponentRelease? availableRelease;
 PreparedRelease? preparedRelease;
 string? preparedTag;
 CancellationTokenSource? updateOperation;
 async void UpdatePrimary(object sender,RoutedEventArgs e){
  if(installerBusy||updateOperation is not null||operation is not null)return;
  if(availableRelease is null||availableRelease.Tag==InstalledComponentVersion()){CheckUpdate(sender,e);return;}
  if(engine.State.Version!="0.1.0"){UpdateStatus.Text="Запустите установщик zapret повторно, чтобы обновить приложение и службу вместе.";return;}
  if(MessageBox.Show("Обновить компоненты? Подключение временно прервётся. Настройки и списки сохранятся.","zapret",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
  await ChangeComponents(["--components",availableRelease.Tag]);
  UpdateButton.Content="Проверить обновления";availableRelease=null;
 }
 async void CheckUpdate(object sender,RoutedEventArgs e){
  if(installerBusy||updateOperation is not null)return;
  using var cancellation=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);cancellation.CancelAfter(TimeSpan.FromSeconds(30));updateOperation=cancellation;
  UpdateButton.IsEnabled=false;DownloadUpdateButton.IsEnabled=false;CancelUpdateButton.IsEnabled=true;UpdateStatus.Text="Проверяем GitHub…";
  try{
   using var http=UpdateHttp.Create();
   availableRelease=await new ReleaseUpdater(http).Check(cancellation.Token);
   UpdateStatus.Text=$"Доступен стабильный выпуск {availableRelease.Tag} · {availableRelease.Size/1024.0/1024:F1} МБ. SHA-256 предоставлен GitHub. Установлено: {InstalledComponentVersion()}.";
   UpdateButton.Content=availableRelease.Tag==InstalledComponentVersion()?"Проверить обновления":"Установить обновление";
   UpdateStatus.Text=availableRelease.Tag==InstalledComponentVersion()?"У вас актуальные компоненты · "+availableRelease.Tag:"Доступно обновление · "+availableRelease.Tag+". Загрузим, проверим и установим автоматически.";
   AddLog("GitHub: стабильный выпуск "+availableRelease.Tag);
  }catch(OperationCanceledException){UpdateStatus.Text="Проверка отменена или время ожидания истекло.";}
  catch(Exception ex){availableRelease=null;UpdateStatus.Text="Не удалось проверить GitHub: "+UpdateHttp.Describe(ex);AddLog(ex.ToString());}
  finally{updateOperation=null;UpdateButton.IsEnabled=true;CancelUpdateButton.IsEnabled=false;DownloadUpdateButton.IsEnabled=availableRelease is not null;}
 }
 async void DownloadUpdate(object sender,RoutedEventArgs e){
  if(installerBusy||updateOperation is not null||availableRelease is null)return;
  using var cancellation=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);cancellation.CancelAfter(TimeSpan.FromMinutes(5));updateOperation=cancellation;
  UpdateButton.IsEnabled=DownloadUpdateButton.IsEnabled=OpenUpdateButton.IsEnabled=false;CancelUpdateButton.IsEnabled=true;UpdateProgress.Value=0;preparedRelease=null;preparedTag=null;InstallComponentsButton.IsEnabled=false;
  try{
   using var http=UpdateHttp.Create();
   UpdateStatus.Text="Загружаем архив. Рабочие компоненты не изменяются.";
   preparedRelease=await new ReleaseUpdater(http).Download(availableRelease,System.IO.Path.Combine(SettingsStore.DataDirectory,"updates"),new Progress<int>(value=>{if(updateOperation!=cancellation||preparedRelease is not null||cancellation.IsCancellationRequested)return;UpdateProgress.Value=value;UpdateStatus.Text=value<100?$"Загрузка: {value}%":"Проверяем SHA-256, структуру и совместимость стратегий…";}),cancellation.Token);
   preparedTag=availableRelease.Tag;
   UpdateStatus.Text=$"Пакет {availableRelease.Tag} проверен: {preparedRelease.Strategies} стратегий. Готов к установке. Пользовательские списки будут сохранены; потребуется подтверждение Windows.";
   AddLog("Проверен пакет "+availableRelease.Tag);
  }catch(OperationCanceledException){UpdateStatus.Text="Загрузка отменена или время ожидания истекло. Рабочие компоненты не изменены.";}
  catch(Exception ex){UpdateStatus.Text="Пакет не подготовлен: "+UpdateHttp.Describe(ex);AddLog(ex.ToString());}
  finally{updateOperation=null;UpdateButton.IsEnabled=DownloadUpdateButton.IsEnabled=true;CancelUpdateButton.IsEnabled=false;OpenUpdateButton.IsEnabled=InstallComponentsButton.IsEnabled=preparedRelease is not null;}
 }
 void CancelUpdate(object sender,RoutedEventArgs e)=>updateOperation?.Cancel();
 void OpenUpdate(object sender,RoutedEventArgs e){
  if(preparedRelease is null)return;
  try{Process.Start(new ProcessStartInfo(preparedRelease.Directory){UseShellExecute=true});}catch(Exception ex){UpdateStatus.Text=ex.Message;}
 }
 static string InstalledComponentVersion(){
  try{using var doc=System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(ServicePaths.Components,"release.json")));return doc.RootElement.GetProperty("Tag").GetString()??"неизвестно";}
  catch{return "неизвестно (компоненты установлены вручную)";}
 }
 async void InstallComponents(object sender,RoutedEventArgs e){
  if(installerBusy||updateOperation is not null||preparedTag is null||operation is not null)return;
  if(engine.State.Version!="0.1.0"){UpdateStatus.Text="Сначала обновите службу до 0.1.0 в настройках.";return;}
  if(MessageBox.Show("Установить компоненты "+preparedTag+"? Подключение временно прервётся. Установщик повторно загрузит и проверит пакет в защищённой папке; пользовательские списки сохранятся. При ошибке запуска будет выполнен откат.","zapret",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
  await ChangeComponents(["--components",preparedTag]);
 }
 async void RollbackComponents(object sender,RoutedEventArgs e){
  if(installerBusy||updateOperation is not null||operation is not null)return;
  if(!System.IO.File.Exists(System.IO.Path.Combine(ServicePaths.Root,"previous-components.txt"))){UpdateStatus.Text="Предыдущая версия отсутствует.";return;}
  if(MessageBox.Show("Вернуть предыдущие компоненты? Подключение временно прервётся. Текущие пользовательские списки сохранятся.","zapret",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
  await ChangeComponents(["--rollback-components"]);
 }
 async Task ChangeComponents(string[] args){
  UpdateButton.IsEnabled=DownloadUpdateButton.IsEnabled=InstallComponentsButton.IsEnabled=RollbackComponentsButton.IsEnabled=false;
  UpdateStatus.Text="Установка: загрузка, резервная копия и проверка запуска. Дождитесь завершения.";
  try{
   bool success=await RunInstaller(args);
   await AttachService();
   UpdateStatus.Text=success?"Операция завершена. Установлено: "+InstalledComponentVersion()+". Пользовательские списки сохранены.":"Операция не завершена. Подробности — в сообщении установщика. Установлено: "+InstalledComponentVersion();
   AddLog(UpdateStatus.Text);
  }finally{
   UpdateButton.IsEnabled=RollbackComponentsButton.IsEnabled=true;
   DownloadUpdateButton.IsEnabled=availableRelease is not null;InstallComponentsButton.IsEnabled=preparedRelease is not null;
  }
 }}






