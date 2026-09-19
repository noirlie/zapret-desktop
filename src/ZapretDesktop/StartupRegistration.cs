using Microsoft.Win32;
namespace ZapretDesktop;
public static class StartupRegistration {
 const string Key=@"Software\Microsoft\Windows\CurrentVersion\Run";
 const string Name="ZapretDesktop";
 public static bool Enabled {get{using var key=Registry.CurrentUser.OpenSubKey(Key);return key?.GetValue(Name) is string;}}
 public static string Command(string executable){if(!System.IO.Path.IsPathFullyQualified(executable)||executable.Contains('"'))throw new ArgumentException("Некорректный путь приложения");return "\""+executable+"\" --startup";}
 public static void Set(bool enabled,string? executable=null){using var key=Registry.CurrentUser.CreateSubKey(Key);if(enabled)key.SetValue(Name,Command(executable??Environment.ProcessPath!),RegistryValueKind.String);else key.DeleteValue(Name,false);}
}

