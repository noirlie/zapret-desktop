using System.Net.Http;
using System.Text.Json;
namespace ZapretDesktop;
public record ProbeResult(bool YouTube, bool Discord, string YouTubeDetail, string DiscordDetail) {
 public bool Passed=>YouTube&&Discord;
 public bool HasDnsFailure=>YouTubeDetail.StartsWith("DNS:",StringComparison.Ordinal)||DiscordDetail.StartsWith("DNS:",StringComparison.Ordinal);
}
public interface IProbe { Task<ProbeResult> CheckAsync(CancellationToken token); }
public sealed class ProbeFailureException(string message,ProbeResult result):System.IO.IOException(message) { public ProbeResult Result {get;}=result; }
public interface IEngine {
 bool Running {get;}
 Task StartAsync(Strategy strategy,CancellationToken token);
 Task StopAsync();
}
public sealed record SelectionResult(Strategy? Strategy,ProbeResult Probe,bool AlreadyAvailable,bool Verified=true);
public sealed class AutoSelector(IEngine engine,IProbe probe) {
 public async Task<SelectionResult> SelectAsync(IReadOnlyList<Strategy> strategies,string? cached,bool automatic,Action<string> report,CancellationToken token) {
  try {
   token.ThrowIfCancellationRequested();
   if(!automatic) {
    var chosen=strategies.FirstOrDefault()??throw new System.IO.IOException("Не выбрана стратегия.");
    report("Запускаем выбранный режим без предварительной сетевой проверки: "+chosen.Name);
    await engine.StopAsync();
    await engine.StartAsync(chosen,token);
    if(!engine.Running)throw new System.IO.IOException("Движок завершился после запуска.");
    token.ThrowIfCancellationRequested();
    return new(chosen,new(false,false,"Не проверен","Не проверен"),false,false);
   }
   report("Проверяем доступность без запуска движка…");
   var baseline=await probe.CheckAsync(token);
   report("Без движка: YouTube — "+baseline.YouTubeDetail+"; Discord — "+baseline.DiscordDetail);
   if(automatic && baseline.HasDnsFailure) throw new ProbeFailureException("Автоподбор остановлен: ошибка системного DNS. Подробности — в карточках сервисов. Это не означает, что стратегии не работают. Для проверки в браузере включите ручной режим.",baseline);
   var candidates=automatic?strategies.OrderBy(s=>s.Name==cached?0:1).ToArray():strategies.Take(1).ToArray();
   ProbeResult last=baseline;
   for(int i=0;i<candidates.Length;i++) {
    token.ThrowIfCancellationRequested();
    await engine.StopAsync();
    report($"Проверяем {i+1} из {candidates.Length}: {candidates[i].Name}");
    try {await engine.StartAsync(candidates[i],token);}
    catch(System.ComponentModel.Win32Exception) {throw;}
    catch(UnauthorizedAccessException) {throw;}
    catch(System.IO.IOException ex) {report("Стратегия не запустилась: "+ex.Message);continue;}
    last=await probe.CheckAsync(token);
    if(!last.Passed&&!last.HasDnsFailure&&engine.Running) {
     report("Повторяем проверку после временного сбоя: "+candidates[i].Name);
     await Task.Delay(250,token);
     last=await probe.CheckAsync(token);
    }
    report(candidates[i].Name+": YouTube — "+last.YouTubeDetail+"; Discord — "+last.DiscordDetail);
    if(!automatic && engine.Running) return new(candidates[i],last,false,false);
    if(last.HasDnsFailure)throw new ProbeFailureException("Проверка прервана: ошибка DNS, а не подтверждённый отказ стратегии.",last);
    if(last.Passed && engine.Running) {
     var confirmation=await probe.CheckAsync(token);
     if(confirmation.Passed && engine.Running) {token.ThrowIfCancellationRequested();return new(candidates[i],confirmation,false);}
     last=confirmation;
    }
   }
   await engine.StopAsync();
   throw new ProbeFailureException("Автопроверка не подтвердила рабочий режим. Можно запустить стратегию вручную и проверить в браузере.",last);
  } catch {await engine.StopAsync();throw;}
 }
}

