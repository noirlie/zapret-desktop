using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
namespace ZapretDesktop;
sealed class EngineJob:IDisposable {
 [StructLayout(LayoutKind.Sequential)] struct Basic {public long ProcessTime,JobTime;public uint Flags;public UIntPtr MinWorking,MaxWorking;public uint Active;public UIntPtr Affinity;public uint Priority,Scheduling;}
 [StructLayout(LayoutKind.Sequential)] struct Limits {public Basic Basic;public ulong ReadOps,WriteOps,OtherOps,ReadBytes,WriteBytes,OtherBytes;public UIntPtr ProcessMemory,JobMemory,PeakProcess,PeakJob;}
 [DllImport("kernel32.dll",SetLastError=true)]static extern IntPtr CreateJobObject(IntPtr attributes,string? name);
 [DllImport("kernel32.dll",SetLastError=true)]static extern bool SetInformationJobObject(IntPtr job,int info,ref Limits limits,uint size);
 [DllImport("kernel32.dll",SetLastError=true)]static extern bool AssignProcessToJobObject(IntPtr job,IntPtr process);
 [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
 IntPtr job;
 public EngineJob(){job=CreateJobObject(IntPtr.Zero,null);if(job==IntPtr.Zero)throw new Win32Exception();var limits=new Limits{Basic=new Basic{Flags=0x2000}};if(!SetInformationJobObject(job,9,ref limits,(uint)Marshal.SizeOf<Limits>())){Dispose();throw new Win32Exception();}}
 public void Attach(Process process){if(!AssignProcessToJobObject(job,process.Handle))throw new Win32Exception(Marshal.GetLastWin32Error());}
 public void Dispose(){if(job!=IntPtr.Zero){CloseHandle(job);job=IntPtr.Zero;}}
}

