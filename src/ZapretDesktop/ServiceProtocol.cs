using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
namespace ZapretDesktop;
public static class ServicePaths {
 public const string Name="ZapretDesktopService";
 public const string Pipe="ZapretDesktop.Service.v1";
 public static string Root=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"ZapretDesktopService");
 public static string Components=>Path.Combine(Root,"components");
}
public record ServiceRequest(string Command,bool Automatic=true,string? Strategy=null,string? Preferred=null);
public record ServiceSnapshot(bool Running,bool Busy,string Message,string? Strategy=null,ProbeResult? Probe=null,bool Verified=false,string? Error=null,string? Version=null,bool RecoveryPending=false);
public static class ServiceWire {
 public static async Task<T> Read<T>(Stream stream,CancellationToken token) {
  using var bytes=new MemoryStream();var single=new byte[1];
  while(bytes.Length<32768) {
   if(await stream.ReadAsync(single,token)==0)throw new EndOfStreamException();
   if(single[0]==10)return JsonSerializer.Deserialize<T>(bytes.ToArray())??throw new InvalidDataException("Empty message");
   bytes.WriteByte(single[0]);
  }
  throw new InvalidDataException("Message too large");
 }
 public static async Task Write<T>(Stream stream,T value,CancellationToken token){var bytes=Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value)+"\n");if(bytes.Length>32768)throw new InvalidDataException("Message too large");await stream.WriteAsync(bytes,token);await stream.FlushAsync(token);}
}
public sealed class ServiceClient(string pipeName=ServicePaths.Pipe) {
 public ServiceSnapshot State {get;private set;}=new(false,false,"Служба не подключена");
 public bool Running=>State.Running;
 public async Task<ServiceSnapshot> Send(ServiceRequest request,CancellationToken token=default,int timeoutSeconds=5) {
  using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
  using var pipe=new NamedPipeClientStream(".",pipeName,PipeDirection.InOut,PipeOptions.Asynchronous);
  await pipe.ConnectAsync(timeout.Token);
  await ServiceWire.Write(pipe,request,timeout.Token);
  State=await ServiceWire.Read<ServiceSnapshot>(pipe,timeout.Token);
  return State;
 }
 public async Task StopAsync(){var state=await Send(new("stop"),timeoutSeconds:15);if(state.Error is not null)throw new IOException(state.Error);if(state.Running||state.Busy||state.RecoveryPending)throw new IOException("Служба не подтвердила отключение.");}
}



