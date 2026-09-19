using ZapretDesktop;
var folder=Path.GetFullPath("installer/payload/components");
int ok=0, bad=0;
foreach(var f in Directory.GetFiles(folder,"general*.bat")) {
try { var s=StrategyImporter.Read(f); Console.WriteLine($"PASS {s.Name}: {s.Arguments.Length} arguments"); ok++; }
catch(Exception e) { Console.WriteLine($"REJECT {Path.GetFileName(f)}: {e.Message}"); bad++; }
}
Console.WriteLine($"Imported {ok}, rejected {bad}");
if(ok==0) Environment.Exit(1);
var temp=Path.Combine(AppContext.BaseDirectory,"fixtures");
Directory.CreateDirectory(temp);
foreach(var input in new[]{"start \"x\" \"%BIN%winws.exe\" --lua=evil","start \"x\" \"%BIN%winws.exe\" --new & calc","start \"x\" \"%BIN%winws.exe\" --hostlist=\"C:\\outside.txt\"","start \"x\" \"%BIN%winws.exe\" --hostlist=\"broken"}) {
var f=Path.Combine(temp,"general.bat"); File.WriteAllText(f,input);
try { StrategyImporter.Read(f); throw new Exception("Accepted invalid fixture"); } catch(InvalidDataException) { Console.WriteLine("PASS rejected unsupported input"); }
}
