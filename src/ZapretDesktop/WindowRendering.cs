using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
namespace ZapretDesktop;

public static class WindowRendering {
 public static void Initialize(){
  // This small UI does not need a GPU. Avoid driver/device-reset dependent surfaces.
  RenderOptions.ProcessRenderMode=System.Windows.Interop.RenderMode.SoftwareOnly;
  Log("Application startup; software rendering");
 }
 public static void Attach(Window window){
  window.SourceInitialized+=(_,_)=>{
   var source=HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
   if(source?.CompositionTarget is { } target){target.BackgroundColor=Colors.White;target.RenderMode=System.Windows.Interop.RenderMode.SoftwareOnly;}
   Log("Window source initialized");
  };
  window.ContentRendered+=(_,_)=>Log("Window content rendered");
 }
 public static void Log(string message){
  try{var folder=SettingsStore.DataDirectory;Directory.CreateDirectory(folder);var file=Path.Combine(folder,"startup.log");if(File.Exists(file)&&new FileInfo(file).Length>128000)File.Move(file,file+".previous",true);File.AppendAllText(file,$"{DateTimeOffset.Now:O} [{Environment.ProcessId}] {message}\n");}catch(IOException){}catch(UnauthorizedAccessException){}
 }
}
