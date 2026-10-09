using System.IO;
using System.Net.Http;
using KioskClinicaPC.Equipment;

namespace Kiosk.EquipmentSetup;
internal static class Coordinator
{
    internal static HttpClient CreateHttp() => new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
    internal static async Task<EquipmentEvent> Run(EquipmentRequest request, Action<EquipmentEvent> progress, CancellationToken ct)
    {
        Payload.UseAssembly(typeof(Coordinator).Assembly);
        string work;
        try { work = MachineState.Prepare(); }
        catch { return new("result", "El trabajador necesita un administrador interactivo y carpetas de estado seguras.", ExitCode: 1); }
        using var log = new StreamWriter(Path.Combine(MachineState.Root, "logs", "equipment-" + Path.GetFileName(work) + ".log")) { AutoFlush = true };
        void Diagnostic(string message) { lock (log) log.WriteLine($"{DateTime.UtcNow:O} {message}"); }
        void Report(EquipmentEvent value)
        {
            // Never serialize requests, embedded configuration, HTTP headers or credentials.
            Diagnostic(EquipmentDiagnostics.FormatEvent(value)); progress(value);
        }
        Diagnostic($"Assistant {Payload.Manifest.AssistantVersion}; worker {Payload.Manifest.WorkerVersion}; Kiosk {Payload.Manifest.KioskVersion}; source {Payload.Manifest.SourceCommit}");
        try
        {
            if (!Payload.Compatible()) throw new InvalidDataException("Manifiesto de recursos incompatible.");
            using var equipmentLease = SetupLease.Equipment(MachineState.Root, request.Pack);
            // Pack worker holds the legacy lock throughout preflight, Kiosk and pack installation.
            using var http = CreateHttp();
            var catalog = new EquipmentCatalogClient(http, Payload.Configuration);
            var execution = new EquipmentExecution(catalog.Load, () => new PackSession(work, Diagnostic), new KioskPayload(work),
                (selection, report, token) => Payload.Prepare(selection, work, http, report, token));
            var result = await execution.Run(request, Report, ct);
            Diagnostic(EquipmentDiagnostics.FormatEvent(result));
            Diagnostic($"Result {result.ExitCode}; kiosk verified {result.KioskVerified}; reboot {result.RebootRequired}");
            return result;
        }
        catch (Exception ex)
        {
            Diagnostic($"Coordinator: {EquipmentDiagnostics.Describe(ex)}");
            return new("result", ex is IOException ? "Otra instalación puede estar activa o el estado no es accesible. Consulta los diagnósticos de Setup." : "No se pudo iniciar la operación. Consulta los diagnósticos de Setup.", ExitCode: 1);
        }
    }
}
