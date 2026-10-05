using System.Security.Cryptography;
using System.Text.Json;
using Kiosk.Deployment;

namespace Kiosk.Server.Services;

public sealed class DeploymentReleaseStore
{
    private readonly string _root;
    private readonly SemaphoreSlim _gate = new(1, 1);
    public DeploymentReleaseStore(string setups) { _root = Path.Combine(setups, "deployment"); Directory.CreateDirectory(_root); }
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
        return Directory.EnumerateFiles(_root, "*.exe.json").Select(path =>
        {
            try
            {
                var r = JsonSerializer.Deserialize<DeploymentRelease>(File.ReadAllText(path), AtomicState.Json);
                if (r is null || !Compatible(r)) return ((DeploymentRelease Release, string Path)?)null;
                string exe = Path.Combine(_root, r.FileName);
                if (!File.Exists(exe) || new FileInfo(exe).Length != r.SizeBytes) return null;
                using var file = File.OpenRead(exe);
                return Convert.ToHexString(SHA256.HashData(file)).Equals(r.Sha256, StringComparison.OrdinalIgnoreCase) ? (r, exe) : null;
            }
            catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException) { return null; }
        }).Where(r => r.HasValue).OrderByDescending(r => Version.Parse(r!.Value.Release.Version)).FirstOrDefault();
    }
}
