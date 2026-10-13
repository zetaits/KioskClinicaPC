using System.IO;
using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;

namespace Kiosk.SetupHelper;

/// <summary>WinGet's API doesn't expose InstallModes. Validate the official manifest, not guessed EXE flags.</summary>
internal static class ManifestPolicy
{
    public static string RelativeDirectory(string id, string version)
    {
        if (!Regex.IsMatch(id, "^[A-Za-z0-9][A-Za-z0-9._+-]{1,199}$") || version is "." or ".." || version.IndexOfAny(['/', '\\', '\r', '\n']) >= 0)
            throw new InvalidDataException("Identificador o versión no válido.");
        return "manifests/" + char.ToLowerInvariant(id[0]) + "/" + id.Replace('.', '/') + "/" + version;
    }
    public static bool SupportsSilentMachine(string yaml, string? architecture = null, string? installerType = null)
    {
        var stream = new YamlStream(); stream.Load(new StringReader(yaml));
        if (stream.Documents.Count != 1 || stream.Documents[0].RootNode is not YamlMappingNode root) return false;
        var installers = Node(root, "Installers") as YamlSequenceNode;
        IEnumerable<YamlMappingNode> candidates = installers == null ? [root] : installers.Children.OfType<YamlMappingNode>();
        var applicable = candidates.Where(installer =>
        {
            string Value(string name) => ((Node(installer, name) ?? Node(root, name)) as YamlScalarNode)?.Value ?? "";
            string type = Value("NestedInstallerType"); if (type == "") type = Value("InstallerType");
            return Value("Scope") == "machine" &&
                (architecture == null || Value("Architecture").Equals(architecture, StringComparison.OrdinalIgnoreCase)) &&
                (installerType == null || type.Equals(installerType, StringComparison.OrdinalIgnoreCase));
        }).ToList();
        return applicable.Count > 0 && applicable.All(installer =>
        {
            YamlNode? Field(string name) => Node(installer, name) ?? Node(root, name);
            string Value(string name) => (Field(name) as YamlScalarNode)?.Value ?? "";
            string type = Value("NestedInstallerType"); if (type == "") type = Value("InstallerType");
            if (Value("Scope") != "machine" || Field("Authentication") != null ||
                type is "msstore" or "msix" or "appx" or "portable" ||
                (architecture != null && !Value("Architecture").Equals(architecture, StringComparison.OrdinalIgnoreCase)) ||
                (installerType != null && !type.Equals(installerType, StringComparison.OrdinalIgnoreCase))) return false;
            if (Field("InstallModes") is YamlSequenceNode modes)
                return modes.Children.OfType<YamlScalarNode>().Any(x => x.Value == "silent");
            // Documented WinGet technology defaults, not per-application patches.
            if (type is "msi" or "wix" or "inno" or "nullsoft" or "burn") return true;
            return Field("InstallerSwitches") is YamlMappingNode switches && Node(switches, "Silent") is YamlScalarNode { Value.Length: > 0 };
        });
    }
    private static YamlNode? Node(YamlMappingNode map, string key) => map.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value : null;
    public static bool ValidateLocal(string repository, string id, string version)
    {
        string directory = Path.Combine(repository, RelativeDirectory(id, version).Replace('/', Path.DirectorySeparatorChar));
        foreach (string name in new[] { id + ".installer.yaml", id + ".yaml" })
        {
            string path = Path.Combine(directory, name);
            if (File.Exists(path)) return SupportsSilentMachine(File.ReadAllText(path));
        }
        return false;
    }
}
