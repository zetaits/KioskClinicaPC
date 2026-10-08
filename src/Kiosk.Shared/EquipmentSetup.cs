using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using KioskClinicaPC.Core.Sync;

namespace KioskClinicaPC.Equipment;

public sealed record EquipmentConfiguration(string ServerUrl, string SetupKey);
public sealed record EquipmentSelection(string Id, string Version);
public sealed record EquipmentRequest(bool Pack, bool Kiosk, long CatalogRevision, List<EquipmentSelection> Applications, bool Resume, bool AllowPartialPack = false, bool ResolveLatest = false);
public sealed record EquipmentEvent(string Kind, string Message, PackRun? Run = null, int? ExitCode = null,
    bool KioskVerified = false, bool RebootRequired = false, int? Percent = null);
public enum CatalogFailure { Unconfigured, Unauthorized, Forbidden, Timeout, Unavailable, Incompatible }
public sealed class CatalogException(CatalogFailure failure, string message) : Exception(message)
{
    public CatalogFailure Failure { get; } = failure;
}

/// <summary>The only catalogue supported by the equipment wizard. Never follows redirects with provisioning credentials.</summary>
public sealed class EquipmentCatalogClient(HttpClient http, EquipmentConfiguration configuration,
    Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    public async Task<PackCatalog> Load(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!Uri.TryCreate(configuration.ServerUrl.TrimEnd('/') + "/", UriKind.Absolute, out var server) ||
            (server.Scheme != "https" && !(server.Scheme == "http" && server.IsLoopback)) || !string.IsNullOrEmpty(server.UserInfo) ||
            !string.IsNullOrEmpty(server.Query) || !string.IsNullOrEmpty(server.Fragment) || string.IsNullOrWhiteSpace(configuration.SetupKey))
            throw new CatalogException(CatalogFailure.Unconfigured, "Este asistente no tiene un servidor de aprovisionamiento configurado.");
        for (int attempt = 0; ; attempt++)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(45));
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(server, "api/setup/v3/catalog"));
                request.Headers.Add("X-Setup-Key", configuration.SetupKey);
                using var response = await http.SendAsync(request, timeout.Token);
                if (response.StatusCode == HttpStatusCode.Unauthorized) throw new CatalogException(CatalogFailure.Unauthorized, "El panel ha rechazado la clave de aprovisionamiento (401). Publica un nuevo asistente.");
                if (response.StatusCode == HttpStatusCode.Forbidden) throw new CatalogException(CatalogFailure.Forbidden, "El panel no permite consultar el pack con este asistente (403).");
                if (response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
                    throw new HttpRequestException("Servidor temporalmente no disponible.");
                if (!response.IsSuccessStatusCode || !response.Headers.TryGetValues("X-Setup-Catalog-Version", out var versions) || !versions.SequenceEqual(["3"]))
                    throw new CatalogException(CatalogFailure.Incompatible, "El panel no ofrece un catálogo compatible v3. Actualiza el servidor y descarga el asistente nuevo.");
                var catalog = await response.Content.ReadFromJsonAsync<PackDefinition>(cancellationToken: timeout.Token);
                Validate(catalog);
                return catalog!.ForExecution();
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                if (attempt >= 2) throw new CatalogException(CatalogFailure.Timeout, "El panel no respondió en 45 segundos. Comprueba la conexión y reintenta.");
            }
            catch (HttpRequestException)
            {
                if (attempt >= 2) throw new CatalogException(CatalogFailure.Unavailable, "No se pudo conectar con el panel después de tres intentos.");
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
            { throw new CatalogException(CatalogFailure.Incompatible, "El panel devolvió un formato de catálogo incompatible."); }
            await (delay ?? Task.Delay)(TimeSpan.FromSeconds(attempt + 1), ct);
        }
    }

    public static void Validate(PackCatalog? catalog)
    {
        if (catalog?.Definition is { } definition)
        {
            Validate(definition);
            if (catalog.Revision != definition.Revision || catalog.Applications is null ||
                !PackDefinition.FromCatalog(catalog).Applications.SequenceEqual(definition.Applications))
                throw new CatalogException(CatalogFailure.Incompatible, "La definición y la selección del pack no coinciden.");
            return;
        }
        if (catalog is null || catalog.Revision < 0 || catalog.Applications is null || catalog.Applications.Count > 1000 ||
            catalog.Applications.Any(x => x is null || !Regex.IsMatch(x.Id ?? "", "^[a-f0-9]{32}$") ||
                string.IsNullOrWhiteSpace(x.WingetId) || string.IsNullOrWhiteSpace(x.DisplayName) || string.IsNullOrWhiteSpace(x.PinnedVersion)) ||
            catalog.Applications.Select(x => x.Id).Distinct().Count() != catalog.Applications.Count)
            throw new CatalogException(CatalogFailure.Incompatible, "El panel devolvió un formato de catálogo incompatible.");
    }
    public static void Validate(PackDefinition? definition)
    {
        if (definition is null || definition.Revision < 0 || definition.Applications is null || definition.Applications.Count > 1000 ||
            definition.Applications.Any(a => a is null || !Regex.IsMatch(a.Id ?? "", "^[a-f0-9]{32}$") ||
                !Regex.IsMatch(a.WingetId ?? "", "^[A-Za-z0-9][A-Za-z0-9._+-]{1,199}$") || string.IsNullOrWhiteSpace(a.DisplayName) || a.Order is < 0 or > 10000) ||
            definition.Applications.Select(a => a.Id).Distinct().Count() != definition.Applications.Count ||
            definition.Applications.Select(a => a.WingetId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != definition.Applications.Count)
            throw new CatalogException(CatalogFailure.Incompatible, "El panel devolvió una definición de pack incompatible.");
    }
}

