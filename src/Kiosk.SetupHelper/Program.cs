using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Kiosk.InstallerCore;
using KioskClinicaPC.Core.Sync;

namespace Kiosk.SetupHelper;

internal static class Program
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly string StateRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ClinicaPC", "Setup");
    private static readonly string LedgerPath = Path.Combine(StateRoot, "applied-packages.json");

    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 3 || args[0] is not ("catalog" or "install")) return 64;
        try
        {
            Directory.CreateDirectory(StateRoot);
            var request = Ini.Read(args[1]);
            ValidateServer(request.Get("Setup", "ServerUrl"));
            return args[0] == "catalog"
                ? await Catalog(request, args[2])
                : await Install(request, args[2]);
        }
        catch (Exception ex)
        {
            Ini.Write(args[2], new Dictionary<string, Dictionary<string, string>>
            {
                ["Result"] = new() { ["Ok"] = "0", ["Error"] = Safe(ex.Message) }
            });
            return 1;
        }
    }

    private static async Task<int> Catalog(Ini request, string output)
    {
        using HttpClient http = CreateHttp(request);
        var catalog = await http.GetFromJsonAsync<InitialSetupCatalog>("api/setup/catalog", Json)
            ?? throw new InvalidDataException("El servidor devolvi\u00f3 un cat\u00e1logo vac\u00edo.");
        var applied = LoadLedger();
        var result = new Dictionary<string, Dictionary<string, string>>
        {
            ["Result"] = new() { ["Ok"] = "1", ["Count"] = catalog.Packages.Count.ToString() }
        };
        int index = 0;
        foreach (InitialSetupPackage package in catalog.Packages.OrderBy(p => p.Order).ThenBy(p => p.DisplayName))
        {
            bool wasApplied = applied.Any(a => a.PackageId == package.Id && a.Sha256.Equals(package.Sha256, StringComparison.OrdinalIgnoreCase));
            result[$"Package{index++}"] = new()
            {
                ["Id"] = package.Id, ["Name"] = Safe(package.DisplayName), ["SizeBytes"] = package.SizeBytes.ToString(),
                ["Sha256"] = package.Sha256, ["Default"] = package.SelectedByDefault ? "1" : "0", ["Applied"] = wasApplied ? "1" : "0"
            };
        }
        Ini.Write(output, result);
        return 0;
    }

    private static async Task<int> Install(Ini request, string output)
    {
        int count = request.GetInt("Selection", "Count");
        var ids = Enumerable.Range(0, count).Select(i => request.Get("Selection", $"Id{i}"))
            .Where(id => !string.IsNullOrWhiteSpace(id)).ToList();
        if (ids.Count == 0) throw new InvalidDataException("No se seleccionaron aplicaciones.");

        using HttpClient http = CreateHttp(request);
        var create = new InitialSetupSessionRequest
        {
            MachineName = Environment.MachineName,
            SetupVersion = request.Get("Setup", "Version"),
            PackageIds = ids
        };
        using HttpResponseMessage createdResponse = await http.PostAsJsonAsync("api/setup/sessions", create, Json);
        createdResponse.EnsureSuccessStatusCode();
        var session = await createdResponse.Content.ReadFromJsonAsync<InitialSetupSessionResponse>(Json)
            ?? throw new InvalidDataException("El servidor no cre\u00f3 la sesi\u00f3n de instalaci\u00f3n.");
        http.DefaultRequestHeaders.Add("X-Setup-Token", session.Token);

        var lines = new List<string>();
        var ledger = LoadLedger();
        int successes = 0, failures = 0;
        bool reboot = false;
        string work = Path.Combine(StateRoot, "staging", session.SessionId);
        Directory.CreateDirectory(work);
        try
        {
            foreach (InstallationManifest package in session.Packages)
            {
                string file = Path.Combine(work, package.PackageId + (package.Kind == InstallerPackageKind.Msi ? ".msi" : ".exe"));
                try
                {
                    await Report(http, session.SessionId, package.PackageId, InstallationJobState.Downloading, 0, null, null);
                    await PackageInstallation.DownloadResumable(http,
                        $"api/setup/sessions/{session.SessionId}/packages/{package.PackageId}/download", file, package.SizeBytes,
                        p => Report(http, session.SessionId, package.PackageId, InstallationJobState.Downloading, p, null, null), CancellationToken.None);
                    await Report(http, session.SessionId, package.PackageId, InstallationJobState.Verifying, 100, null, null);
                    PackageInstallation.Verify(file, package.SizeBytes, package.Sha256, package.AllowUnsigned);
                    await Report(http, session.SessionId, package.PackageId, InstallationJobState.Installing, 100, null, null);
                    int exit = await PackageInstallation.Execute(file, package.Kind, CancellationToken.None);
                    bool needsReboot = exit is 1641 or 3010;
                    if (exit != 0 && !needsReboot) throw new InstallerExitException(exit);
                    reboot |= needsReboot;
                    successes++;
                    lines.Add($"{package.DisplayName}: instalada{(needsReboot ? " (requiere reinicio)" : "")}");
                    ledger.RemoveAll(a => a.PackageId == package.PackageId);
                    ledger.Add(new AppliedPackage(package.PackageId, package.Sha256, package.DisplayName, DateTime.UtcNow));
                    SaveLedger(ledger);
                    await Report(http, session.SessionId, package.PackageId,
                        needsReboot ? InstallationJobState.RebootRequired : InstallationJobState.Succeeded,
                        100, exit, needsReboot ? "Instalada; Windows solicita reiniciar." : "Instalada.");
                }
                catch (Exception ex)
                {
                    failures++;
                    lines.Add($"{package.DisplayName}: ERROR - {Safe(ex.Message)}");
                    int? exit = ex is InstallerExitException ie ? ie.ExitCode : null;
                    await TryReport(http, session.SessionId, package.PackageId, InstallationJobState.Failed, null, exit, ex.Message);
                }
                finally { try { if (File.Exists(file)) File.Delete(file); } catch { } }
            }
        }
        finally { try { Directory.Delete(work, true); } catch { } }

        Ini.Write(output, new Dictionary<string, Dictionary<string, string>>
        {
            ["Result"] = new()
            {
                ["Ok"] = failures == 0 ? "1" : "0", ["SuccessCount"] = successes.ToString(),
                ["FailureCount"] = failures.ToString(), ["RebootRequired"] = reboot ? "1" : "0",
                ["Summary"] = Safe(string.Join("\n", lines))
            }
        });
        return failures == 0 ? 0 : 2;
    }

    private static HttpClient CreateHttp(Ini request)
    {
        var http = new HttpClient { BaseAddress = new Uri(request.Get("Setup", "ServerUrl").TrimEnd('/') + "/"), Timeout = TimeSpan.FromMinutes(20) };
        http.DefaultRequestHeaders.Add("X-Setup-Key", request.Get("Setup", "SetupKey"));
        return http;
    }

    private static void ValidateServer(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)) throw new InvalidDataException("URL de servidor no v\u00e1lida.");
        bool local = uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback;
        if (uri.Scheme != Uri.UriSchemeHttps && !local) throw new InvalidDataException("El Setup exige HTTPS salvo en localhost.");
    }

    private static Task Report(HttpClient http, string session, string package, InstallationJobState state,
        int? progress, int? exit, string? message) => SendStatus(http, session, package,
            new InitialSetupStatusUpdate { State = state, ProgressPercent = progress, ExitCode = exit, Message = message });

    private static async Task SendStatus(HttpClient http, string session, string package, InitialSetupStatusUpdate update)
    {
        using var response = await http.PostAsJsonAsync($"api/setup/sessions/{session}/packages/{package}/status", update, Json);
        response.EnsureSuccessStatusCode();
    }

    private static async Task TryReport(HttpClient http, string session, string package, InstallationJobState state,
        int? progress, int? exit, string? message)
    {
        try { await Report(http, session, package, state, progress, exit, message); } catch { }
    }

    private static List<AppliedPackage> LoadLedger()
    {
        try { return JsonSerializer.Deserialize<List<AppliedPackage>>(File.ReadAllText(LedgerPath), Json) ?? new(); }
        catch { return new(); }
    }

    private static void SaveLedger(List<AppliedPackage> ledger)
    {
        string tmp = LedgerPath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(ledger, Json));
        File.Move(tmp, LedgerPath, true);
    }

    private static string Safe(string value) => value.Replace("\r", " ").Replace("\n", "\\n").Trim();
    private sealed record AppliedPackage(string PackageId, string Sha256, string DisplayName, DateTime AppliedAtUtc);
}

