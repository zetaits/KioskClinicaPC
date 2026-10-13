using System.Security.Cryptography;
using System.Text.Json;
using Kiosk.Deployment;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kiosk.Server.Services;

public sealed class DeploymentReleaseStore
{
    private readonly string _root;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ILogger<DeploymentReleaseStore> _logger;
    public DeploymentReleaseStore(string setups, ILogger<DeploymentReleaseStore>? logger = null)
    {
        _root = Path.Combine(setups, "deployment");
        _logger = logger ?? NullLogger<DeploymentReleaseStore>.Instance;
        Directory.CreateDirectory(_root);
    }
    public static bool Compatible(DeploymentRelease r) => r.SchemaVersion == 1 && r.ProtocolVersion == 1 &&
        r.InstallerKind == "deployment-wpf" && Version.TryParse(r.Version, out _) &&
        r.FileName == $"Setup-InstalacionRedClinicaPC-{r.Version}.exe" && r.SizeBytes is > 0 and <= 1024L * 1024 * 1024 &&
        DeploymentPolicy.IsHash(r.Sha256) && System.Text.RegularExpressions.Regex.IsMatch(r.SourceCommit ?? "", "^[a-fA-F0-9]{40}$") &&
        r.AdkVersion == "10.1.26100.9457" && r.RealValidationPassed && r.BootHashes is not null &&
        new[] { "ipxe-shim.efi", "ipxe.efi", "wimboot", "boot.wim" }.All(n => r.BootHashes.TryGetValue(n, out var h) && DeploymentPolicy.IsHash(h));
    public async Task Import(DeploymentRelease release, Stream input, CancellationToken ct)
    {
        DeploymentPolicy.Require(Compatible(release), "Paquete incompatible o sin validación real de PXE e instalación.");
        await _gate.WaitAsync(ct);
        string temp = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".tmp");
        string target = Path.Combine(_root, release.FileName);
        try
        {
            DeploymentPolicy.Require(!File.Exists(target) && !File.Exists(target + ".json"), "La versión ya existe. Publica otra versión.");
            await using (var output = File.Create(temp))
            {
                byte[] buffer = new byte[81920]; long written = 0; int read;
                while ((read = await input.ReadAsync(buffer, ct)) > 0)
                {
                    written += read; DeploymentPolicy.Require(written <= release.SizeBytes, "Tamaño de paquete incorrecto.");
                    await output.WriteAsync(buffer.AsMemory(0, read), ct);
                }
                DeploymentPolicy.Require(written == release.SizeBytes, "Paquete incompleto.");
            }
            using (var file = File.OpenRead(temp)) DeploymentPolicy.Require(
                Convert.ToHexString(SHA256.HashData(file)).Equals(release.Sha256, StringComparison.OrdinalIgnoreCase), "Integridad del paquete incorrecta.");
            File.Move(temp, target);
            try { AtomicState.Write(target + ".json", release); }
            catch { File.Delete(target); throw; }
        }
        finally { if (File.Exists(temp)) File.Delete(temp); _gate.Release(); }
    }
    public (DeploymentRelease Release, string Path)? Latest()
    {
        return Directory.EnumerateFiles(_root, "*.exe.json").Select(ReadVerified)
            .Where(r => r.HasValue).OrderByDescending(r => Version.Parse(r!.Value.Release.Version)).FirstOrDefault();
    }

    public (DeploymentRelease Release, string Path)? Find(string version)
    {
        if (!Version.TryParse(version, out _)) return null;
        return ReadVerified(Path.Combine(_root, $"Setup-InstalacionRedClinicaPC-{version}.exe.json"));
    }

    private DeploymentRelease RequireMetadata(DeploymentRelease? release, string manifestPath)
    {
        if (release is null || !Compatible(release) || Path.GetFileName(manifestPath) != release.FileName + ".json")
            throw new InvalidDataException("Manifiesto de instalación por red incompatible.");
        InstallerMetadataFiles.RequireAvailable(Path.Combine(_root, release.FileName), release.SizeBytes);
        return release;
    }

    private (DeploymentRelease Release, string Path)? ReadVerified(string path)
    {
        try
        {
            KioskClinicaPC.Equipment.SetupComponentCache.SafePath(path);
            var release = RequireMetadata(JsonSerializer.Deserialize<DeploymentRelease>(File.ReadAllText(path), AtomicState.Json), path);
            string exe = Path.Combine(_root, release.FileName);
            using var file = File.OpenRead(exe);
            return Convert.ToHexString(SHA256.HashData(file)).Equals(release.Sha256, StringComparison.OrdinalIgnoreCase)
                ? (release, exe) : null;
        }
        catch (Exception ex) when (InstallerMetadataFiles.IsReadError(ex)) { return null; }
    }

    public async Task<DeploymentSectionData?> LatestMetadataAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var paths = Directory.EnumerateFiles(_root, "*.exe.json").ToList();
        DeploymentRelease? latest = null;
        foreach (string path in paths)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                await using var manifest = InstallerMetadataFiles.OpenManifest(path);
                var release = RequireMetadata(await JsonSerializer.DeserializeAsync<DeploymentRelease>(manifest, AtomicState.Json, ct), path);
                if (latest is null || Version.Parse(release.Version) > Version.Parse(latest.Version)) latest = release;
            }
            catch (Exception ex) when (InstallerMetadataFiles.IsReadError(ex))
            { _logger.LogWarning(ex, "Publicación de instalación por red {Manifest} omitida del catálogo", Path.GetFileName(path)); }
        }
        if (paths.Count > 0 && latest is null) throw new InvalidDataException("Ninguna publicación de instalación por red está disponible.");
        return latest is null ? null : new(latest.Version);
    }
}
