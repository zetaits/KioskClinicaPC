using System.IO;
using System.Text;
namespace Kiosk.SetupHelper;
internal sealed class Ini
{
    private readonly Dictionary<string, Dictionary<string, string>> _sections = new(StringComparer.OrdinalIgnoreCase);
    public string Get(string section, string key) => _sections.TryGetValue(section, out var values) && values.TryGetValue(key, out var value) ? value : "";
    public int GetInt(string section, string key) => int.TryParse(Get(section, key), out int value) ? value : 0;
    public static Ini Read(string path)
    {
        var ini = new Ini(); string section = "";
        foreach (var raw in File.ReadAllLines(path))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';')) continue;
            if (line.StartsWith('[') && line.EndsWith(']')) { section = line[1..^1]; ini._sections.TryAdd(section, new(StringComparer.OrdinalIgnoreCase)); continue; }
            int equals = line.IndexOf('=');
            if (equals > 0 && ini._sections.TryGetValue(section, out var values)) values[line[..equals].Trim()] = line[(equals + 1)..].Trim();
        }
        return ini;
    }
    public static void Write(string path, Dictionary<string, Dictionary<string, string>> sections)
    {
        using var writer = new StreamWriter(path, false, Encoding.Unicode);
        foreach (var section in sections) { writer.WriteLine($"[{section.Key}]"); foreach (var pair in section.Value) writer.WriteLine($"{pair.Key}={pair.Value.Replace("\r", " ").Replace("\n", "\\n")}"); writer.WriteLine(); }
    }
}
