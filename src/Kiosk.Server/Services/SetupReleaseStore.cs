using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using KioskClinicaPC.Equipment;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kiosk.Server.Services;

/// <summary>Immutable candidates and components; only explicit panel activation changes the download pointer.</summary>
public sealed partial class SetupReleaseStore
{
    private readonly string _root;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ILogger<SetupReleaseStore> _logger;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private sealed record Pointer(string? Version, string? PreviousVersion);
    public SetupReleaseStore(string root, ILogger<SetupReleaseStore>? logger = null)
    {
        _logger = logger ?? NullLogger<SetupReleaseStore>.Instance;
        _root = Path.Combine(Path.GetFullPath(root), "v3");
        SetupComponentCache.SafePath(_root);
        foreach (string directory in new[] { Path.Combine(_root, "releases"), Path.Combine(_root, "components", "worker"), Path.Combine(_root, "components", "kiosk") })
        { SetupComponentCache.SafePath(directory); Directory.CreateDirectory(directory); }
    }
    private string ReleasePath(string version)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(version, "^[0-9]+\\.[0-9]+\\.[0-9]+$")) throw new InvalidDataException("Versión no válida.");
        return Path.Combine(_root, "releases", version);
    }
    private string ComponentPath(SetupComponent component) => Path.Combine(_root, "components", component.Kind, component.Sha256);
    private static SetupRelease RequireCompatible(SetupRelease? release, string version)
    {
        if (release is not { Compatible: true } || release.Version != version)
            throw new InvalidDataException("Manifiesto de publicación incompatible.");
        return release;
    }
    private static bool ValidFile(string path, long size, string hash)
    {
        SetupComponentCache.SafePath(path);
        if (!File.Exists(path) || new FileInfo(path).Length != size) return false;
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).Equals(hash, StringComparison.OrdinalIgnoreCase);
    }
    private static bool ValidWorker(string path, SetupRelease release)
    {
        using var stream = File.OpenRead(path);
        // Keep historical publications readable without invalidating the active pointer
        // during a server update. New releases must include the native activation DLL.
        bool requireNativeActivation = System.Version.Parse(release.WorkerVersion) >= new Version(1, 5, 6);
        return PackWorkerCompatibility.ValidateArchive(stream, requireNativeActivation) && PackWorkerCompatibility.Matches(stream, release.WorkerVersion, release.SourceCommit);
    }
    public SetupRelease? Find(string version)
    {
        try
        {
            string directory = ReleasePath(version), manifest = Path.Combine(directory, "manifest.json");
            SetupComponentCache.SafePath(manifest);
            if (!File.Exists(manifest)) return null;
            var release = RequireCompatible(JsonSerializer.Deserialize<SetupRelease>(File.ReadAllText(manifest), Json), version);
            if (release.Editions.Any(e => !ValidFile(Path.Combine(directory, e.FileName), e.SizeBytes, e.Sha256)) ||
                release.Components.Any(c => !ValidFile(ComponentPath(c), c.SizeBytes, c.Sha256))) return null;
            return ValidWorker(ComponentPath(release.Components.Single(c => c.Kind == "worker")), release) ? release : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or UnauthorizedAccessException) { return null; }
    }
    public List<SetupRelease> List() => Directory.EnumerateDirectories(Path.Combine(_root, "releases"))
        .Select(p => Find(Path.GetFileName(p))).OfType<SetupRelease>().OrderByDescending(r => System.Version.Parse(r.Version)).ToList();

    private List<string> MetadataVersions() => Directory.EnumerateDirectories(Path.Combine(_root, "releases"))
        .Select(Path.GetFileName).OfType<string>()
        .Where(v => System.Text.RegularExpressions.Regex.IsMatch(v, "^[0-9]+\\.[0-9]+\\.[0-9]+$") && System.Version.TryParse(v, out _))
        .OrderByDescending(System.Version.Parse).ToList();

    private async Task<InstallerReleaseSummary> ReadMetadataAsync(string version, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        string directory = ReleasePath(version);
        await using var manifest = InstallerMetadataFiles.OpenManifest(Path.Combine(directory, "manifest.json"));
        var release = RequireCompatible(await JsonSerializer.DeserializeAsync<SetupRelease>(manifest, Json, ct), version);
        foreach (var edition in release.Editions)
            InstallerMetadataFiles.RequireAvailable(Path.Combine(directory, edition.FileName), edition.SizeBytes);
        foreach (var component in release.Components)
            InstallerMetadataFiles.RequireAvailable(ComponentPath(component), component.SizeBytes);
        return new(release.Version, release.Editions.OrderBy(e => e.Edition == "online" ? 0 : 1)
            .Select(e => new InstallerEditionSummary(e.Edition, e.SizeBytes)).ToImmutableArray());
    }

    public async Task<InstallerReleaseSummary?> LatestMetadataAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var versions = MetadataVersions();
        foreach (string version in versions)
        {
            try { return await ReadMetadataAsync(version, ct); }
            catch (Exception ex) when (InstallerMetadataFiles.IsReadError(ex))
            { _logger.LogWarning(ex, "Publicación {Version} omitida del catálogo del instalador", version); }
        }
        if (versions.Count > 0) throw new InvalidDataException("Ninguna publicación de equipos está disponible.");
        return null;
    }

    public async Task<ImmutableArray<InstallerReleaseSummary>> HistoryMetadataAsync(string displayedVersion, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var versions = MetadataVersions().Where(v => v != displayedVersion).ToList();
        var releases = ImmutableArray.CreateBuilder<InstallerReleaseSummary>();
        foreach (string version in versions)
        {
            try { releases.Add(await ReadMetadataAsync(version, ct)); }
            catch (Exception ex) when (InstallerMetadataFiles.IsReadError(ex))
            { _logger.LogWarning(ex, "Publicación {Version} omitida del historial del instalador", version); }
        }
        if (versions.Count > 0 && releases.Count == 0) throw new InvalidDataException("Ninguna publicación del historial está disponible.");
        return releases.ToImmutable();
    }

    public async Task<string?> ReadActiveVersionMetadataAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Pointer? pointer;
        try
        {
            await using var stream = InstallerMetadataFiles.OpenManifest(Path.Combine(_root, "active.json"));
            pointer = await JsonSerializer.DeserializeAsync<Pointer>(stream, Json, ct);
        }
        catch (FileNotFoundException) { return null; }
        if (pointer is null) throw new InvalidDataException("Puntero activo vacío.");
        if (pointer.Version is { } version)
            await ReadMetadataAsync(version, ct); // Detect dangling pointers without reading binary content.
        return pointer.Version;
    }
    private Pointer ReadPointer()
    {
        string path = Path.Combine(_root, "active.json"); SetupComponentCache.SafePath(path);
        return File.Exists(path) ? JsonSerializer.Deserialize<Pointer>(File.ReadAllText(path), Json) ?? throw new InvalidDataException("Puntero activo vacío.") : new(null, null);
    }
    public string? ActiveVersion => ReadPointer().Version;
    public string? PreviousVersion => ReadPointer().PreviousVersion;
    public SetupRelease? Active() => ActiveVersion is { } version ? Find(version) : null;
    public bool Ready()
    {
        try { return ActiveVersion is not { } version || Find(version) != null; }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or UnauthorizedAccessException) { return false; }
    }
    public string EditionPath(SetupRelease release, string edition) => Path.Combine(ReleasePath(release.Version), release.Editions.Single(e => e.Edition == edition).FileName);
    public (string Path, SetupComponent Component)? PublishedComponent(string kind, string hash)
    {
        if (kind is not ("worker" or "kiosk") || !System.Text.RegularExpressions.Regex.IsMatch(hash, "^[a-f0-9]{64}$")) return null;
        // Includes prior releases and candidates, never exposes an orphan from an interrupted import.
        foreach (var release in List())
            if (release.Components.Find(c => c.Kind == kind && c.Sha256 == hash) is { } component)
                return (ComponentPath(component), component);
        return null;
    }
    public async Task Import(SetupRelease release, IReadOnlyDictionary<string, (string Name, Stream Stream)> files, CancellationToken ct)
    {
        if (!release.Compatible || files.Count != 4) throw new InvalidDataException("Publicación v3 incompleta o incompatible.");
        await _gate.WaitAsync(ct);
        string staging = Path.Combine(_root, ".stage-" + Guid.NewGuid().ToString("N"));
        try
        {
            if (File.Exists(RetirementPath(release.Version)))
                throw new InvalidDataException("Esta versión fue retirada por retención; publica una versión nueva.");
            string legacyRoot = Path.GetDirectoryName(_root)!;
            string legacyManifest = Path.Combine(legacyRoot, $"Setup-EquipoClinicaPC-{release.Version}.bundle.json");
            string legacyExe = Path.Combine(legacyRoot, $"Setup-EquipoClinicaPC-{release.Version}.exe");
            SetupComponentCache.SafePath(legacyManifest); SetupComponentCache.SafePath(legacyExe);
            if (File.Exists(legacyManifest) || File.Exists(legacyExe))
                throw new InvalidDataException("Esta versión ya existe en el almacén anterior; publica una versión nueva.");
            SetupComponentCache.SafePath(staging); Directory.CreateDirectory(staging);
            foreach (var (key, name, size, hash) in release.Editions.Select(e => (e.Edition, e.FileName, e.SizeBytes, e.Sha256))
                .Concat(release.Components.Select(c => (c.Kind, c.Kind == "worker" ? "worker.zip" : "kiosk.exe", c.SizeBytes, c.Sha256))))
            {
                if (!files.TryGetValue(key, out var input) || input.Name != name) throw new InvalidDataException("Falta un archivo de la publicación.");
                string path = Path.Combine(staging, name);
                await using (var output = File.Create(path))
                {
                    byte[] buffer = new byte[81920]; long total = 0; int read;
                    while ((read = await input.Stream.ReadAsync(buffer, ct)) != 0)
                    {
                        total += read; if (total > size) throw new InvalidDataException("Tamaño excedido.");
                        await output.WriteAsync(buffer.AsMemory(0, read), ct);
                    }
                    if (total != size) throw new InvalidDataException("Tamaño incorrecto.");
                }
                if (!ValidFile(path, size, hash)) throw new InvalidDataException("SHA-256 incorrecto.");
            }
            if (!ValidWorker(Path.Combine(staging, "worker.zip"), release)) throw new InvalidDataException("Trabajador incompatible o ZIP inseguro.");
            string target = ReleasePath(release.Version);
            SetupComponentCache.SafePath(target);
            if (Directory.Exists(target))
            {
                var existing = Find(release.Version);
                if (existing == null || JsonSerializer.Serialize(existing, Json) != JsonSerializer.Serialize(release, Json))
                    throw new InvalidDataException("Esta versión ya existe con contenido diferente.");
                TryPruneLocked();
                return; // Identical verified retry is idempotent.
            }
            foreach (var component in release.Components)
            {
                string path = ComponentPath(component); SetupComponentCache.SafePath(path);
                string source = Path.Combine(staging, component.Kind == "worker" ? "worker.zip" : "kiosk.exe");
                if (File.Exists(path))
                {
                    if (!ValidFile(path, component.SizeBytes, component.Sha256)) throw new InvalidDataException("Componente almacenado corrupto.");
                    File.Delete(source);
                }
                else File.Move(source, path);
            }
            await File.WriteAllTextAsync(Path.Combine(staging, "manifest.json"), JsonSerializer.Serialize(release, Json), ct);
            Directory.Move(staging, target); // Release visibility is atomic; active pointer is untouched.
            TryPruneLocked();
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            _gate.Release();
        }
    }
    public async Task Activate(string? version, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        string temporary = Path.Combine(_root, "active-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            if (version != null && Find(version) == null) throw new InvalidDataException("La candidata no supera la comprobación de todos sus archivos.");
            var current = ReadPointer();
            if (current.Version == version) { TryPruneLocked(); return; }
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(new Pointer(version, current.Version), Json), ct);
            SetupComponentCache.SafePath(Path.Combine(_root, "active.json"));
            File.Move(temporary, Path.Combine(_root, "active.json"), true);
            TryPruneLocked();
        }
        finally { File.Delete(temporary); _gate.Release(); }
    }
}
