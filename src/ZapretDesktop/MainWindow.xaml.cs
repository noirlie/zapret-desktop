using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
namespace ZapretDesktop;
public partial class MainWindow : Window {
 readonly CancellationTokenSource lifetime=new();
 readonly SettingsStore store=new();
 readonly UserSettings preferences;
 readonly ServiceClient engine;
 readonly WebProbe probe=new();
 readonly DispatcherTimer monitor=new(){Interval=TimeSpan.FromSeconds(3)};
 CancellationTokenSource? operation;
 Task? operationTask;
 List<Strategy> strategies=new();
 bool closing,closingRequested,ready,connected,probing;
 string network="";
 public MainWindow() {
  InitializeComponent();
  SourceInitialized+=(_,_)=>ApplyWindowCorners();
  preferences=store.Load();
  engine=new ServiceClient();
  Folder.Text=string.IsNullOrWhiteSpace(preferences.Folder)?ServicePaths.Components:preferences.Folder;
  AutoMode.IsChecked=preferences.Automatic;
  LoadStrategies(this,new RoutedEventArgs());
  ready=true;UpdateMode();
  InitializeTray();
  LoadStartupOptions();
  AddLog("Версия 0.1.0. Автоподбор проверяет доступность сервисов.");
  monitor.Tick+=Monitor;monitor.Start();Loaded+=async (_,_)=>await StartApplication();
  Closing+=async (_,e)=>{
   if(closing)return;
   if(!exitRequested){e.Cancel=true;HideToTray();return;}
   e.Cancel=true;if(closingRequested)return;closingRequested=true;
   lifetime.Cancel();operation?.Cancel();
   try {if(operationTask is not null)await operationTask;closing=true;monitor.Stop();_ = Dispatcher.BeginInvoke(Close);}
   catch(Exception ex){closingRequested=false;AddLog("Не удалось завершить: "+ex.Message);}
  };
 }
 void AddLog(string message) {
  if(closing)return;
  var line=$"[{DateTime.Now:HH:mm:ss}] {message}";
  Log.AppendText(line+"\n");if(Log.Text.Length>60000)Log.Text=Log.Text[^45000..];Log.ScrollToEnd();
  try{Directory.CreateDirectory(SettingsStore.DataDirectory);var path=Path.Combine(SettingsStore.DataDirectory,"session.log");if(File.Exists(path)&&new FileInfo(path).Length>1_000_000)File.Move(path,path+".previous",true);File.AppendAllText(path,line+Environment.NewLine);}catch(IOException){}catch(UnauthorizedAccessException){}
 }
 void Save(){try{store.Save(preferences);}catch(Exception e){AddLog("Настройки не сохранены: "+e.Message);}}
 void UpdateMode(){if(ManualStrategyPanel is not null)ManualStrategyPanel.Visibility=AutoMode.IsChecked==true?Visibility.Collapsed:Visibility.Visible;Mode.Text=AutoMode.IsChecked==true?"Автоматический подбор":"Ручной режим: "+(Strategies.SelectedItem as Strategy)?.Name;Strategies.IsEnabled=operation is null&&!connected&&AutoMode.IsChecked!=true;}
 void LoadStrategies(object sender,RoutedEventArgs e){
  if(operation is not null||connected)return;
  try{
   var result=new List<Strategy>();
   foreach(var file in Directory.GetFiles(Folder.Text,"general*.bat").OrderBy(x=>Path.GetFileName(x)=="general.bat"?0:1).ThenBy(x=>x))
    try{result.Add(StrategyImporter.Read(file));}catch(Exception ex){AddLog(Path.GetFileName(file)+": пропущена — "+ex.Message);}
   strategies=result;Strategies.ItemsSource=result;Strategies.SelectedItem=result.FirstOrDefault(s=>s.Name==preferences.SelectedStrategy)??result.FirstOrDefault();
   preferences.Folder=Path.GetFullPath(Folder.Text);Save();AddLog("Доступно стратегий: "+result.Count);UpdateMode();
  }catch(Exception ex){Progress.Text="Не удалось загрузить компоненты. Откройте настройки.";AddLog(ex.Message);}
 }
 void StrategyChanged(object sender,SelectionChangedEventArgs e){if(!ready)return;preferences.SelectedStrategy=(Strategies.SelectedItem as Strategy)?.Name??"general";Save();UpdateMode();}
 void ModeChanged(object sender,RoutedEventArgs e){if(!ready)return;preferences.Automatic=AutoMode.IsChecked==true;Save();UpdateMode();}
 void BrowseFolder(object sender,RoutedEventArgs e){var dialog=new Microsoft.Win32.OpenFolderDialog{Title="Папка zapret с bin, lists и general.bat"};if(dialog.ShowDialog()==true){Folder.Text=dialog.FolderName;LoadStrategies(sender,e);}}
 void Elevate(object sender,RoutedEventArgs e){if(operation is not null||connected)return;try{Save();Process.Start(new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=true,Verb="runas"});Close();}catch(Exception ex){AddLog("Повышение прав: "+ex.Message);}}
 async void ToggleEngine(object sender,RoutedEventArgs e){
  if(installerBusy)return;
  if(operation is not null){operation.Cancel();Caption.Text="Отменяем подбор…";return;}
  if(connected||(engine.State.Busy||engine.State.RecoveryPending)){await Disconnect();return;}
  if(!serviceAvailable){Progress.Text="Установите или подключите службу в настройках.";return;}
  if(probing){Progress.Text="Дождитесь окончания диагностики.";return;}
  if(strategies.Count==0){Progress.Text="Выберите папку компонентов в настройках.";return;}
  operation=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);operation.CancelAfter(TimeSpan.FromMinutes(8));
  operationTask=Connect(operation);await operationTask;
 }
 async Task Connect(CancellationTokenSource request){
  Settings.IsEnabled=false;ProbeButton.IsEnabled=false;Heading.Text="Подбираем подключение";Caption.Text="Нажмите круг, чтобы отменить";
  System.Windows.Automation.AutomationProperties.SetName(Power,"Отменить подбор");Power.Opacity=.7;
  try{
   network=SettingsStore.NetworkKey();preferences.Working.TryGetValue(network,out var cached);
   if(cached is null)preferences.ManualCandidates.TryGetValue(network,out cached);
   var state=await engine.Send(new("start",AutoMode.IsChecked==true,(Strategies.SelectedItem as Strategy)?.Name,cached),request.Token);
   while(state.Busy){
    Progress.Text=state.Message;
    await Task.Delay(400,request.Token);
    state=await engine.Send(new("status"),request.Token);
   }
   if(state.Error is not null)throw new ProbeFailureException(state.Error,state.Probe??new(false,false,"Не проверен","Не проверен"));
   ApplyServiceState(state);
   if(state.Running&&state.Strategy is not null){if(state.Verified)preferences.Working[network]=state.Strategy;else preferences.ManualCandidates[network]=state.Strategy;Save();}
  }catch(OperationCanceledException){
   if(!closingRequested){try{await engine.StopAsync();}catch(Exception stop){AddLog(stop.Message);}ShowStopped();Progress.Text="Подбор отменён или связь со службой прервана.";}
  }catch(Exception ex){if(!closingRequested){Progress.Text=ex.Message;AddLog(ex.Message);if(ex is ProbeFailureException failure)ShowProbe(failure.Result);}}
  finally{operation=null;request.Dispose();Settings.IsEnabled=true;ProbeButton.IsEnabled=true;Power.Opacity=1;UpdateMode();}
 }
 async Task Disconnect(){Power.IsEnabled=false;try{await engine.StopAsync();ShowStopped();}catch(Exception ex){Progress.Text=ex.Message;AddLog(ex.Message);}finally{Power.IsEnabled=true;}}
 void ShowStopped(){connected=false;Heading.Text="Всё начинается с одной кнопки";Caption.Text="Нажмите, чтобы подключиться";Progress.Text="Движок не запущен";Power.Background=Brushes.White;PowerIcon.Stroke=Brushes.Black;YouTube.Text=Discord.Text="Не проверен";System.Windows.Automation.AutomationProperties.SetName(Power,"Подключить");UpdateMode();}
 void ShowProbe(ProbeResult r){YouTube.Text=r.YouTubeDetail;Discord.Text=r.DiscordDetail;}
 bool refreshing;
 async void Monitor(object? sender,EventArgs e){
  if(installerBusy||closingRequested||operation is not null||refreshing||!serviceAvailable)return;
  refreshing=true;
  try{ApplyServiceState(await engine.Send(new("status"),lifetime.Token));}
  catch(OperationCanceledException){}
  catch(Exception ex){serviceAvailable=false;ServiceStatus.Text="Нет связи со службой. Подключение может продолжать работать.";AddLog(ex.Message);}
  finally{refreshing=false;}
 }
 async void ProbeClick(object sender,RoutedEventArgs e){
  if(probing||operation is not null)return;probing=true;ProbeButton.IsEnabled=false;
  try{YouTube.Text=Discord.Text="Проверяется…";var r=await probe.CheckAsync(lifetime.Token);if(!closingRequested){ShowProbe(r);AddLog("YouTube: "+r.YouTubeDetail+"; Discord: "+r.DiscordDetail);}}
  catch(OperationCanceledException){}catch(Exception ex){AddLog(ex.Message);}finally{probing=false;ProbeButton.IsEnabled=true;}
 }
 void ExportLog(object sender,RoutedEventArgs e){var dialog=new Microsoft.Win32.SaveFileDialog{FileName="zapret-diagnostics.txt",Filter="Текстовый отчёт|*.txt"};if(dialog.ShowDialog()!=true)return;try{var text=Log.Text.Replace(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"[USER]",StringComparison.OrdinalIgnoreCase);File.WriteAllText(dialog.FileName,"zapret 0.1.0 — диагностика подключения\n"+text);}catch(Exception ex){AddLog("Экспорт: "+ex.Message);}}
}




