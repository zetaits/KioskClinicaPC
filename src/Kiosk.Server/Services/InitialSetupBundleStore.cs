using System.Security.Cryptography;
using Newtonsoft.Json;

namespace Kiosk.Server.Services;

public sealed class InitialSetupBundleManifest
{
    public string Version { get; set; } = "";
    public string FileName { get; set; } = "";
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = "";
    public string ServerUrl { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
}

public sealed record ValidatedInitialSetupBundle(InitialSetupBundleManifest Manifest, string FullPath);

/// <summary>Descubre artefactos copiados por despliegue y solo expone el mas reciente si supera integridad.</summary>
public sealed class InitialSetupBundleStore
{
    private readonly string _root;

    public InitialSetupBundleStore(string root)
    {
        _root = Path.GetFullPath(root);
        Directory.CreateDirectory(_root);
    }

    public string Root => _root;

    public ValidatedInitialSetupBundle? Latest(out string? error)
    {
        error = null;
        var candidates = new List<(Version Version, InitialSetupBundleManifest Manifest, string Path)>();
        foreach (string manifestPath in Directory.EnumerateFiles(_root, "*.bundle.json", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var manifest = JsonConvert.DeserializeObject<InitialSetupBundleManifest>(File.ReadAllText(manifestPath))
                    ?? throw new InvalidDataException("Manifiesto vac\u00edo.");
                if (!Version.TryParse(manifest.Version, out Version? version))
                    throw new InvalidDataException("Versi\u00f3n no v\u00e1lida.");
                string fileName = Path.GetFileName(manifest.FileName);
                if (!fileName.Equals(manifest.FileName, StringComparison.Ordinal) || !fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Nombre de Setup no v\u00e1lido.");
                string full = Path.GetFullPath(Path.Combine(_root, fileName));
                if (!full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
                    throw new FileNotFoundException("No se encuentra el Setup asociado.");
                var info = new FileInfo(full);
                if (manifest.SizeBytes <= 0 || info.Length != manifest.SizeBytes)
                    throw new InvalidDataException("El tama\u00f1o del Setup no coincide con su manifiesto.");
                using var stream = File.OpenRead(full);
                string hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
                if (!hash.Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("El SHA-256 del Setup no coincide con su manifiesto.");
                candidates.Add((version, manifest, full));
            }
            catch (Exception ex)
            {
                error = $"{Path.GetFileName(manifestPath)}: {ex.Message}";
            }
        }

        var latest = candidates.OrderByDescending(c => c.Version).FirstOrDefault();
        return latest.Manifest == null ? null : new(latest.Manifest, latest.Path);
    }
}
