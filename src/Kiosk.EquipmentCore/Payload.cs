using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using KioskClinicaPC.Equipment;

namespace Kiosk.EquipmentSetup;

internal sealed record PayloadManifest(int SchemaVersion, int CatalogApiVersion, string InstallerKind,
    string AssistantVersion, string WorkerVersion, string KioskVersion, string SourceCommit, string WorkerSha256, string KioskSha256,
    string Edition = "complete", int ComponentProtocolVersion = 0, SetupComponent? Worker = null, SetupComponent? Kiosk = null);
internal static class Payload
{
    private static Assembly _source = typeof(Payload).Assembly;
    private static string? _directory;
    internal static void UseAssembly(Assembly source) { _source = source; _directory = null; }
    internal static void UseDirectory(string directory) { _directory = Path.GetFullPath(directory); }
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    internal static Stream Open(string name)
    {
        if (name != Path.GetFileName(name)) throw EquipmentDiagnostics.InvalidData("Nombre de recurso no válido.");
        if (_directory != null) SetupComponentCache.SafePath(Path.Combine(_directory, name));
        return _directory is null ? _source.GetManifestResourceStream("Equipment." + name)
            ?? throw EquipmentDiagnostics.InvalidData("Falta un recurso del asistente.") : File.OpenRead(Path.Combine(_directory, name));
    }
    internal static T Read<T>(string name) { using var stream = Open(name); return JsonSerializer.Deserialize<T>(stream, Json) ?? throw EquipmentDiagnostics.InvalidData("Recurso incompatible."); }
    internal static EquipmentConfiguration Configuration => Read<EquipmentConfiguration>("config.json");
    internal static PayloadManifest Manifest => Read<PayloadManifest>("payload.json");
    internal static bool Included(string name) => _directory is not null ? File.Exists(Path.Combine(_directory, name)) :
        _source.GetManifestResourceNames().Contains("Equipment." + name);
    internal static PanelPasswordProvisioning? PanelPassword()
    {
        if (!Included("kiosk-password.json")) return null; // Older/local assistants remain compatible.
        using var stream = Open("kiosk-password.json");
        using var reader = new StreamReader(stream);
        char[] buffer = new char[4097];
        int length = reader.ReadBlock(buffer, 0, buffer.Length);
        if (length > 4096) throw EquipmentDiagnostics.InvalidData("Aprovisionamiento de contraseña demasiado grande.");
        PanelPasswordProvisioning? seed;
        try { seed = JsonSerializer.Deserialize<PanelPasswordProvisioning>(new string(buffer, 0, length), Json); }
        catch (JsonException) { throw EquipmentDiagnostics.InvalidData("Formato del aprovisionamiento de contraseña inválido."); }
        return seed?.IsCompatible() == true ? seed : throw EquipmentDiagnostics.InvalidData("Aprovisionamiento de contraseña incompatible.");
    }
    internal static bool ConfigurationCompatible()
    {
        var config = Configuration;
        return Uri.TryCreate(config.ServerUrl, UriKind.Absolute, out var server) &&
            (server.Scheme == "https" || server.Scheme == "http" && server.IsLoopback) &&
            server.UserInfo.Length == 0 && server.Query.Length == 0 && server.Fragment.Length == 0 &&
            System.Text.RegularExpressions.Regex.IsMatch(config.SetupKey ?? "", "^[a-fA-F0-9]{64}$");
    }
    internal static bool Compatible()
    {
        var manifest = Manifest;
        var actual = _source.GetName().Version;
        return (manifest.SchemaVersion == 2 || manifest.SchemaVersion == 3 && manifest.Edition is "online" or "complete" &&
            manifest.ComponentProtocolVersion == 1 && manifest.Worker is { Compatible: true, Kind: "worker" } && manifest.Kiosk is { Compatible: true, Kind: "kiosk" } &&
            manifest.Worker.Version == manifest.WorkerVersion && manifest.Kiosk.Version == manifest.KioskVersion &&
            manifest.Worker.Sha256 == manifest.WorkerSha256 && manifest.Kiosk.Sha256 == manifest.KioskSha256) &&
            manifest.CatalogApiVersion == 3 && manifest.InstallerKind == "equipment-wpf" &&
            Version.TryParse(manifest.AssistantVersion, out var assistant) && actual?.ToString(3) == assistant.ToString(3) &&
            Version.TryParse(manifest.WorkerVersion, out _) && Version.TryParse(manifest.KioskVersion, out _) &&
            System.Text.RegularExpressions.Regex.IsMatch(manifest.SourceCommit, "^[a-fA-F0-9]{40}$") &&
            System.Text.RegularExpressions.Regex.IsMatch(manifest.WorkerSha256, "^[a-fA-F0-9]{64}$") &&
            System.Text.RegularExpressions.Regex.IsMatch(manifest.KioskSha256, "^[a-fA-F0-9]{64}$");
    }
    internal static bool WorkerMetadataPresent()
    {
        using var resource = Open("worker.zip");
        return ValidateWorkerArchive(resource) && (Manifest.SchemaVersion != 3 || PackWorkerCompatibility.Matches(resource, Manifest.WorkerVersion, Manifest.SourceCommit));
    }
    internal static bool ValidateWorkerArchive(Stream resource) => PackWorkerCompatibility.ValidateArchive(resource);
    private static bool SafeEntry(ZipArchiveEntry entry) => PackWorkerCompatibility.SafeEntry(entry);
    internal static async Task<bool> Verify(string name, string hash, CancellationToken ct)
    {
        using var stream = Open(name);
        var manifest = Manifest;
        if (manifest.SchemaVersion == 3)
        {
            var descriptor = name == "worker.zip" ? manifest.Worker : manifest.Kiosk;
            if (descriptor is null || stream.Length != descriptor.SizeBytes || descriptor.Sha256 != hash) return false;
        }
        return hash.Length == 64 && Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).Equals(hash, StringComparison.OrdinalIgnoreCase);
    }
    internal static async Task<string> Extract(string name, string hash, string work, Action<EquipmentEvent> progress, CancellationToken ct)
    {
        string target = Path.Combine(work, name);
        SetupComponentCache.SafePath(target);
        if (File.Exists(target))
        {
            await using var prepared = File.OpenRead(target);
            if (!Convert.ToHexString(await SHA256.HashDataAsync(prepared, ct)).Equals(hash, StringComparison.OrdinalIgnoreCase))
                throw EquipmentDiagnostics.InvalidData("El componente preparado no supera SHA-256.");
            return target;
        }
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
        if (!Convert.ToHexString(await SHA256.HashDataAsync(file, ct)).Equals(hash, StringComparison.OrdinalIgnoreCase)) throw EquipmentDiagnostics.InvalidData("El recurso no supera SHA-256.");
        return target;
    }
    internal static void ValidateKioskVersion(string? actual, string expected)
    {
        if (string.Equals(actual?.Trim(), expected, StringComparison.Ordinal)) return;
        // Log only parsed version numbers, never arbitrary strings from executable metadata.
        string observed = Version.TryParse(actual?.Trim(), out var version) ? version.ToString() : "ausente o no válida";
        string required = Version.TryParse(expected, out var wanted) ? wanted.ToString() : "no válida";
        throw EquipmentDiagnostics.InvalidData($"Versión del instalador Kiosk incompatible; esperada {required}; detectada {observed}.");
    }
    internal static async Task Prepare(EquipmentRequest request, string work, HttpClient http, Action<EquipmentEvent> progress, CancellationToken ct)
    {
        var manifest = Manifest;
        foreach (var (selected, name, descriptor, hash) in new[] {
            (request.Pack, "worker.zip", manifest.Worker, manifest.WorkerSha256),
            (request.Kiosk, "kiosk.exe", manifest.Kiosk, manifest.KioskSha256) })
        {
            if (!selected) continue;
            string label = name == "worker.zip" ? "trabajador WinGet" : "Kiosk";
            string stage = "extracción";
            void Stage(string value)
            {
                stage = value;
                progress(new("prepare-stage", $"{label}; fase: {stage}"));
            }
            try
            {
                if (name == "kiosk.exe")
                {
                    Stage("validación del aprovisionamiento inicial");
                    if (PanelPassword() != null && (!Version.TryParse(manifest.KioskVersion, out var kioskVersion) || kioskVersion < new Version(1, 2, 1)))
                        throw EquipmentDiagnostics.InvalidData("La contraseña inicial del panel requiere Kiosk 1.2.1 o posterior.");
                }
                string path = Path.Combine(work, name);
                if (_directory is not null || manifest.Edition == "complete")
                {
                    Stage("extracción del recurso");
                    await Extract(name, hash, work, progress, ct);
                }
                else
                {
                    Stage("descarga y copia desde caché");
                    if (descriptor is null) throw EquipmentDiagnostics.InvalidData("Falta el descriptor del componente.");
                    progress(new("prepare", "Descargando " + label));
                    await new SetupComponentCache(Path.Combine(MachineState.Root, "cache"), http, Configuration)
                        .CopyTo(descriptor, path, p => progress(new("prepare", "Descarga de " + label, Percent: p)), ct);
                }
                Stage("verificación de tamaño y SHA-256");
                if (descriptor != null && !await SetupComponentCache.Verify(path, descriptor, ct))
                    throw EquipmentDiagnostics.InvalidData("El componente preparado no supera la comprobación de tamaño o SHA-256.");
                if (name == "worker.zip")
                {
                    Stage("verificación del archivo y metadatos WinGet");
                    using var archive = File.OpenRead(path);
                    if (!ValidateWorkerArchive(archive) || manifest.SchemaVersion == 3 && !PackWorkerCompatibility.Matches(archive, manifest.WorkerVersion, manifest.SourceCommit))
                        throw EquipmentDiagnostics.InvalidData("Archivo o metadatos del trabajador WinGet incompatibles con el manifiesto.");
                }
                else if (manifest.SchemaVersion == 3)
                {
                    Stage("verificación de versión del instalador");
                    ValidateKioskVersion(System.Diagnostics.FileVersionInfo.GetVersionInfo(path).ProductVersion, manifest.KioskVersion);
                }
                Stage("componente preparado y verificado");
            }
            catch (OperationCanceledException ex)
            {
                progress(new("diagnostic", $"{label}; fase: {stage}; {(ct.IsCancellationRequested ? "cancelación solicitada" : "tiempo agotado")}; {EquipmentDiagnostics.Describe(ex)}"));
                if (ct.IsCancellationRequested) throw;
                throw new ComponentPreparationException($"La preparación de {label} agotó su tiempo. Comprueba la conexión y reintenta.", ex);
            }
            catch (Exception ex)
            {
                progress(new("diagnostic", $"{label}; fase: {stage}; {EquipmentDiagnostics.Describe(ex)}"));
                throw new ComponentPreparationException($"No se pudo preparar {label}. Comprueba la conexión y los archivos del asistente y reintenta.", ex);
            }
        }
    }
    internal static async Task<string> ExtractWorker(string work, Action<EquipmentEvent> progress, CancellationToken ct)
    {
        var zip = await Extract("worker.zip", Manifest.WorkerSha256, work, progress, ct);
        string directory = Path.Combine(work, "worker"); SetupComponentCache.SafePath(directory); Directory.CreateDirectory(directory);
        using (var stream = File.OpenRead(zip)) if (!ValidateWorkerArchive(stream)) throw EquipmentDiagnostics.InvalidData("Archivo del trabajador incompatible.");
        using var archive = ZipFile.OpenRead(zip);
        long total = archive.Entries.Sum(e => e.Length), written = 0;
        if (total > 1024L * 1024 * 1024 || archive.Entries.Count > 2000) throw EquipmentDiagnostics.InvalidData("Archivo del trabajador incompatible.");
        foreach (var entry in archive.Entries)
        {
            string path = Path.GetFullPath(Path.Combine(directory, entry.FullName));
            SetupComponentCache.SafePath(path);
            if (!path.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !SafeEntry(entry))
                throw EquipmentDiagnostics.InvalidData("Ruta interna incompatible.");
            if (entry.FullName.EndsWith('/')) { Directory.CreateDirectory(path); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var input = entry.Open(); await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
            byte[] buffer = new byte[81920]; long entryWritten = 0; int read;
            while ((read = await input.ReadAsync(buffer, ct)) != 0)
            {
                entryWritten += read;
                if (entryWritten > entry.Length) throw EquipmentDiagnostics.InvalidData("El ZIP excede su tamaño declarado.");
                await output.WriteAsync(buffer.AsMemory(0, read), ct);
            }
            if (entryWritten != entry.Length) throw EquipmentDiagnostics.InvalidData("Entrada ZIP incompleta.");
            written += entryWritten;
            progress(new("extract", "Extrayendo trabajador WinGet", Percent: total == 0 ? 100 : (int)(100 * written / total)));
        }
        if (!File.Exists(Path.Combine(directory, "KioskSetupHelper.exe")) || !File.Exists(Path.Combine(directory, "Microsoft.Management.Deployment.winmd")))
            throw EquipmentDiagnostics.InvalidData("Faltan el trabajador o sus metadatos WinGet.");
        return Path.Combine(directory, "KioskSetupHelper.exe");
    }
}
