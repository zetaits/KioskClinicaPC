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

    private readonly SemaphoreSlim _importGate = new(1, 1);
    public async Task ImportAsync(InitialSetupBundleManifest manifest, string uploadedName, Stream input, CancellationToken ct)
    {
        if (!Version.TryParse(manifest.Version, out _) || manifest.FileName != Path.GetFileName(manifest.FileName) ||
            manifest.FileName != uploadedName || !manifest.FileName.StartsWith("Setup-EquipoClinicaPC-", StringComparison.Ordinal) ||
            !manifest.FileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || manifest.SizeBytes is <= 0 or > 1024L * 1024 * 1024)
            throw new InvalidDataException("Manifiesto del instalador interno no válido.");
        await _importGate.WaitAsync(ct);
        string staging = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            string target = Path.Combine(_root, manifest.FileName);
            string manifestPath = Path.ChangeExtension(target, ".bundle.json");
            if (File.Exists(target) || File.Exists(manifestPath)) throw new InvalidDataException("Esta versión ya existe; publica una nueva versión.");
            await using (var file = File.Create(staging))
            {
                byte[] buffer = new byte[81920]; long total = 0; int read;
                while ((read = await input.ReadAsync(buffer, ct)) > 0)
                {
                    total += read;
                    if (total > manifest.SizeBytes) throw new InvalidDataException("El tamaño excede el manifiesto.");
                    await file.WriteAsync(buffer.AsMemory(0, read), ct);
                }
                if (total != manifest.SizeBytes) throw new InvalidDataException("Tamaño incorrecto.");
            }
            await using (var file = File.OpenRead(staging))
                if (!Convert.ToHexString(await SHA256.HashDataAsync(file, ct)).Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("SHA-256 incorrecto.");
            File.Move(staging, target);
            try
            {
                await File.WriteAllTextAsync(staging, JsonConvert.SerializeObject(manifest), ct);
                File.Move(staging, manifestPath);
            }
            catch { File.Delete(target); throw; }
        }
        finally { if (File.Exists(staging)) File.Delete(staging); _importGate.Release(); }
    }

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
