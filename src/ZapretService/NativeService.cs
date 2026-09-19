using System.ComponentModel;
using System.Runtime.InteropServices;
namespace ZapretDesktop;
static class NativeService {
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct Entry {public string? Name;public MainCallback? Main;}
 [StructLayout(LayoutKind.Sequential)] struct Status {public uint Type,State,Controls,Exit,SpecificExit,Checkpoint,Hint;}
 [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void MainCallback(uint count,IntPtr args);
 [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate uint Handler(uint control,uint type,IntPtr data,IntPtr context);
 [DllImport("advapi32",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool StartServiceCtrlDispatcher(Entry[] table);
 [DllImport("advapi32",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr RegisterServiceCtrlHandlerEx(string name,Handler handler,IntPtr context);
 [DllImport("advapi32",SetLastError=true)] static extern bool SetServiceStatus(IntPtr handle,ref Status status);
 static MainCallback? main;static Handler? handler;static IntPtr handle;static CancellationTokenSource stop=new();
 public static Action? Resumed;
 static void Report(uint state,uint exit=0){var s=new Status{Type=0x10,State=state,Controls=state==4?69u:0,Exit=exit,Hint=state is 2 or 3?15000u:0,Checkpoint=state is 2 or 3?1u:0};SetServiceStatus(handle,ref s);}
 public static void Run(Func<CancellationToken,Task> work){
  handler=(control,type,_,_)=>{if(control is 1 or 5){Report(3);stop.Cancel();}else if(control==13&&type is 7 or 18)Resumed?.Invoke();return 0;};
  main=(_,_)=>{handle=RegisterServiceCtrlHandlerEx(ServicePaths.Name,handler,IntPtr.Zero);if(handle==IntPtr.Zero)return;Report(2);try{Report(4);work(stop.Token).GetAwaiter().GetResult();Report(1);}catch{Report(1,1);}};
  if(!StartServiceCtrlDispatcher([new Entry{Name=ServicePaths.Name,Main=main},new Entry()]))throw new Win32Exception(Marshal.GetLastWin32Error());
 }
}

