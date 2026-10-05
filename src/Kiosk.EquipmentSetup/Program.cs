using System.IO;
using System.Text.Json;
using System.Windows;
using KioskClinicaPC.Equipment;

namespace Kiosk.EquipmentSetup;
internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            if (args is ["--diagnose"] or ["--diagnose-json"])
                return Diagnose(args[0] == "--diagnose").GetAwaiter().GetResult();
            if (args is ["--worker", var name] && System.Text.RegularExpressions.Regex.IsMatch(name, "^ClinicaPC\\.Equipment\\.[a-f0-9]{32}$"))
                return WorkerPipe.Work(name).GetAwaiter().GetResult();
            if (args.Length > 0)
            {
                var request = ParseSilent(args);
                if (request == null) return 64;
                MachineState.RequireElevated();
                return Silent(request).GetAwaiter().GetResult();
            }
            var app = new Application();
            return app.Run(new MainWindow());
        }
        catch { return 1; }
    }
    internal static EquipmentRequest? ParseSilent(string[] args)
    {
        var normalized = args.Select(a => a.ToUpperInvariant()).ToArray();
        if (!new[] { "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART" }.All(normalized.Contains) ||
            normalized.Distinct().Count() != normalized.Length || normalized.Any(a => a is not ("/VERYSILENT" or "/SUPPRESSMSGBOXES" or "/NORESTART" or "/RESUME") && !a.StartsWith("/COMPONENTS=")) ||
            normalized.Count(a => a.StartsWith("/COMPONENTS=")) > 1) return null;
        string components = normalized.SingleOrDefault(a => a.StartsWith("/COMPONENTS="))?[12..] ?? "PACK";
        var values = components.Split(',');
        if (values.Length == 0 || values.Distinct().Count() != values.Length || values.Any(v => v is not ("PACK" or "KIOSK"))) return null;
        return new(values.Contains("PACK"), values.Contains("KIOSK"), 0, [], normalized.Contains("/RESUME"));
    }
    private static async Task<int> Silent(EquipmentRequest request)
    {
        string work = MachineState.Prepare();
        using var log = new StreamWriter(Path.Combine(MachineState.Root, "logs", "silent-" + Path.GetFileName(work) + ".log")) { AutoFlush = true };
        log.WriteLine($"{DateTime.UtcNow:O} Inicio del modo silencioso; asistente {Payload.Manifest.AssistantVersion}");
        try
        {
            using var http = Coordinator.CreateHttp();
            if (request.Pack)
            {
                var catalog = await new EquipmentCatalogClient(http, Payload.Configuration).Load(CancellationToken.None);
                request = request with { CatalogRevision = catalog.Revision, Applications = catalog.Applications.Where(a => a.SelectedByDefault).Select(a => new EquipmentSelection(a.Id, a.PinnedVersion)).ToList() };
            }
            var result = await Coordinator.Run(request, _ => { }, CancellationToken.None);
            if (result.KioskVerified)
            {
                try { await KioskPayload.RegisterAutostart(); }
                catch { log.WriteLine("Autostart del usuario no verificado."); return 2; }
            }
            log.WriteLine($"{DateTime.UtcNow:O} Resultado {result.ExitCode}");
            return result.ExitCode ?? 1;
        }
        catch (Exception ex) { log.WriteLine($"{DateTime.UtcNow:O} {ex.GetType().Name}; HRESULT {ex.HResult:X8}"); return 1; }
    }
    private static async Task<int> Diagnose(bool showWindow)
    {
        var manifest = Payload.Manifest;
        bool compatible = Payload.Compatible();
        bool worker = false, kiosk = false;
        try { worker = await Payload.Verify("worker.zip", manifest.WorkerSha256, CancellationToken.None); } catch (InvalidDataException) { }
        if (worker) worker = Payload.WorkerMetadataPresent();
        try { kiosk = await Payload.Verify("kiosk.exe", manifest.KioskSha256, CancellationToken.None); } catch (InvalidDataException) { }
        var info = new { manifest.SchemaVersion, manifest.CatalogApiVersion, manifest.InstallerKind, manifest.AssistantVersion, manifest.WorkerVersion, manifest.KioskVersion, manifest.SourceCommit, compatible, workerResourceVerified = worker, kioskResourceVerified = kiosk };
        string text = JsonSerializer.Serialize(info, Payload.Json);
        if (showWindow) MessageBox.Show(text, "Diagnóstico del asistente");
        else Console.WriteLine(text);
        return compatible && worker && kiosk ? 0 : 1;
    }
}
