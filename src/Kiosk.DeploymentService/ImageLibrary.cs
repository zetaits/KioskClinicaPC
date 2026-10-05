using System.Security.Cryptography;
using Kiosk.Deployment;

namespace Kiosk.DeploymentService;

public sealed record LocalImageIntegrity(Dictionary<string, string> Files);
public sealed class ImageLibrary(StationState state)
{
    private readonly SemaphoreSlim _import = new(1, 1);
    private readonly object _verifiedGate = new();
    private readonly Dictionary<string, Dictionary<string, (long Length, DateTime LastWrite)>> _verified = [];
    public string Root => Path.Combine(state.Settings.Storage ?? throw new InvalidDataException("Selecciona una carpeta de almacenamiento."), "images");
    public string DirectoryFor(string id)
    { DeploymentPolicy.Require(DeploymentPolicy.IsHash(id), "Identificador de imagen no válido."); return Path.Combine(Root, id); }
    public async Task<DeploymentImage> Import(string iso, CancellationToken ct)
    {
        DeploymentPolicy.Require(Path.IsPathFullyQualified(iso) && Path.GetExtension(iso).Equals(".iso", StringComparison.OrdinalIgnoreCase) && File.Exists(iso), "Selecciona una ISO local existente.");
        await _import.WaitAsync(ct);
        try
        {
            StationState.SecureDirectory(Root);
            string hash;
            await using (var file = File.OpenRead(iso)) hash = Convert.ToHexString(await SHA256.HashDataAsync(file, ct));
            string target = DirectoryFor(hash);
            DeploymentPolicy.Require(!Directory.Exists(target), "Esta ISO ya está importada. Comprueba el catálogo.");
            string stage = Path.Combine(Root, "import-" + Guid.NewGuid().ToString("N"));
            StationState.SecureDirectory(stage);
            try
            {
                var image = await PowerShellRunner.Run<DeploymentImage>("Import-Iso.ps1", new { Iso = iso, Target = stage, Hash = hash }, ct);
                DeploymentPolicy.Image(image);
                var files = new Dictionary<string, string>();
                foreach (var path in Directory.EnumerateFiles(stage, "*", SearchOption.AllDirectories))
                {
                    DeploymentPolicy.Require((File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0, "La ISO contiene una ruta redirigida.");
                    using var file = File.OpenRead(path); files[Path.GetRelativePath(stage, path)] = Convert.ToHexString(SHA256.HashData(file));
                }
                AtomicState.Write(Path.Combine(stage, "integrity.json"), new LocalImageIntegrity(files));
                // Verify the source has not changed during mounting/extraction.
                await using (var source = File.OpenRead(iso)) DeploymentPolicy.Require(Convert.ToHexString(await SHA256.HashDataAsync(source, ct)) == hash, "La ISO cambió durante la importación.");
                Directory.Move(stage, target);
                state.Queue.AddImage(image); return image;
            }
            catch
            {
                // Preserve an incomplete import for diagnosis; it is never registered as usable.
                throw;
            }
        }
        finally { _import.Release(); }
    }
    public bool Verify(DeploymentImage image)
    {
        try
        {
            string root = DirectoryFor(image.Id);
            var manifest = AtomicState.Read<LocalImageIntegrity>(Path.Combine(root, "integrity.json"), () => throw new InvalidDataException());
            var stamps = new Dictionary<string, (long, DateTime)>();
            bool valid = manifest.Files.Count > 0 && manifest.Files.All(pair =>
            {
                string file = Path.GetFullPath(Path.Combine(root, pair.Key));
                if (!file.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(file) || (File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) return false;
                var info = new FileInfo(file); stamps[file] = (info.Length, info.LastWriteTimeUtc);
                using var input = File.OpenRead(file); return Convert.ToHexString(SHA256.HashData(input)) == pair.Value;
            });
            lock (_verifiedGate) { if (valid) _verified[image.Id] = stamps; else _verified.Remove(image.Id); }
            return valid;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { return false; }
    }
    public bool VerifiedForClaim(DeploymentImage image)
    {
        lock (_verifiedGate)
        {
            return _verified.TryGetValue(image.Id, out var stamps) && stamps.All(pair =>
            {
                var info = new FileInfo(pair.Key);
                return info.Exists && (info.Attributes & FileAttributes.ReparsePoint) == 0 && info.Length == pair.Value.Length && info.LastWriteTimeUtc == pair.Value.LastWrite;
            });
        }
    }
}
