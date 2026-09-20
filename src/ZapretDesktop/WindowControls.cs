using System.Windows;
namespace ZapretDesktop;
public partial class MainWindow {
 [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
 static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
 [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
 static extern int DwmGetWindowAttribute(IntPtr window,int attribute,out int value,int size);
 void ApplyWindowCorners(){
  if(!OperatingSystem.IsWindowsVersionAtLeast(10,0,22000))return;
  var handle=new System.Windows.Interop.WindowInteropHelper(this).Handle;
  int preference=2;
  var setResult=DwmSetWindowAttribute(handle,33,ref preference,sizeof(int));
  var getResult=DwmGetWindowAttribute(handle,33,out var applied,sizeof(int));
  if(setResult!=0||getResult!=0||applied!=preference)
   WindowRendering.Log($"Window corner preference not applied: set={setResult:X8}, get={getResult:X8}, value={applied}");
 }
 void OpenAuthor(object sender,System.Windows.Navigation.RequestNavigateEventArgs e){
  e.Handled=true;
  try{System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://t.me/noirlie"){UseShellExecute=true});}
  catch(Exception ex){AddLog("Не удалось открыть Telegram: "+ex.Message);}
 }
 void MinimizeWindow(object sender,RoutedEventArgs e)=>SystemCommands.MinimizeWindow(this);
 void MaximizeWindow(object sender,RoutedEventArgs e) {
  if(WindowState==WindowState.Maximized)SystemCommands.RestoreWindow(this);
  else SystemCommands.MaximizeWindow(this);
 }
 void CloseWindow(object sender,RoutedEventArgs e)=>Close();
}
