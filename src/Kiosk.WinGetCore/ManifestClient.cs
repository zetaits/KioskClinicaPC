using System.IO;
using System.Net;
using System.Net.Http;
using KioskClinicaPC.Core.Sync;

namespace Kiosk.SetupHelper;

/// <summary>Sequential, per-execution transport policy. A shared outage is paid for once per run.</summary>
internal sealed class ManifestClient(HttpClient http,
    Func<TimeSpan, CancellationToken, Task>? delay = null, TimeProvider? clock = null)
{
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private string? _unavailable;

    public async Task Validate(string id, string version, string architecture, string type, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        string directory = ManifestPolicy.RelativeDirectory(id, version);
        if (_unavailable is not null) throw new IOException(_unavailable);
        foreach (string name in new[] { id + ".installer.yaml", id + ".yaml" })
        {
            string url = "https://raw.githubusercontent.com/microsoft/winget-pkgs/master/" +
                string.Join('/', (directory + "/" + name).Split('/').Select(Uri.EscapeDataString));
            for (int attempt = 0; attempt < 3; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                string yaml;
                try
                {
                    using var response = await http.GetAsync(url, ct);
                    ct.ThrowIfCancellationRequested();
                    if (response.StatusCode == HttpStatusCode.NotFound) break;
                    if (Transient(response.StatusCode))
                    {
                        string reason = response.StatusCode == HttpStatusCode.TooManyRequests
                            ? "GitHub ha limitado temporalmente la consulta del manifiesto oficial (HTTP 429). Reintenta el pack más tarde."
                            : $"El repositorio de manifiestos oficiales no está disponible temporalmente (HTTP {(int)response.StatusCode}). Reintenta el pack más tarde.";
                        if (attempt == 2) throw Unavailable(reason);
                        var retry = response.Headers.RetryAfter;
                        var wait = retry?.Delta ?? (retry?.Date is { } date ? date - _clock.GetUtcNow() : TimeSpan.FromSeconds(5 * (attempt + 1)));
                        if (wait > TimeSpan.FromMinutes(1)) throw Unavailable(reason);
                        await _delay(wait > TimeSpan.Zero ? wait : TimeSpan.Zero, ct);
                        continue;
                    }
                    response.EnsureSuccessStatusCode();
                    yaml = await response.Content.ReadAsStringAsync(ct);
                }
                catch (HttpRequestException ex) when (ex.StatusCode is null || Transient(ex.StatusCode.Value))
                {
                    ct.ThrowIfCancellationRequested();
                    if (attempt == 2) throw Unavailable("No se pudo contactar con el repositorio de manifiestos oficiales después de tres intentos. Reintenta el pack más tarde.");
                    await _delay(TimeSpan.FromSeconds(attempt + 1), ct);
                    continue;
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    if (attempt == 2) throw Unavailable("El manifiesto oficial no respondió después de tres intentos. Reintenta el pack más tarde.");
                    await _delay(TimeSpan.FromSeconds(attempt + 1), ct);
                    continue;
                }
                ct.ThrowIfCancellationRequested();
                if (!ManifestPolicy.SupportsSilentMachine(yaml, architecture, type))
                    throw new InvalidDataException("El manifiesto oficial no declara instalación silenciosa machine-wide para el instalador aplicable.");
                return;
            }
        }
        throw new CatalogDriftException("El manifiesto oficial de esta versión ya no está disponible.");
    }

    private IOException Unavailable(string reason)
    {
        _unavailable = reason;
        return new IOException(reason);
    }
    private static bool Transient(HttpStatusCode status) => status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)status >= 500;
}
