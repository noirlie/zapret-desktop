namespace ZapretDesktop;
public record RecoveryDecision(string Kind,string Message="");
public sealed class RecoveryPolicy {
 bool enabled,pending;
 string network="",reason="";
 int failures;
 DateTimeOffset due,stableSince;
 readonly Queue<DateTimeOffset> attempts=new();
 public void Arm(string key,DateTimeOffset now){enabled=true;network=key;pending=false;failures=0;attempts.Clear();due=now;stableSince=now;}
 public void Disarm(){enabled=false;pending=false;attempts.Clear();}
 public RecoveryDecision Observe(DateTimeOffset now,bool online,string key,bool running,bool busy,bool? healthy=null,bool resumed=false){
  if(!enabled||busy)return new("none");
  if(!online){stableSince=now;Queue(now,"Ожидаем появления сети");return new("wait","Сеть недоступна. Восстановление начнётся после её появления.");}
  if(key!=network){network=key;pending=false;Queue(now,"Сеть изменилась");}
  if(resumed)Queue(now,"Возобновление работы после паузы");
  if(!running)Queue(now,"Движок завершился");
  if(healthy==false){failures++;stableSince=now;if(failures>=2)Queue(now,"Две проверки соединения завершились неудачно");}
  else if(healthy==true)failures=0;
  if(running&&!pending&&healthy!=false&&now-stableSince>=TimeSpan.FromMinutes(5))attempts.Clear();
  if(!pending)return new("none");
  if(now<due)return new("wait","Ожидаем стабилизации сети перед восстановлением.");
  while(attempts.Count>0&&now-attempts.Peek()>=TimeSpan.FromMinutes(10))attempts.Dequeue();
  if(attempts.Count>=3)return new("limited","Автовосстановление приостановлено: 3 попытки за 10 минут. Можно подключиться вручную.");
  attempts.Enqueue(now);pending=false;failures=0;stableSince=now;
  due=now+TimeSpan.FromSeconds(attempts.Count switch{1=>20,2=>60,_=>120});
  return new("recover",reason);
 }
 void Queue(DateTimeOffset now,string message){if(pending)return;pending=true;reason=message;stableSince=now;if(due<now+TimeSpan.FromSeconds(5))due=now+TimeSpan.FromSeconds(5);}
}

