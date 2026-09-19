using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
namespace ZapretDesktop;

// The original (non-elevated) process never writes into Setup's protected temp folder.
// Windows supplies its identity through a local named pipe, not through client-provided text.
public static class SetupIdentity {
 [DllImport("kernel32.dll",SetLastError=true)]
 static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe,out uint processId);
 public static async Task Capture(string name,string output){
  using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(25));
  var acl=new PipeSecurity();acl.SetAccessRuleProtection(true,false);
  acl.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid,null),PipeAccessRights.FullControl,AccessControlType.Deny));
  acl.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid,null),PipeAccessRights.ReadWrite,AccessControlType.Allow));
  acl.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User!,PipeAccessRights.FullControl,AccessControlType.Allow));
  using var pipe=NamedPipeServerStreamAcl.Create(name,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous,128,128,acl);
  await pipe.WaitForConnectionAsync(timeout.Token);
  var hello=new byte[1];
  if(await pipe.ReadAsync(hello,timeout.Token)!=1||hello[0]!=1)throw new IOException("Некорректный запрос установщика");
  if(!GetNamedPipeClientProcessId(pipe.SafePipeHandle,out var pid))throw new IOException("Не удалось проверить процесс пользователя");
  using var client=Process.GetProcessById((int)pid);
  using var current=Process.GetCurrentProcess();
  if(client.SessionId!=current.SessionId||!string.Equals(client.MainModule?.FileName,Environment.ProcessPath,StringComparison.OrdinalIgnoreCase))throw new UnauthorizedAccessException("Посторонний процесс вместо помощника установщика");
  SecurityIdentifier? owner=null;
  pipe.RunAsClient(()=>{using var identity=WindowsIdentity.GetCurrent(true);owner=identity?.User;});
  if(owner is null||!owner.IsAccountSid())throw new IOException("Не удалось определить пользователя");
  // Only the elevated server writes the file. The directory ACL remains untouched.
  await File.WriteAllTextAsync(output,owner.Value,timeout.Token);
  await pipe.WriteAsync(new byte[]{1},timeout.Token);
 }
 public static async Task Identify(string name){
  using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(20));
  using var pipe=new NamedPipeClientStream(".",name,PipeDirection.InOut,PipeOptions.Asynchronous,TokenImpersonationLevel.Identification);
  await pipe.ConnectAsync(timeout.Token);
  await pipe.WriteAsync(new byte[]{1},timeout.Token);
  var ack=new byte[1];
  if(await pipe.ReadAsync(ack,timeout.Token)!=1||ack[0]!=1)throw new IOException("Установщик не подтвердил пользователя");
 }
}
