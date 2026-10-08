using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace KioskClinicaPC.Equipment;

public sealed record PackWorkerManifest(int SchemaVersion, int CatalogApiVersion, string WorkerVersion, string SourceCommit);
public static class PackWorkerCompatibility
{
    public static bool Validate(Stream resource)
    {
        try
        {
            using var archive = new ZipArchive(resource, ZipArchiveMode.Read, leaveOpen: true);
            if (archive.Entries.Count(e => e.FullName.Equals("pack-worker.json", StringComparison.OrdinalIgnoreCase)) != 1 ||
                archive.GetEntry("pack-worker.json") is not { Length: > 0 and <= 4096 } entry) return false;
            using var input = entry.Open(); var metadata = JsonSerializer.Deserialize<PackWorkerManifest>(input, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return metadata is { SchemaVersion: 1, CatalogApiVersion: 3 } && Version.TryParse(metadata.WorkerVersion, out _) &&
                Regex.IsMatch(metadata.SourceCommit ?? "", "^[a-fA-F0-9]{40}$");
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException) { return false; }
    }
}
