using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using KioskClinicaPC.Core.Sync;

namespace KioskClinicaPC.Equipment;

public sealed record EquipmentConfiguration(string ServerUrl, string SetupKey);
public sealed record EquipmentSelection(string Id, string Version);
public sealed record EquipmentRequest(bool Pack, bool Kiosk, long CatalogRevision, List<EquipmentSelection> Applications, bool Resume);
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
                using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(server, "api/setup/v2/catalog"));
                request.Headers.Add("X-Setup-Key", configuration.SetupKey);
                using var response = await http.SendAsync(request, timeout.Token);
                if (response.StatusCode == HttpStatusCode.Unauthorized) throw new CatalogException(CatalogFailure.Unauthorized, "El panel ha rechazado la clave de aprovisionamiento (401). Publica un nuevo asistente.");
                if (response.StatusCode == HttpStatusCode.Forbidden) throw new CatalogException(CatalogFailure.Forbidden, "El panel no permite consultar el pack con este asistente (403).");
                if (response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
                    throw new HttpRequestException("Servidor temporalmente no disponible.");
                if (!response.IsSuccessStatusCode || !response.Headers.TryGetValues("X-Setup-Catalog-Version", out var versions) || !versions.SequenceEqual(["2"]))
                    throw new CatalogException(CatalogFailure.Incompatible, "El panel no ofrece un catálogo compatible v2. Actualiza el servidor y publica el asistente WPF.");
                var catalog = await response.Content.ReadFromJsonAsync<PackCatalog>(cancellationToken: timeout.Token);
                Validate(catalog);
                return catalog!;
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
        if (catalog is null || catalog.Revision < 0 || catalog.Applications is null || catalog.Applications.Count > 1000 ||
            catalog.Applications.Any(x => x is null || !Regex.IsMatch(x.Id ?? "", "^[a-f0-9]{32}$") ||
                string.IsNullOrWhiteSpace(x.WingetId) || string.IsNullOrWhiteSpace(x.DisplayName) || string.IsNullOrWhiteSpace(x.PinnedVersion)) ||
            catalog.Applications.Select(x => x.Id).Distinct().Count() != catalog.Applications.Count)
            throw new CatalogException(CatalogFailure.Incompatible, "El panel devolvió un formato de catálogo incompatible.");
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
            request.Applications.Any(x => x is null || !Regex.IsMatch(x.Id ?? "", "^[a-f0-9]{32}$") || string.IsNullOrWhiteSpace(x.Version)) ||
            request.Applications.Select(x => x.Id).Distinct().Count() != request.Applications.Count)
            throw new ArgumentException("Selecciona al menos un componente y, para el pack, al menos una aplicación.");
    }
    public static PackCatalog Snapshot(EquipmentRequest request, PackCatalog authorized)
    {
        ValidateRequest(request); EquipmentCatalogClient.Validate(authorized);
        if (request.CatalogRevision != authorized.Revision || request.Applications.Any(s =>
            !authorized.Applications.Any(a => a.Id == s.Id && a.PinnedVersion == s.Version))) throw new SelectionChangedException();
        return new(authorized.Revision, authorized.Applications.Where(a => request.Applications.Any(s => s.Id == a.Id))
            .OrderBy(a => a.Order).Select(a => a with { }).ToList());
    }
}

public interface IEquipmentPackSession : IAsyncDisposable
{
    Task<bool> Preflight(PackCatalog snapshot, bool resume, Action<EquipmentEvent> progress, CancellationToken ct);
    Task<bool> Install(Action<EquipmentEvent> progress, CancellationToken ct);
    bool RebootRequired { get; }
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
                progress(new("phase", "Validando la revisión y las versiones del pack…"));
                // Revalidation precedes extraction/bootstrap and every machine change.
                var snapshot = EquipmentPolicy.Snapshot(request, await loadCatalog(ct));
                pack = createPack();
                progress(new("phase", "Comprobando todas las aplicaciones seleccionadas…"));
                if (!await pack.Preflight(snapshot, request.Resume, progress, ct))
                    return new("result", "Comprobación previa fallida. Revisa las aplicaciones indicadas.", ExitCode: 1);
            }
            ct.ThrowIfCancellationRequested();
            if (request.Kiosk)
            {
                started = true;
                kioskVerified = await kiosk.InstallAndVerify(request.Resume, progress, ct);
                reboot = kiosk.RebootRequired;
                if (!kioskVerified) return new("result", "Kiosk no se ha podido verificar. Revisa los diagnósticos antes de reintentar.", ExitCode: 2, RebootRequired: reboot);
            }
            ct.ThrowIfCancellationRequested();
            if (pack != null)
            {
                started = true;
                bool complete = await pack.Install(progress, ct);
                reboot |= pack.RebootRequired;
                if (!complete) return new("result", "Quedan aplicaciones pendientes o sin verificar. Puedes reanudar tras revisar el resultado.", ExitCode: 2, KioskVerified: kioskVerified, RebootRequired: reboot);
            }
            return new("result", "Todos los componentes seleccionados están verificados.", ExitCode: 0, KioskVerified: kioskVerified, RebootRequired: reboot);
        }
        catch (SelectionChangedException ex) { return new("review", ex.Message, ExitCode: 1); }
        catch (OperationCanceledException) { return new("result", "Operación cancelada. Un instalador nativo puede seguir activo; comprueba el estado antes de reanudar.", ExitCode: 2, KioskVerified: kioskVerified, RebootRequired: reboot); }
        catch (Exception ex)
        {
            progress(new("diagnostic", $"{ex.GetType().Name}; HRESULT {ex.HResult:X8}"));
            return new("result", ex is CatalogException or ArgumentException or NativeStateException ? ex.Message : "La operación falló. Consulta los diagnósticos de Setup.", ExitCode: started ? 2 : 1, KioskVerified: kioskVerified, RebootRequired: reboot);
        }
        finally { if (pack != null) await pack.DisposeAsync(); }
    }
}
