using System.IO;
using System.Globalization;
namespace ZapretDesktop;
public static class DomainValidation {
 public static string Normalize(string input){
  if(input is null)throw new IOException("Список доменов не задан.");
  if(input.Length>6000)throw new IOException("Список слишком длинный: максимум 6000 символов.");
  var result=new List<string>();var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var idn=new IdnMapping();int line=0;
  foreach(var raw in input.Replace("\r","").Split('\n')){
   line++;var value=raw.Trim();if(value.Length==0||value.StartsWith('#'))continue;
   try{value=idn.GetAscii(value.TrimEnd('.')).ToLowerInvariant();}catch(ArgumentException){throw new IOException($"Строка {line}: некорректное имя домена.");}
   if(value.Length>253||!value.Contains('.')||System.Net.IPAddress.TryParse(value,out _)||value.Split('.').Any(label=>label.Length is 0 or >63||label[0]=='-'||label[^1]=='-'||label.Any(c=>!(char.IsAsciiLetterOrDigit(c)||c=='-'))))throw new IOException($"Строка {line}: укажите домен без https://, пути, порта и звёздочек.");
   if(seen.Add(value))result.Add(value);
  }
  var text=string.Join("\n",result);if(text.Length>6000)throw new IOException("Список после преобразования слишком длинный.");return text.Length==0?"":text+"\n";
 }
}
