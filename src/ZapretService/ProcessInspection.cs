using System.ComponentModel;
using System.Diagnostics;
namespace ZapretDesktop;
public static class ProcessInspection {
 public static bool IsRunningAt(Process process,string executable){
  try{
   if(process.HasExited)return false;
   return string.Equals(process.MainModule?.FileName,executable,StringComparison.OrdinalIgnoreCase);
  }catch(InvalidOperationException){return false;}
  catch(Win32Exception ex){
   // Access denied for a live process must not be mistaken for successful shutdown.
   try{if(process.HasExited)return false;}catch(InvalidOperationException){return false;}
   throw new IOException("Не удалось проверить остановку службы: "+ex.Message,ex);
  }
 }
}

