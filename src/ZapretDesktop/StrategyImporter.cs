using System.IO;
using System.Text.RegularExpressions;
namespace ZapretDesktop;
public record Strategy(string Name, string Root, string[] Arguments)
{
    public override string ToString() => Name;
}
public static class StrategyImporter
{
    static readonly HashSet<string> Allowed = new(("dpi-desync dpi-desync-any-protocol dpi-desync-badseq-increment dpi-desync-cutoff dpi-desync-fake-discord dpi-desync-fake-http dpi-desync-fake-quic dpi-desync-fake-stun dpi-desync-fake-tls dpi-desync-fake-tls-mod dpi-desync-fake-unknown dpi-desync-fake-unknown-udp dpi-desync-fakedsplit-pattern dpi-desync-fooling dpi-desync-hostfakesplit-mod dpi-desync-repeats dpi-desync-split-pos dpi-desync-split-seqovl dpi-desync-split-seqovl-pattern filter-l3 filter-l7 filter-tcp filter-udp hostlist hostlist-domains hostlist-exclude hostlist-exclude-domains ip-id ipset ipset-exclude new wf-tcp wf-udp").Split(' '));
    public static Strategy Read(string file)
    {
        var root = Path.GetDirectoryName(Path.GetFullPath(file))!;
        var text = Regex.Replace(File.ReadAllText(file), @"\^\s*\r?\n", " ");
        var lines = text.Split('\n').Where(x => x.TrimStart().StartsWith("start ", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (lines.Length != 1) throw new InvalidDataException("Ожидалась одна команда start.");
        const string marker = "\"%BIN%winws.exe\"";
        var pos = lines[0].IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (pos < 0) throw new InvalidDataException("Неподдерживаемая команда запуска.");
        var args = lines[0][(pos + marker.Length)..].Trim().Replace("--dpi-desync-fake-tls=^!", "--dpi-desync-fake-tls=!");
        args = args.Replace("%BIN%", Path.Combine(root, "bin") + Path.DirectorySeparatorChar)
                   .Replace("%LISTS%", Path.Combine(root, "lists") + Path.DirectorySeparatorChar)
                   .Replace("%GameFilterTCP%", "12").Replace("%GameFilterUDP%", "12");
        if (args.IndexOfAny(['%', '^', '&', '|', '<', '>', '\r', '\n']) >= 0) throw new InvalidDataException("Неизвестная конструкция в стратегии.");
        if (args.Count(c => c == '"') % 2 != 0) throw new InvalidDataException("Незакрытая кавычка.");
        var tokens = Regex.Matches(args, "(?:[^\\s\"]+|\"[^\"]*\")+").Select(x => x.Value.Replace("\"", "")).ToArray();
        if(tokens.Length == 0) throw new InvalidDataException("Пустая стратегия.");
        foreach (var token in tokens)
        {
            var key = token.Split('=')[0];
            if (!key.StartsWith("--") || !Allowed.Contains(key[2..])) throw new InvalidDataException("Неподдерживаемый параметр: " + key);
            var value = token.Contains('=') ? token[(token.IndexOf('=')+1)..] : "";
            if (value.Contains(':') || value.Contains('\\') || value.Contains('/'))
            {
                var full = Path.GetFullPath(value);
                if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
                    throw new InvalidDataException("Отсутствующий или внешний файл: " + value);
            }
        }
        if (!File.Exists(Path.Combine(root, "bin", "winws.exe"))) throw new FileNotFoundException("Не найден winws.exe.");
        return new(Path.GetFileNameWithoutExtension(file), root, tokens);
    }
}



