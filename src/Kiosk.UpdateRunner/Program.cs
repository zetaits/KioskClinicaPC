using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KioskClinicaPC.Core.Sync;

internal static class Program
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly string PendingRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "KioskClinicaPC", "kiosk-updates");

    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length >= 2 && args[0].Equals("apply", StringComparison.OrdinalIgnoreCase))
            {
                using var mutex = new Mutex(false, @"Global\KioskClinicaPC_UpdateRunner");
                if (!mutex.WaitOne(0)) return 0;
                try { return await Apply(args[1]); }
                finally { mutex.ReleaseMutex(); }
            }
            return Scan();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("KioskUpdateRunner: " + ex);
            return 1;
        }
    }

    private static int Scan()
    {
        if (!Directory.Exists(PendingRoot)) return 0;
        foreach (string requestPath in Directory.EnumerateFiles(PendingRoot, "request.json", SearchOption.AllDirectories)
                     .OrderBy(File.GetLastWriteTimeUtc))
        {
            KioskUpdateRunnerRequest? request = ReadRequest(requestPath);
            if (request == null) continue;
            string folder = Path.GetDirectoryName(requestPath)!;
            string activeRunner = Path.Combine(folder, "runner-active.exe");
            File.Copy(Environment.ProcessPath!, activeRunner, true);
            Process.Start(new ProcessStartInfo(activeRunner, $"apply \"{requestPath}\"")
            { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = folder });
            return 0;
        }
        return 0;
    }

    private static async Task<int> Apply(string requestPath)
    {
        KioskUpdateRunnerRequest request = ReadRequest(requestPath)
            ?? throw new InvalidDataException("Trabajo de actualización no válido.");
        using var http = CreateHttp(request);
        bool kioskStopped = false;
        try
        {
            ValidateLocalPaths(requestPath, request);
            using var auth = await http.GetAsync($"api/updates/{request.Assignment.JobId}/authorize");
            if (auth.StatusCode == HttpStatusCode.NotFound)
            {
                CompleteLocalRequest(requestPath);
                return 0;
            }
            auth.EnsureSuccessStatusCode();
            var live = await auth.Content.ReadFromJsonAsync<KioskUpdateAssignment>(Json)
                ?? throw new InvalidDataException("Autorización de actualización vacía.");
            request.Assignment = live;
            await File.WriteAllTextAsync(requestPath, JsonSerializer.Serialize(request, Json));
            if (DateTime.UtcNow < live.InstallAfterUtc || DateTime.UtcNow >= live.InstallBeforeUtc) return 0;

            // La lectura completa del Setup se hace una sola vez, ya dentro de la ventana, pero siempre
            // antes de parar el kiosco o ejecutar código del paquete.
            byte[] manifestBytes = await File.ReadAllBytesAsync(request.ManifestPath);
            string signature = await File.ReadAllTextAsync(request.SignaturePath);
            string publicKey = await File.ReadAllTextAsync(request.PublicKeyPath);
            KioskReleaseManifest manifest = KioskReleaseSecurity.ParseAndVerify(manifestBytes, signature, publicKey);
            if (manifest.Version != request.Assignment.Version || manifest.FileName != Path.GetFileName(request.SetupPath) ||
                new FileInfo(request.SetupPath).Length != manifest.SizeBytes ||
                !KioskReleaseSecurity.Sha256(request.SetupPath).Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("El instalador preparado no coincide con el manifiesto firmado.");

            await Report(http, request, KioskUpdateState.Installing, 100, null, "Instalando la actualización.");
            foreach (Process process in Process.GetProcessesByName("KioskClinicaPC"))
                try { process.Kill(true); process.WaitForExit(10_000); } catch { }
            kioskStopped = true;
            using var setup = Process.Start(new ProcessStartInfo(request.SetupPath,
                "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /NOCANCEL /COMPONENTS=\"kiosk\"")
            { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(request.SetupPath)! })
                ?? throw new InvalidOperationException("No se pudo iniciar el instalador.");
            await setup.WaitForExitAsync();
            int exitCode = setup.ExitCode;
            bool success = exitCode is 0 or 1641 or 3010;
            await TryReport(http, request, success ? KioskUpdateState.AwaitingRestart : KioskUpdateState.Failed,
                100, exitCode, success ? "Instalación finalizada; esperando confirmación tras reiniciar." : "El instalador devolvió un error.");
            CompleteLocalRequest(requestPath);
            ScheduleRestart();
            return success ? 0 : 1;
        }
        catch (HttpRequestException ex)
        {
            Console.Error.WriteLine("El servidor de actualizaciones no está disponible: " + ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            await TryReport(http, request, KioskUpdateState.Failed, null, null, ex.Message);
            CompleteLocalRequest(requestPath);
            if (kioskStopped) ScheduleRestart();
            return 1;
        }
    }

    private static void ScheduleRestart() => Process.Start(new ProcessStartInfo(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "shutdown.exe"),
        "/r /t 30 /c \"Actualización de Kiosko Clínica PC. El equipo se reiniciará.\"")
    { UseShellExecute = false, CreateNoWindow = true });

    private static KioskUpdateRunnerRequest? ReadRequest(string path)
    {
        try { return JsonSerializer.Deserialize<KioskUpdateRunnerRequest>(File.ReadAllText(path), Json); }
        catch { return null; }
    }

    private static void CompleteLocalRequest(string requestPath)
    {
        try { File.Delete(requestPath); }
        catch (Exception ex) { Console.Error.WriteLine("No se pudo cerrar el trabajo local: " + ex.Message); }
    }

    private static void ValidateLocalPaths(string requestPath, KioskUpdateRunnerRequest request)
    {
        string root = Path.GetFullPath(Path.GetDirectoryName(requestPath)!).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (string path in new[] { request.ManifestPath, request.SignaturePath, request.SetupPath, request.PublicKeyPath })
            if (!Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
                throw new InvalidDataException("El trabajo contiene una ruta local no autorizada.");
    }

    private static HttpClient CreateHttp(KioskUpdateRunnerRequest request)
    {
        var http = new HttpClient { BaseAddress = new Uri(request.ServerUrl.TrimEnd('/') + "/"), Timeout = TimeSpan.FromMinutes(2) };
        if (!string.IsNullOrWhiteSpace(request.ApiKey)) http.DefaultRequestHeaders.Add("X-Api-Key", request.ApiKey);
        http.DefaultRequestHeaders.Add("X-Update-Token", request.Assignment.Token);
        return http;
    }

    private static async Task Report(HttpClient http, KioskUpdateRunnerRequest request, KioskUpdateState state,
        int? progress, int? exitCode, string message)
    {
        using var response = await http.PostAsJsonAsync($"api/updates/{request.Assignment.JobId}/status",
            new KioskUpdateStatusUpdate { DeviceId = request.Assignment.DeviceId, State = state,
                ProgressPercent = progress, ExitCode = exitCode, Message = message }, Json);
        response.EnsureSuccessStatusCode();
    }

    private static async Task TryReport(HttpClient http, KioskUpdateRunnerRequest request, KioskUpdateState state,
        int? progress, int? exitCode, string message)
    { try { await Report(http, request, state, progress, exitCode, message); } catch { } }
}
