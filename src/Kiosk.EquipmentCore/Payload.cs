using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using KioskClinicaPC.Equipment;

namespace Kiosk.EquipmentSetup;

internal sealed record PayloadManifest(int SchemaVersion, int CatalogApiVersion, string InstallerKind,
    string AssistantVersion, string WorkerVersion, string KioskVersion, string SourceCommit, string WorkerSha256, string KioskSha256);
internal static class Payload
{
    private static Assembly _source = typeof(Payload).Assembly;
    private static string? _directory;
    internal static void UseAssembly(Assembly source) { _source = source; _directory = null; }
    internal static void UseDirectory(string directory) { _directory = Path.GetFullPath(directory); }
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    internal static Stream Open(string name)
    {
        if (name != Path.GetFileName(name)) throw new InvalidDataException("Nombre de recurso no válido.");
        return _directory is null ? _source.GetManifestResourceStream("Equipment." + name)
            ?? throw new InvalidDataException("Falta un recurso del asistente.") : File.OpenRead(Path.Combine(_directory, name));
    }
    internal static T Read<T>(string name) { using var stream = Open(name); return JsonSerializer.Deserialize<T>(stream, Json) ?? throw new InvalidDataException("Recurso incompatible."); }
    internal static EquipmentConfiguration Configuration => Read<EquipmentConfiguration>("config.json");
    internal static PayloadManifest Manifest => Read<PayloadManifest>("payload.json");
    internal static bool Compatible()
    {
        var manifest = Manifest;
        var actual = _source.GetName().Version;
        return manifest.SchemaVersion == 2 && manifest.CatalogApiVersion == 3 && manifest.InstallerKind == "equipment-wpf" &&
            Version.TryParse(manifest.AssistantVersion, out var assistant) && actual?.ToString(3) == assistant.ToString(3) &&
            Version.TryParse(manifest.WorkerVersion, out _) && Version.TryParse(manifest.KioskVersion, out _) &&
            System.Text.RegularExpressions.Regex.IsMatch(manifest.SourceCommit, "^[a-fA-F0-9]{40}$") &&
            System.Text.RegularExpressions.Regex.IsMatch(manifest.WorkerSha256, "^[a-fA-F0-9]{64}$") &&
            System.Text.RegularExpressions.Regex.IsMatch(manifest.KioskSha256, "^[a-fA-F0-9]{64}$");
    }
    internal static bool WorkerMetadataPresent()
    {
        using var resource = Open("worker.zip"); return ValidateWorkerArchive(resource);
    }
    internal static bool ValidateWorkerArchive(Stream resource)
    {
        using var archive = new ZipArchive(resource, ZipArchiveMode.Read, leaveOpen: true);
        if (archive.Entries.Count > 2000 || archive.Entries.Sum(e => e.Length) > 1024L * 1024 * 1024 ||
            archive.Entries.Any(e => !SafeEntry(e)) || archive.Entries.Select(e => e.FullName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != archive.Entries.Count) return false;
        if (!new[] { "KioskSetupHelper.exe", "KioskSetupHelper.dll", "KioskSetupHelper.runtimeconfig.json", "Microsoft.Management.Deployment.winmd" }
            .All(name => archive.Entries.Count(e => e.FullName == name && e.Length > 0) == 1)) return false;
        resource.Position = 0;
        return PackWorkerCompatibility.Validate(resource);
    }
    private static bool SafeEntry(ZipArchiveEntry entry) => !string.IsNullOrEmpty(entry.FullName) && !entry.FullName.StartsWith('/') &&
        !entry.FullName.Contains(':') && !entry.FullName.Contains('\\') &&
        !entry.FullName.Split('/').Any(p => p is "." or ".." || p.EndsWith('.') || p.EndsWith(' ')) &&
        (entry.ExternalAttributes >> 16 & 0xF000) != 0xA000;
    internal static async Task<bool> Verify(string name, string hash, CancellationToken ct)
    {
        using var stream = Open(name);
        return hash.Length == 64 && Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).Equals(hash, StringComparison.OrdinalIgnoreCase);
    }
    internal static async Task<string> Extract(string name, string hash, string work, Action<EquipmentEvent> progress, CancellationToken ct)
    {
        string target = Path.Combine(work, name);
        using var resource = Open(name);
        await using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
        {
            byte[] buffer = new byte[81920]; int read; long done = 0; int last = -1;
            while ((read = await resource.ReadAsync(buffer, ct)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), ct); done += read;
                int percent = (int)(100 * done / resource.Length);
                if (percent != last) { last = percent; progress(new("extract", "Preparando " + (name == "worker.zip" ? "trabajador WinGet" : "motor de Kiosk"), Percent: percent)); }
            }
        }
        await using var file = File.OpenRead(target);
        if (!Convert.ToHexString(await SHA256.HashDataAsync(file, ct)).Equals(hash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("El recurso no supera SHA-256.");
        return target;
    }
    internal static async Task<string> ExtractWorker(string work, Action<EquipmentEvent> progress, CancellationToken ct)
    {
        var zip = await Extract("worker.zip", Manifest.WorkerSha256, work, progress, ct);
        string directory = Path.Combine(work, "worker"); Directory.CreateDirectory(directory);
        using (var stream = File.OpenRead(zip)) if (!ValidateWorkerArchive(stream)) throw new InvalidDataException("Archivo del trabajador incompatible.");
        using var archive = ZipFile.OpenRead(zip);
        long total = archive.Entries.Sum(e => e.Length), written = 0;
        if (total > 1024L * 1024 * 1024 || archive.Entries.Count > 2000) throw new InvalidDataException("Archivo del trabajador incompatible.");
        foreach (var entry in archive.Entries)
        {
            string path = Path.GetFullPath(Path.Combine(directory, entry.FullName));
            if (!path.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !SafeEntry(entry))
                throw new InvalidDataException("Ruta interna incompatible.");
            if (entry.FullName.EndsWith('/')) { Directory.CreateDirectory(path); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var input = entry.Open(); await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
            await input.CopyToAsync(output, ct); written += entry.Length;
            progress(new("extract", "Extrayendo trabajador WinGet", Percent: total == 0 ? 100 : (int)(100 * written / total)));
        }
        if (!File.Exists(Path.Combine(directory, "KioskSetupHelper.exe")) || !File.Exists(Path.Combine(directory, "Microsoft.Management.Deployment.winmd")))
            throw new InvalidDataException("Faltan el trabajador o sus metadatos WinGet.");
        return Path.Combine(directory, "KioskSetupHelper.exe");
    }
}
