using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using static Kiosk.SetupHelper.PackState;

using KioskClinicaPC.Core.Sync;

namespace Kiosk.SetupHelper;

internal static class LegacyCommands
{
    [STAThread]
    public static int Run(string[] args, Func<Ini, string, int>? showProgress = null)
    {
        if (args is ["--diagnose-json"])
        {
            Console.WriteLine(JsonSerializer.Serialize(new { workerVersion = typeof(LegacyCommands).Assembly.GetName().Version?.ToString(3), catalogApiVersion = 3,
                componentProtocolVersion = 1, wpf = typeof(LegacyCommands).Assembly.GetReferencedAssemblies().Any(a => a.Name == "PresentationFramework") }, Json));
            return 0;
        }
        if (args is ["equipment-worker"]) return EquipmentWorker.Run().GetAwaiter().GetResult();
        if (args.Length < 2) return 64;
        if (args[0] == "inspect" && args.Length == 3)
        {
            // Read-only diagnostics: never bootstrap or install applications on the development PC.
            try
            {
                var item = Task.Run(() => new WinGetEngine().Inspect(args[1], args[2])).WaitAsync(TimeSpan.FromMinutes(5)).GetAwaiter().GetResult();
                Console.WriteLine($"{item.Application.WingetId} {item.Application.PinnedVersion}: {item.Message}"); return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex.ToString()); return 1; }
        }
        if (args[0] == "export-index")
        {
            if (args.Length != 3) return 64;
            ExportDiagnostics? diagnostics = null;
            try
            {
                diagnostics = new ExportDiagnostics(args[1]);
                diagnostics.Info($"Runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}; " +
                    $"OS: {System.Runtime.InteropServices.RuntimeInformation.OSDescription}; " +
                    $"architecture: {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");
                diagnostics.Phase("Checking official manifest repository");
                if (!Directory.Exists(Path.Combine(args[2], "manifests")))
                    throw new DirectoryNotFoundException("The official manifest repository has no manifests directory.");
                var index = Task.Run(() => new WinGetEngine(diagnostics.Phase, diagnostics.Info).Export(args[2])).GetAwaiter().GetResult();
                diagnostics.Phase("Writing catalogue JSON");
                File.WriteAllText(args[1], JsonSerializer.Serialize(index, Json));
                diagnostics.Info($"Export complete: {index.Applications.Count} applications; {index.Applications.Count(x => x.Eligible)} eligible.");
                return 0;
            }
            catch (Exception ex)
            {
                if (diagnostics != null) diagnostics.Failure(ex);
                else Console.Error.WriteLine($"Export diagnostics initialization failed: {ExportDiagnostics.Describe(ex)}\n{ex}");
                return 1;
            }
            finally { diagnostics?.Dispose(); }
        }
        if (args.Length != 3 || args[0] is not ("catalog" or "pending-catalog" or "preflight" or "install" or "install-silent")) return 64;
        try
        {
            LocalState.Prepare();
            var request = Ini.Read(args[1]);
            if (args[0] == "pending-catalog")
            {
                var pending = ReadPending();
                if (pending == null) { Ini.Write(args[2], new() { ["Result"] = new() { ["Ok"] = "0" } }); return 0; }
                WriteCatalog(new(pending.CatalogRevision, pending.Items.Select(x => x.Application with { SelectedByDefault = true }).ToList()), args[2]); return 0;
            }
            if (args[0] == "catalog") return Task.Run(() => Catalog(request, args[2])).GetAwaiter().GetResult();
            if (args[0] == "install")
            {
                return showProgress?.Invoke(request, args[2]) ?? 64;
            }
            return Task.Run(() => Execute(request, args[2], null, args[0] == "preflight", CancellationToken.None)).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Ini.Write(args[2], new() { ["Result"] = new() { ["Ok"] = "0", ["Error"] = ex.Message } }); return 1;
        }
    }
    private static async Task<int> Catalog(Ini request, string output)
    {
        string url = request.Get("Setup", "ServerUrl");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)))
            throw new InvalidDataException("El catálogo exige HTTPS.");
        using var http = new HttpClient { BaseAddress = new Uri(url.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(45) };
        http.DefaultRequestHeaders.Add("X-Setup-Key", request.Get("Setup", "SetupKey"));
        PackCatalog? catalog = null;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try { catalog = await http.GetFromJsonAsync<PackCatalog>("api/setup/v2/catalog", Json); break; }
            catch (HttpRequestException ex) when (attempt < 2 && (ex.StatusCode == null || (int)ex.StatusCode >= 500)) { await Task.Delay(1000 * (attempt + 1)); }
        }
        if (catalog == null) throw new InvalidDataException("Catálogo vacío.");
        WriteCatalog(catalog, output); return 0;
    }
    private static void WriteCatalog(PackCatalog catalog, string output)
    {
        // Snapshot is beside the request, not fetched again during installation. Contains no credentials.
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!, "pack-snapshot.json"), JsonSerializer.Serialize(catalog, Json));
        var sections = new Dictionary<string, Dictionary<string, string>> { ["Result"] = new() { ["Ok"] = "1", ["Count"] = catalog.Applications.Count.ToString() } };
        int i = 0;
        foreach (var app in catalog.Applications.OrderBy(x => x.Order))
            sections[$"Package{i++}"] = new() { ["Id"] = app.Id, ["Name"] = app.DisplayName + " · " + app.PinnedVersion, ["Default"] = app.SelectedByDefault ? "1" : "0" };
        Ini.Write(output, sections);
    }
    internal static async Task<int> Execute(Ini request, string output, Action<PackRun>? changed, bool preflight, CancellationToken ct, PackRun? resume = null)
    {
        // Cross-process exclusion prevents competing installs and state corruption. Never overwrite another run.
        await using var lease = new FileStream(Path.Combine(StateRoot, "run.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var catalog = JsonSerializer.Deserialize<PackCatalog>(File.ReadAllText(request.Get("Setup", "Snapshot")), Json) ?? throw new InvalidDataException("Falta snapshot.");
        var ids = Enumerable.Range(0, request.GetInt("Selection", "Count")).Select(i => request.Get("Selection", $"Id{i}")).ToHashSet();
        if (ids.Count == 0 || ids.Any(id => !catalog.Applications.Any(x => x.Id == id))) throw new InvalidDataException("Selección de aplicaciones no válida.");
        var previous = ReadPending();
        if (resume == null && request.Get("Setup", "Resume") == "1" && previous != null)
        {
            resume = previous;
            resume.Items = resume.Items.Where(x => ids.Contains(x.Application.Id)).ToList();
        }
        var run = resume ?? new PackRun { CatalogRevision = catalog.Revision, Items = catalog.Applications.Where(x => ids.Contains(x.Id)).OrderBy(x => x.Order).Select(x => new PackItemResult { Application = x }).ToList() };
        if (run.Items.Count == 0 || run.Items.Any(x => !System.Text.RegularExpressions.Regex.IsMatch(x.Application.Id, "^[a-f0-9]{32}$")))
            throw new InvalidDataException("El estado contiene identificadores de aplicaciones no válidos.");
        if (resume == null && previous != null)
        {
            // A new selection must not bypass the safety guard for an interrupted native installer.
            foreach (var item in run.Items)
            {
                var old = previous.Items.Find(x => x.Application.WingetId == item.Application.WingetId &&
                    (x.RequiresRebootBeforeRetry || x.State is PackItemState.Installing or PackItemState.VerificationPending));
                if (old == null) continue;
                item.RequiresRebootBeforeRetry = true; item.LastAttemptBootTimeUtc = old.LastAttemptBootTimeUtc ?? previous.HostBootTimeUtc;
            }
        }
        string logs = Path.Combine(StateRoot, "logs", run.StartedAtUtc.ToString("yyyyMMdd-HHmmss") + "-" + DateTime.UtcNow.ToString("HHmmss-fff")); Directory.CreateDirectory(logs);
        var engine = await Bootstrap.CreateEngine(message => { foreach (var item in run.Items) item.Message = message; changed?.Invoke(run); }, ct);
        bool ok = await PackExecution.Run(run, engine, logs, changed, Save, preflight, ct);
        Ini.Write(output, new() { ["Result"] = new() { ["Ok"] = ok ? "1" : "0", ["RebootRequired"] = run.RebootRequired ? "1" : "0",
            ["Summary"] = (preflight ? (!ok ? "Comprobación previa fallida; desmarca las aplicaciones indicadas y vuelve a comprobar." : "Comprobación previa correcta.") : run.Summary) + "\n" + string.Join("\n", run.Items.Select(x => $"{x.Application.DisplayName}: {x.Message}")) } });
        return ok ? 0 : 2;
    }
}
