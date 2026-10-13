using System.Security;
using System.Text.Json;
using KioskClinicaPC.Equipment;

namespace Kiosk.Server.Services;

public sealed partial class SetupReleaseStore
{
    public const int RetainedVersionCount = 5;

    public async Task Prune(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try { ct.ThrowIfCancellationRequested(); TryPruneLocked(); }
        finally { _gate.Release(); }
    }

    // Called only under the same gate as import/activation. A cleanup failure
    // must not report a successfully stored/activated publication as failed.
    private void TryPruneLocked()
    {
        try
        {
            var versions = MetadataVersions();
            var pointer = ReadPointer(); // Fail closed if the active pointer is unreadable.
            var keep = new HashSet<string>(StringComparer.Ordinal);
            foreach (string? protectedVersion in new[] { pointer.Version, pointer.PreviousVersion })
            {
                if (protectedVersion is null) continue;
                _ = ReleasePath(protectedVersion);
                if (versions.Contains(protectedVersion)) keep.Add(protectedVersion);
            }
            if (pointer.Version is { } active && !keep.Contains(active))
                throw new InvalidDataException("La versión activa no está disponible; se conserva el almacén para recuperación.");
            foreach (string version in versions)
                if (keep.Count < RetainedVersionCount) keep.Add(version);

            foreach (string version in versions.Where(version => !keep.Contains(version)))
            {
                try
                {
                    string directory = ReleasePath(version);
                    RequireDirectChild(directory, Path.Combine(_root, "releases"));
                    RequireSafeTree(directory);
                    RecordRetirement(version); // Preserve version uniqueness after deleting its binaries.
                    Directory.Delete(directory, recursive: true);
                    _logger.LogInformation("Publicación antigua del asistente {Version} retirada por retención", version);
                }
                catch (Exception ex) when (IsRetentionError(ex))
                { _logger.LogWarning(ex, "No se pudo retirar la publicación {Version}; se reintentará la limpieza", version); }
            }
            PruneUnusedComponents();
        }
        catch (Exception ex) when (IsRetentionError(ex))
        { _logger.LogWarning(ex, "No se pudo completar la retención del asistente; se conserva el contenido pendiente"); }
    }

    private string RetirementPath(string version)
    {
        _ = ReleasePath(version);
        string marker = Path.Combine(_root, "retired", version);
        SetupComponentCache.SafePath(marker);
        return marker;
    }

    private void RecordRetirement(string version)
    {
        string marker = RetirementPath(version), directory = Path.GetDirectoryName(marker)!;
        RequireDirectChild(directory, _root);
        Directory.CreateDirectory(directory);
        RequireDirectChild(marker, directory);
        if (!File.Exists(marker)) File.WriteAllText(marker, version);
    }

    private void PruneUnusedComponents()
    {
        var referenced = new HashSet<(string Kind, string Hash)>();
        // Read every remaining manifest, including a release whose removal failed.
        // An opaque/unsafe directory makes component collection unsafe: retain all.
        foreach (string directory in Directory.EnumerateDirectories(Path.Combine(_root, "releases")))
        {
            string version = Path.GetFileName(directory);
            _ = ReleasePath(version);
            RequireDirectChild(directory, Path.Combine(_root, "releases"));
            string manifest = Path.Combine(directory, "manifest.json");
            SetupComponentCache.SafePath(manifest);
            var release = RequireCompatible(JsonSerializer.Deserialize<SetupRelease>(File.ReadAllText(manifest), Json), version);
            foreach (var component in release.Components) referenced.Add((component.Kind, component.Sha256));
        }
        foreach (string kind in new[] { "worker", "kiosk" })
        {
            string directory = Path.Combine(_root, "components", kind);
            SetupComponentCache.SafePath(directory);
            foreach (string file in Directory.EnumerateFiles(directory))
            {
                string hash = Path.GetFileName(file);
                if (!System.Text.RegularExpressions.Regex.IsMatch(hash, "^[a-f0-9]{64}$") || referenced.Contains((kind, hash))) continue;
                try
                {
                    RequireDirectChild(file, directory);
                    SetupComponentCache.SafePath(file);
                    File.Delete(file);
                }
                catch (Exception ex) when (IsRetentionError(ex))
                { _logger.LogWarning(ex, "No se pudo retirar un componente {Kind} sin referencias; se reintentará", kind); }
            }
        }
    }

    private static void RequireDirectChild(string path, string parent)
    {
        if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), Path.GetFullPath(parent), StringComparison.Ordinal))
            throw new InvalidDataException("Ruta de limpieza fuera del almacén del asistente.");
        SetupComponentCache.SafePath(parent);
    }

    private static void RequireSafeTree(string directory)
    {
        SetupComponentCache.SafePath(directory);
        foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
        {
            SetupComponentCache.SafePath(entry);
            if (Directory.Exists(entry)) RequireSafeTree(entry);
        }
    }

    private static bool IsRetentionError(Exception ex)
        => InstallerMetadataFiles.IsReadError(ex) || ex is SecurityException;
}

/// <summary>Apply retention to existing stores at startup; retry busy files hourly.</summary>
public sealed class SetupReleaseRetentionService(SetupReleaseStore releases) : BackgroundService
{
    public override async Task StartAsync(CancellationToken ct)
    {
        await releases.Prune(ct);
        await base.StartAsync(ct);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        while (await timer.WaitForNextTickAsync(stoppingToken)) await releases.Prune(stoppingToken);
    }
}
