using System.Security.Principal;
namespace ZapretDesktop;
public sealed class SingleInstance:IDisposable {
 readonly Mutex mutex;
 readonly EventWaitHandle wake;
 readonly bool owns;
 RegisteredWaitHandle? registration;
 public bool IsPrimary=>owns;
 public SingleInstance(string? testName=null){
  var name=testName??"Local\\ZapretDesktop."+WindowsIdentity.GetCurrent().User!.Value;
  mutex=new Mutex(true,name+".mutex",out owns);
  wake=new EventWaitHandle(false,EventResetMode.AutoReset,name+".wake");
 }
 public void NotifyPrimary()=>wake.Set();
 public void Listen(Action callback){if(!owns)throw new InvalidOperationException();registration=ThreadPool.RegisterWaitForSingleObject(wake,(_,_)=>callback(),null,Timeout.Infinite,false);}
 public void Dispose(){registration?.Unregister(null);wake.Dispose();if(owns)mutex.ReleaseMutex();mutex.Dispose();}
}