public sealed class SelectionChangedException() : Exception("El pack cambió en el panel. Vuelve a cargarlo y revisa la selección antes de instalar.");
public sealed class NativeStateException(string message) : Exception(message);
public static class EquipmentPolicy
{
    public static void ValidateRequest(EquipmentRequest request)
    {
        if ((!request.Pack && !request.Kiosk) || request.Applications is null || request.Applications.Count > 1000 ||
            (request.Pack && request.Applications.Count == 0) || (!request.Pack && request.Applications.Count != 0) ||
            request.Applications.Any(x => x is null || !Regex.IsMatch(x.Id ?? "", "^[a-f0-9]{32}$") || (!request.ResolveLatest && string.IsNullOrWhiteSpace(x.Version))) ||
            request.Applications.Select(x => x.Id).Distinct().Count() != request.Applications.Count)
            throw new ArgumentException("Selecciona al menos un componente y, para el pack, al menos una aplicación.");
    }
    public static PackCatalog Snapshot(EquipmentRequest request, PackCatalog authorized)
    {
        ValidateRequest(request); EquipmentCatalogClient.Validate(authorized);
        if (request.ResolveLatest != (authorized.Definition is not null)) throw new SelectionChangedException();
        if (request.CatalogRevision != authorized.Revision || request.Applications.Any(s =>
            !authorized.Applications.Any(a => a.Id == s.Id && (request.ResolveLatest || a.PinnedVersion == s.Version)))) throw new SelectionChangedException();
        var apps = authorized.Applications.Where(a => request.Applications.Any(s => s.Id == a.Id)).OrderBy(a => a.Order).Select(a => a with { }).ToList();
        return new(authorized.Revision, apps, authorized.Definition is null ? null : new(authorized.Revision,
            apps.Select(a => new PackEntry(a.Id, a.WingetId, a.DisplayName, a.SelectedByDefault, a.Order)).ToList()));
    }
}

public interface IEquipmentPackSession : IAsyncDisposable
{
    Task<bool> Preflight(PackCatalog snapshot, bool resume, Action<EquipmentEvent> progress, CancellationToken ct, bool allowPartial = false);
    Task<bool> Install(Action<EquipmentEvent> progress, CancellationToken ct);
    bool RebootRequired { get; }
    PackRun? LastRun => null;
}
public interface IEquipmentKiosk
{
    Task<bool> InstallAndVerify(bool resume, Action<EquipmentEvent> progress, CancellationToken ct);
    bool RebootRequired { get; }
}
public sealed class EquipmentExecution(Func<CancellationToken, Task<PackCatalog>> loadCatalog,
    Func<IEquipmentPackSession> createPack, IEquipmentKiosk kiosk)
{
    public async Task<EquipmentEvent> Run(EquipmentRequest request, Action<EquipmentEvent> progress, CancellationToken ct)
    {
        bool kioskVerified = false, started = false, reboot = false;
        IEquipmentPackSession? pack = null;
        try
        {
            EquipmentPolicy.ValidateRequest(request);
            if (request.Pack)
            {
                progress(new("phase", "Validando la selección autorizada del pack…"));
                // Revalidation precedes extraction/bootstrap and every machine change.
                var snapshot = EquipmentPolicy.Snapshot(request, await loadCatalog(ct));
                pack = createPack();
                progress(new("phase", "Comprobando todas las aplicaciones seleccionadas…"));
                if (!await pack.Preflight(snapshot, request.Resume, progress, ct, request.AllowPartialPack))
                    return new("result", "Preparación detenida. Revisa los diagnósticos y cualquier instalador activo antes de reanudar.", Run: pack.LastRun, ExitCode: 1);
            }
            ct.ThrowIfCancellationRequested();
            if (request.Kiosk)
            {
                started = true;
                kioskVerified = await kiosk.InstallAndVerify(request.Resume, progress, ct);
                reboot = kiosk.RebootRequired;
                if (!kioskVerified) return new("result", "Kiosk no se ha podido verificar. Revisa los diagnósticos antes de reintentar.", Run: pack?.LastRun, ExitCode: 2, RebootRequired: reboot);
            }
            ct.ThrowIfCancellationRequested();
            if (pack != null)
            {
                started = true;
                bool complete = await pack.Install(progress, ct);
                reboot |= pack.RebootRequired;
                if (!complete) return new("result", "Quedan aplicaciones pendientes o sin verificar. Puedes reanudar tras revisar el resultado.", Run: pack.LastRun, ExitCode: 2, KioskVerified: kioskVerified, RebootRequired: reboot);
            }
            return new("result", "Todos los componentes seleccionados están verificados.", Run: pack?.LastRun, ExitCode: 0, KioskVerified: kioskVerified, RebootRequired: reboot);
        }
        catch (SelectionChangedException ex) { return new("review", ex.Message, ExitCode: 1); }
        catch (OperationCanceledException) { return new("result", "Operación cancelada. Un instalador nativo puede seguir activo; comprueba el estado antes de reanudar.", Run: pack?.LastRun, ExitCode: 2, KioskVerified: kioskVerified, RebootRequired: reboot); }
        catch (Exception ex)
        {
            progress(new("diagnostic", $"{ex.GetType().Name}; HRESULT {ex.HResult:X8}"));
            return new("result", ex is CatalogException or ArgumentException or NativeStateException ? ex.Message : "La operación falló. Consulta los diagnósticos de Setup.", Run: pack?.LastRun, ExitCode: started ? 2 : 1, KioskVerified: kioskVerified, RebootRequired: reboot);
        }
        finally { if (pack != null) await pack.DisposeAsync(); }
    }
}
