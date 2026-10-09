using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace KioskClinicaPC.Equipment;

public sealed record PackWorkerManifest(int SchemaVersion, int CatalogApiVersion, string WorkerVersion, string SourceCommit);
public static class PackWorkerCompatibility
{
    public static bool ValidateArchive(Stream resource, bool requireNativeActivation = true)
    {
        try
        {
            resource.Position = 0;
            using (var archive = new ZipArchive(resource, ZipArchiveMode.Read, leaveOpen: true))
            {
                if (archive.Entries.Count > 2000 || archive.Entries.Sum(e => e.Length) > 1024L * 1024 * 1024 ||
                    archive.Entries.Any(e => !SafeEntry(e)) || archive.Entries.Select(e => e.FullName.TrimEnd('/')).Distinct(StringComparer.OrdinalIgnoreCase).Count() != archive.Entries.Count) return false;
                var files = archive.Entries.Where(e => !e.FullName.EndsWith('/')).Select(e => e.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in archive.Entries)
                {
                    string[] parts = entry.FullName.TrimEnd('/').Split('/');
                    for (int i = 1; i < parts.Length; i++) if (files.Contains(string.Join('/', parts.Take(i)))) return false;
                }
                if (!new[] { "KioskSetupHelper.exe", "KioskSetupHelper.dll", "KioskSetupHelper.runtimeconfig.json", "Microsoft.Management.Deployment.winmd" }
                    .All(name => archive.Entries.Count(e => e.FullName == name && e.Length > 0) == 1)) return false;
                if (requireNativeActivation && archive.Entries.Count(e => e.FullName == "Microsoft.Management.Deployment.dll" && e.Length > 0) != 1) return false;
            }
            resource.Position = 0;
            return Validate(resource);
        }
        catch (InvalidDataException) { return false; }
    }
    public static bool SafeEntry(ZipArchiveEntry entry) => !string.IsNullOrEmpty(entry.FullName) && !entry.FullName.StartsWith('/') &&
        !entry.FullName.Contains(':') && !entry.FullName.Contains('\\') &&
        !entry.FullName.Any(c => c < 32 || "<>\"|?*".Contains(c)) && !entry.FullName.Contains("//") &&
        !entry.FullName.Split('/').Any(p => p is "." or ".." || p.EndsWith('.') || p.EndsWith(' ')) &&
        (entry.ExternalAttributes >> 16 & 0xF000) != 0xA000 && (entry.ExternalAttributes & 0x400) == 0 &&
        !entry.FullName.Split('/').Any(p => System.Text.RegularExpressions.Regex.IsMatch(p, "^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\\.|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
    public static bool Matches(Stream resource, string version, string commit)
    {
        resource.Position = 0;
        using var archive = new ZipArchive(resource, ZipArchiveMode.Read, leaveOpen: true);
        var metadata = ReadMetadata(archive);
        return metadata?.WorkerVersion == version && metadata.SourceCommit == commit;
    }
    private static PackWorkerManifest? ReadMetadata(ZipArchive archive)
    {
        if (archive.GetEntry("pack-worker.json") is not { Length: > 0 and <= 4096 } entry) return null;
        using var input = entry.Open();
        byte[] buffer = new byte[4097]; int total = 0, read;
        while (total < buffer.Length && (read = input.Read(buffer, total, buffer.Length - total)) > 0) total += read;
        if (total != entry.Length || total > 4096) return null;
        return JsonSerializer.Deserialize<PackWorkerManifest>(buffer.AsSpan(0, total), new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }
    public static bool Validate(Stream resource)
    {
        try
        {
            using var archive = new ZipArchive(resource, ZipArchiveMode.Read, leaveOpen: true);
            if (archive.Entries.Count(e => e.FullName.Equals("pack-worker.json", StringComparison.OrdinalIgnoreCase)) != 1 ||
                archive.GetEntry("pack-worker.json") is not { Length: > 0 and <= 4096 } entry) return false;
            var metadata = ReadMetadata(archive);
            return metadata is { SchemaVersion: 1, CatalogApiVersion: 3 } && Version.TryParse(metadata.WorkerVersion, out _) &&
                Regex.IsMatch(metadata.SourceCommit ?? "", "^[a-fA-F0-9]{40}$");
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException) { return false; }
    }
}
