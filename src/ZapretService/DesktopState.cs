using System.Text.Json;
namespace ZapretDesktop;

sealed record BootConfiguration(bool Enabled=false,bool Automatic=true,string? Strategy=null,string? LastWorking=null,Dictionary<string,string>? Working=null);
sealed partial class ServiceHost {
 string BootFile=>root.TrimEnd(Path.DirectorySeparatorChar)+".desktop.json";
 BootConfiguration? boot;
 bool bootLoaded;
 void LoadBoot(){
  if(bootLoaded)return;bootLoaded=true;
  try{if(File.Exists(BootFile))boot=JsonSerializer.Deserialize<BootConfiguration>(File.ReadAllText(BootFile));}
  catch(Exception ex)when(ex is IOException or JsonException or UnauthorizedAccessException){Set(Snapshot with{Message="Настройки автоподключения повреждены. Откройте настройки приложения."});}
 }
 static void WriteAtomic(string path,string value){
  if(File.Exists(path)&&(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("Недопустимая ссылка вместо настроек");
  string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
  try{using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){using var writer=new StreamWriter(stream);writer.Write(value);writer.Flush();stream.Flush(true);}File.Move(temp,path,true);}
  finally{if(File.Exists(temp))File.Delete(temp);}
 }
 void SaveBoot(BootConfiguration value){lock(sync){WriteAtomic(BootFile,JsonSerializer.Serialize(value));boot=value;}}
 void RememberStrategy(string? strategy){lock(sync){
  if(strategy is null||boot is null)return;
  try{var working=new Dictionary<string,string>(boot.Working??new());working[NetworkKey()]=strategy;while(working.Count>32)working.Remove(working.Keys.First());SaveBoot(boot with{LastWorking=strategy,Working=working});}
  catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){Set(Snapshot with{Message="Подключение работает; не удалось сохранить режим автозапуска."});}
 }
 }
 public async Task StartAtBoot(CancellationToken token){
  await commands.WaitAsync(token);
  try{
   LoadBoot();
   if(boot?.Enabled!=true||engine.Running||Snapshot.Busy)return;
   var candidates=Directory.GetFiles(root,"general*.bat").Select(f=>{try{return StrategyImporter.Read(f);}catch{return null;}}).Where(s=>s is not null).Cast<Strategy>().ToList();
   var name=boot.Automatic?(boot.Working?.GetValueOrDefault(NetworkKey())??boot.LastWorking):boot.Strategy;
   var chosen=candidates.FirstOrDefault(s=>s.Name==name);
   if(chosen is null){await HandleCore(new("start",boot.Automatic,boot.Strategy,boot.LastWorking),token);return;}
   generation++;desired=true;lastRequest=new("start",boot.Automatic,boot.Strategy,chosen.Name);recovery.Arm(NetworkKey(),DateTimeOffset.UtcNow);
   await engine.StartAsync(chosen,token);
   if(!engine.Running)throw new IOException("Движок завершился при автозапуске");
   Set(new(true,false,"Подключение запущено службой. Проверка выполняется в фоне.",chosen.Name));
  }catch(OperationCanceledException)when(token.IsCancellationRequested){}
  catch(Exception ex){Set(new(engine.Running,false,"Автоподключение ожидает восстановления",Error:ex.Message));}
  finally{commands.Release();}
 }
}
