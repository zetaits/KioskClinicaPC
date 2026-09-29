using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.IO;
using KioskClinicaPC.Core.Config;
using KioskClinicaPC.Core.Sync;
using Serilog;

namespace KioskClinicaPC.Services;

/// <summary>Consulta la versión objetivo decidida por la VPS y delega su preparación en el agente SYSTEM.</summary>
public static class UpdateService
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static Timer? _timer;
    private static KioskSettings? _settings;

    public enum UpdateOutcome { UpToDate, Staged, Failed }
    public readonly record struct UpdateResult(UpdateOutcome Outcome, string? LatestVersion, string? Error);
    public static Version CurrentVersion => Normalize(Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0));

    public static void Start(KioskSettings settings)
    {
        _settings = settings;
        _timer?.Dispose();
        _timer = new Timer(_ => _ = CheckAndStageAsync(settings), null, TimeSpan.Zero, TimeSpan.FromMinutes(15));
    }

    public static Task<UpdateResult> CheckAndStageAsync() => _settings == null
        ? Task.FromResult(new UpdateResult(UpdateOutcome.Failed, null, "El servicio de actualizaciones todavía no está iniciado."))
        : CheckAndStageAsync(_settings);

    public static async Task<UpdateResult> CheckAndStageAsync(KioskSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ServerUrl) || string.IsNullOrWhiteSpace(settings.DeviceId))
            return new(UpdateOutcome.Failed, null, "El kiosco no está conectado a la VPS.");
        if (!await Gate.WaitAsync(0)) return new(UpdateOutcome.UpToDate, null, null);
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri(settings.ServerUrl.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(20) };
            if (!string.IsNullOrWhiteSpace(settings.ServerApiKey)) http.DefaultRequestHeaders.Add("X-Api-Key", settings.ServerApiKey);
            string url = "api/updates/assignment?deviceId=" + Uri.EscapeDataString(settings.DeviceId) +
                         "&currentVersion=" + Uri.EscapeDataString(CurrentVersion.ToString()) +
                         "&deviceName=" + Uri.EscapeDataString(settings.DeviceName ?? Environment.MachineName);
            using var response = await http.GetAsync(url);
            if (response.StatusCode == HttpStatusCode.NoContent) return new(UpdateOutcome.UpToDate, null, null);
            response.EnsureSuccessStatusCode();
            var assignment = await response.Content.ReadFromJsonAsync<KioskUpdateAssignment>(
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
            if (assignment == null) throw new InvalidDataException("La asignación de actualización está vacía.");
            InstallerAgentResponse staged = await InstallerAgentClient.StageKioskUpdateAsync(
                settings.ServerUrl, settings.ServerApiKey, settings.DeviceId, assignment);
            if (!staged.Accepted) throw new InvalidOperationException(staged.Error ?? "El agente rechazó la actualización.");
            Log.Information("Actualización {Version} entregada al agente privilegiado.", assignment.Version);
            return new(UpdateOutcome.Staged, assignment.Version, null);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "No se pudo consultar o preparar la actualización administrada.");
            return new(UpdateOutcome.Failed, null, ex.Message);
        }
        finally { Gate.Release(); }
    }

    private static Version Normalize(Version v) => new(Math.Max(v.Major, 0), Math.Max(v.Minor, 0), Math.Max(v.Build, 0));
}
