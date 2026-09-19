using System.Diagnostics;
using System.IO;
using System.Security.Principal;
namespace ZapretDesktop;
public sealed class EngineController(Action<string> log,Action<Process>? started=null) : IEngine {
 Process? owned;
 public bool Running { get {try{return owned is {HasExited:false};}catch(InvalidOperationException){return false;}} }
 public static bool IsAdmin => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
 public async Task StartAsync(Strategy strategy,CancellationToken token) {
  token.ThrowIfCancellationRequested();
  if(!IsAdmin)throw new UnauthorizedAccessException("Для запуска движка откройте Настройки → Перезапустить с правами администратора.");
  await StopAsync();
  var others=Process.GetProcessesByName("winws");
  bool conflict=others.Length>0;foreach(var p in others)p.Dispose();
  if(conflict)throw new UnauthorizedAccessException("Уже работает другой winws. Остановите его вручную; чужой процесс не изменён.");
  var fresh=StrategyImporter.Read(Path.Combine(strategy.Root,strategy.Name+".bat"));
  var info=new ProcessStartInfo(Path.Combine(fresh.Root,"bin","winws.exe")){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=Path.Combine(fresh.Root,"bin"),RedirectStandardOutput=true,RedirectStandardError=true};
  foreach(var arg in fresh.Arguments)info.ArgumentList.Add(arg);
  var process=new Process{StartInfo=info};
  process.OutputDataReceived+=(_,e)=>{if(e.Data is string s)log(s);};
  process.ErrorDataReceived+=(_,e)=>{if(e.Data is string s)log(s);};
  try {
   if(!process.Start())throw new IOException("Не удалось создать процесс.");
   owned=process;started?.Invoke(process);process.BeginOutputReadLine();process.BeginErrorReadLine();
   await Task.Delay(750,token);
   if(process.HasExited)throw new IOException("winws завершился с кодом "+process.ExitCode);
   log("Запущена "+fresh.Name);
  } catch {if(owned==process)await StopAsync();else process.Dispose();throw;}
 }
 public async Task StopAsync() {
  if(owned is not { } p)return;
  if(!p.HasExited) {
   p.Kill(true);
   using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(5));
   await p.WaitForExitAsync(timeout.Token);
  }
  owned=null;p.Dispose();log("Собственный движок остановлен.");
 }
}

