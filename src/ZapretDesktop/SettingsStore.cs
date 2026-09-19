using System.IO;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace ZapretDesktop;
public sealed class UserSettings {
 public string Folder {get;set;}="";
 public bool Automatic {get;set;}=true;
 public bool StartHidden {get;set;}=true;
 public bool AutoConnect {get;set;}=false;
 public string SelectedStrategy {get;set;}="general";
 public Dictionary<string,string> Working {get;set;}=new();
 public Dictionary<string,string> ManualCandidates {get;set;}=new();
}
public sealed class SettingsStore {
 public static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ZapretDesktop.Alpha");
 readonly string directory;
 public SettingsStore(string? path=null) => directory=path??(AppContext.TryGetSwitch("ZapretDesktop.TestMode",out var testing)&&testing?Path.Combine(Path.GetTempPath(),"ZapretDesktop.UiTests"):DataDirectory);
 public UserSettings Load() {
  try { return JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(Path.Combine(directory,"preferences.json")))??new(); }
  catch(IOException) { return new(); } catch(JsonException) { return new(); }
 }
 public void Save(UserSettings settings) {
  Directory.CreateDirectory(directory);
  var path=Path.Combine(directory,"preferences.json");
  File.WriteAllText(path+".tmp",JsonSerializer.Serialize(settings,new JsonSerializerOptions{WriteIndented=true}));
  File.Move(path+".tmp",path,true);
 }
 public static string NetworkKey() {
  var data=string.Join("|",NetworkInterface.GetAllNetworkInterfaces().Where(n=>n.OperationalStatus==OperationalStatus.Up && n.NetworkInterfaceType!=NetworkInterfaceType.Loopback)
   .OrderBy(n=>n.Id).Select(n=>n.Id+":"+string.Join(",",n.GetIPProperties().GatewayAddresses.Select(a=>a.Address.ToString()))));
  if(OperatingSystem.IsWindows()) {
   object? manager=null;
   try {
    var type=Type.GetTypeFromCLSID(new Guid("DCB00C01-570F-4A9B-8D69-199FDBA5723B"));
    manager=type is null?null:Activator.CreateInstance(type);
    if(manager is not null) {
     var ids=new List<string>();
     foreach(var item in ((dynamic)manager).GetNetworks(1)) {
      try{ids.Add((string)item.GetName()+"|"+(string)item.GetDescription());}
      finally{if(System.Runtime.InteropServices.Marshal.IsComObject(item))System.Runtime.InteropServices.Marshal.ReleaseComObject(item);}
     }
     data+="|profiles:"+string.Join(",",ids.Order());
    }
   }catch(System.Runtime.InteropServices.COMException){}catch(ArgumentException){}catch(Microsoft.CSharp.RuntimeBinder.RuntimeBinderException){}
   finally{if(manager is not null&&System.Runtime.InteropServices.Marshal.IsComObject(manager))System.Runtime.InteropServices.Marshal.ReleaseComObject(manager);}
  }
  return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(data)));
 }
}