internal sealed class Ini
{
    private readonly Dictionary<string, Dictionary<string, string>> _sections = new(StringComparer.OrdinalIgnoreCase);
    public string Get(string section, string key) =>
        _sections.TryGetValue(section, out var values) && values.TryGetValue(key, out string? value) ? value : "";
    public int GetInt(string section, string key) => int.TryParse(Get(section, key), out int value) ? value : 0;

    public static Ini Read(string path)
    {
        var ini = new Ini();
        string section = "";
        foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';')) continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1];
                ini._sections.TryAdd(section, new(StringComparer.OrdinalIgnoreCase));
                continue;
            }
            int equals = line.IndexOf('=');
            if (equals > 0 && ini._sections.TryGetValue(section, out var values)) values[line[..equals].Trim()] = line[(equals + 1)..].Trim();
        }
        return ini;
    }

    public static void Write(string path, Dictionary<string, Dictionary<string, string>> sections)
    {
        // UTF-16 con BOM para que las funciones INI nativas que usa Inno Setup conserven acentos.
        using var writer = new StreamWriter(path, false, Encoding.Unicode);
        foreach (var section in sections)
        {
            writer.WriteLine($"[{section.Key}]");
            foreach (var pair in section.Value) writer.WriteLine($"{pair.Key}={pair.Value}");
            writer.WriteLine();
        }
    }
}
