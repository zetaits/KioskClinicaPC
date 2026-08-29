using System.Diagnostics;
using System.IO.Pipes;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using System.Threading.Channels;
using KioskClinicaPC.Core.Sync;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace Kiosk.InstallerAgent;

public sealed class InstallerWorker : BackgroundService
{
    public const string PipeName = "KioskClinicaPC.InstallerAgent.v1";
    private const string RegistryPath = @"SOFTWARE\ClinicaPC\Kiosk";
    private const string TrustedServerValue = "InstallerServerUrl";
    private static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(60);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly ILogger<InstallerWorker> _log;
    private const string UninstallRegistryPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{A7E3C9F1-2B4D-4E6A-9C8B-1F0D5E2A6B33}_is1";
    private readonly Channel<InstallerAgentRequest> _queue = Channel.CreateBounded<InstallerAgentRequest>(1);
    private int _busy;
    private readonly string _workRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "KioskClinicaPC", "install-jobs");

    public InstallerWorker(ILogger<InstallerWorker> log) { _log = log; Directory.CreateDirectory(_workRoot); }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Task pipe = AcceptPipeLoop(stoppingToken);
        Task jobs = ProcessLoop(stoppingToken);
        await Task.WhenAll(pipe, jobs);
    }

    private async Task AcceptPipeLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var pipe = CreatePipe();
                await pipe.WaitForConnectionAsync(ct);
                using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
                ClientIdentity? identity = GetTrustedClient(pipe);
                if (identity == null)
                {
                    await writer.WriteLineAsync(JsonSerializer.Serialize(new InstallerAgentResponse
                    { Accepted = false, AgentVersion = Version, Error = "Cliente local no autorizado." }, Json));
                    continue;
                }
                using var reader = new StreamReader(pipe, leaveOpen: true);
                var request = JsonSerializer.Deserialize<InstallerAgentRequest>(await reader.ReadLineAsync(ct) ?? "", Json);
                InstallerAgentResponse response;
                if (request?.Operation == "health")
                    response = TrustOrValidateOrigin(request.ServerUrl, allowEnrollment: true, out string? error)
                        ? new() { Accepted = true, AgentVersion = Version, CanUninstallKiosk = RunnerAvailable }
                        : new() { Accepted = false, AgentVersion = Version, Error = error };
                else if (request?.Operation == "uninstall-kiosk")
                {
                    if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
                        response = new() { Accepted = false, AgentVersion = Version, Error = "Ya hay un mantenimiento activo." };
                    else
                    {
                        response = StartKioskUninstall(request, identity);
                        if (!response.Accepted) Interlocked.Exchange(ref _busy, 0);
                    }
                }
                else if (request == null || !ValidInstall(request))
                    response = new() { Accepted = false, AgentVersion = Version, Error = "Petición incompleta." };
                else if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
                    response = new() { Accepted = false, AgentVersion = Version, Error = "Ya hay una instalación activa." };
                else if (!_queue.Writer.TryWrite(request))
                {
                    Interlocked.Exchange(ref _busy, 0);
                    response = new() { Accepted = false, AgentVersion = Version, Error = "No se pudo encolar la instalación." };
                }
                else
                    response = new() { Accepted = true, AgentVersion = Version };
                await writer.WriteLineAsync(JsonSerializer.Serialize(response, Json));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception ex) { _log.LogError(ex, "Error en el canal local del agente."); }
        }
    }

    private static NamedPipeServerStream CreatePipe()
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            PipeAccessRights.ReadWrite, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(PipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, security);
    }

    private static ClientIdentity? GetTrustedClient(NamedPipeServerStream pipe)
    {
        try
        {
            if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out uint pid)) return null;
            using var process = System.Diagnostics.Process.GetProcessById((int)pid);
            string? actual = process.MainModule?.FileName;
            string expected = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "KioskClinicaPC.exe"));
            if (actual == null || !Path.GetFullPath(actual).Equals(expected, StringComparison.OrdinalIgnoreCase)) return null;
            string? sid = null;
            pipe.RunAsClient(() => sid = WindowsIdentity.GetCurrent(true).User?.Value);
            return string.IsNullOrWhiteSpace(sid) ? null : new ClientIdentity((int)pid, sid);
        }
        catch { return null; }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint clientProcessId);

    private async Task ProcessLoop(CancellationToken ct)
    {
        await foreach (var request in _queue.Reader.ReadAllAsync(ct))
        {
            try { await Process(request, ct); }
            finally { Interlocked.Exchange(ref _busy, 0); }
        }
    }

    private async Task Process(InstallerAgentRequest request, CancellationToken serviceCt)
    {
        string jobId = request.JobId!;
        using var http = CreateHttp(request.ServerUrl!, request.ApiKey, request.Token!);
        try
        {
            var manifest = await http.GetFromJsonAsync<InstallationManifest>($"api/installations/{jobId}/manifest", Json, serviceCt)
                ?? throw new InvalidDataException("Manifiesto vacío.");
            if (manifest.JobId != jobId || manifest.DeviceId != request.DeviceId)
                throw new InvalidDataException("El manifiesto no corresponde al equipo.");

            string folder = Path.Combine(_workRoot, jobId);
            Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, "package" + (manifest.Kind == InstallerPackageKind.Msi ? ".msi" : ".exe"));
            await TryReport(http, jobId, request.DeviceId!, InstallationJobState.Downloading, 0, null, null, serviceCt);
            await DownloadResumable(http, $"api/installations/{jobId}/download", file, manifest.SizeBytes,
                p => TryReport(http, jobId, request.DeviceId!, InstallationJobState.Downloading, p, null, null, serviceCt), serviceCt);

            await TryReport(http, jobId, request.DeviceId!, InstallationJobState.Verifying, 100, null, null, serviceCt);
            if (new FileInfo(file).Length != manifest.SizeBytes || !Hash(file).Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Tamaño o SHA-256 incorrecto.");
            SignatureResult signature = Authenticode.Verify(file);
            if (signature == SignatureResult.Invalid || (signature == SignatureResult.Unsigned && !manifest.AllowUnsigned))
                throw new InvalidDataException(signature == SignatureResult.Invalid ? "La firma digital no es válida." : "El instalador no está firmado.");

            await TryReport(http, jobId, request.DeviceId!, InstallationJobState.Installing, 100, null, null, serviceCt);
            int exit = await ExecuteInstaller(file, manifest.Kind, serviceCt);
            bool reboot = exit is 1641 or 3010;
            if (exit != 0 && !reboot) throw new InstallerExitException(exit);
            await TryReport(http, jobId, request.DeviceId!, reboot ? InstallationJobState.RebootRequired : InstallationJobState.Succeeded,
                100, exit, reboot ? "Instalación completada; Windows solicita reiniciar." : "Instalación completada.", serviceCt);
            TryDeleteDirectory(folder);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Falló el trabajo {JobId}.", jobId);
            int? exit = ex is InstallerExitException ie ? ie.ExitCode : null;
            await TryReport(http, jobId, request.DeviceId!, InstallationJobState.Failed, null, exit, ex.Message, CancellationToken.None);
        }
    }

    private static HttpClient CreateHttp(string serverUrl, string? apiKey, string token)
    {
        var handler = new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(20) };
        var http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(20), BaseAddress = new Uri(serverUrl.TrimEnd('/') + "/") };
        if (!string.IsNullOrWhiteSpace(apiKey)) http.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
        http.DefaultRequestHeaders.Add("X-Install-Token", token);
        return http;
    }

    private static async Task DownloadResumable(HttpClient http, string relativeUrl, string path, long expected,
        Func<int, Task> progress, CancellationToken ct)
    {
        Exception? last = null;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                long offset = File.Exists(path) ? new FileInfo(path).Length : 0;
                if (offset > expected) { File.Delete(path); offset = 0; }
                using var req = new HttpRequestMessage(HttpMethod.Get, relativeUrl);
                if (offset > 0) req.Headers.Range = new RangeHeaderValue(offset, null);
                using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                if (offset > 0 && resp.StatusCode == HttpStatusCode.OK) { File.Delete(path); offset = 0; }
                resp.EnsureSuccessStatusCode();
                await using var input = await resp.Content.ReadAsStreamAsync(ct);
                await using var output = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.None, 81920, true);
                byte[] buffer = new byte[81920]; int read; long total = offset; int lastPercent = -1;
                while ((read = await input.ReadAsync(buffer, ct)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), ct); total += read;
                    int percent = expected == 0 ? 0 : (int)Math.Min(99, total * 100 / expected);
                    if (percent >= lastPercent + 5) { lastPercent = percent; await progress(percent); }
                }
                if (total == expected) return;
                throw new IOException("Descarga incompleta.");
            }
            catch (Exception ex) when (attempt < 2 && ex is HttpRequestException or IOException or TaskCanceledException) { last = ex; }
        }
        throw new IOException("No se pudo descargar el instalador tras tres intentos.", last);
    }

    private static async Task<int> ExecuteInstaller(string file, InstallerPackageKind kind, CancellationToken ct)
    {
        string exe; string[] args;
        switch (kind)
        {
            case InstallerPackageKind.Msi:
                exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "msiexec.exe");
                args = new[] { "/i", file, "/qn", "/norestart" }; break;
            case InstallerPackageKind.InnoSetup:
                exe = file; args = new[] { "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/NOCANCEL" }; break;
            case InstallerPackageKind.Nsis:
                exe = file; args = new[] { "/S" }; break;
            default: throw new InvalidDataException("Tipo de instalador no permitido.");
        }
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(file)! };
        foreach (string arg in args) psi.ArgumentList.Add(arg);
        using var process = System.Diagnostics.Process.Start(psi) ?? throw new InvalidOperationException("No se pudo iniciar el instalador.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(InstallTimeout);
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { try { process.Kill(true); } catch { } throw new TimeoutException("La instalación superó 60 minutos."); }
        return process.ExitCode;
    }

    private static async Task Report(HttpClient http, string job, string device, InstallationJobState state,
        int? progress, int? exitCode, string? message, CancellationToken ct)
    {
        using var resp = await http.PostAsJsonAsync($"api/installations/{job}/status",
            new InstallationStatusUpdate { DeviceId = device, State = state, ProgressPercent = progress, ExitCode = exitCode, Message = message }, Json, ct);
        resp.EnsureSuccessStatusCode();
    }

    private async Task TryReport(HttpClient http, string job, string device, InstallationJobState state,
        int? progress, int? exitCode, string? message, CancellationToken ct)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try { await Report(http, job, device, state, progress, exitCode, message, ct); return; }
            catch (Exception ex) when (attempt < 2 && !ct.IsCancellationRequested)
            {
                _log.LogWarning(ex, "No se pudo publicar {State} para {JobId}; reintentando.", state, job);
                try { await Task.Delay(TimeSpan.FromSeconds(2 * (attempt + 1)), ct); } catch { return; }
            }
            catch (Exception ex) { _log.LogWarning(ex, "No se pudo publicar {State} para {JobId}.", state, job); return; }
        }
    }

    private static bool ValidInstall(InstallerAgentRequest r)
    {
        return r.Operation == "install" && TrustOrValidateOrigin(r.ServerUrl, allowEnrollment: false, out _) &&
            IsHex(r.DeviceId, 32) && IsHex(r.JobId, 32) && IsHex(r.Token, 64);
    }

    private InstallerAgentResponse StartKioskUninstall(InstallerAgentRequest request, ClientIdentity identity)
    {
        try
        {
            if (request.KioskProcessId != identity.ProcessId)
                throw new InvalidDataException("La solicitud no corresponde al proceso Kiosk conectado.");
            bool remote = !string.IsNullOrWhiteSpace(request.JobId) || !string.IsNullOrWhiteSpace(request.Token);
            if (remote && (!TrustOrValidateOrigin(request.ServerUrl, allowEnrollment: false, out string? originError) ||
                !IsHex(request.DeviceId, 32) || !IsHex(request.JobId, 32) || !IsHex(request.Token, 64)))
                throw new InvalidDataException(originError ?? "Autorización remota incompleta.");

            string appRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
            string uninstaller = ResolveKioskUninstaller(appRoot);
            string userData = ValidateUserDataDirectory(request.UserDataDirectory, identity.UserSid);
            string runnerSource = Path.Combine(AppContext.BaseDirectory, "Maintenance", "KioskMaintenanceRunner.exe");
            if (!File.Exists(runnerSource)) throw new FileNotFoundException("El ejecutor de mantenimiento no está instalado.", runnerSource);

            string stagingRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "ClinicaPC", "KioskMaintenance", Guid.NewGuid().ToString("N"));
            CreateProtectedDirectory(stagingRoot);
            string runner = Path.Combine(stagingRoot, "KioskMaintenanceRunner.exe");
            File.Copy(runnerSource, runner, overwrite: false);
            string requestPath = Path.Combine(stagingRoot, "request.json");
            File.WriteAllText(requestPath, JsonSerializer.Serialize(new KioskUninstallRunnerRequest
            {
                UninstallerPath = uninstaller, InstallDirectory = appRoot, UserSid = identity.UserSid,
                UserDataDirectory = userData, KioskProcessId = identity.ProcessId,
                ServerUrl = remote ? request.ServerUrl : null, ApiKey = remote ? request.ApiKey : null,
                DeviceId = remote ? request.DeviceId : null, JobId = remote ? request.JobId : null,
                Token = remote ? request.Token : null
            }, Json));

            var psi = new ProcessStartInfo(runner) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = stagingRoot };
            psi.ArgumentList.Add(requestPath);
            System.Diagnostics.Process runnerProcess = System.Diagnostics.Process.Start(psi)
                ?? throw new InvalidOperationException("No se pudo iniciar el ejecutor de mantenimiento.");
            _ = ReleaseBusyWhenRunnerExits(runnerProcess);
            _log.LogWarning("Autodesinstalación de Kiosk aceptada para SID {Sid}; trabajo remoto: {Remote}.", identity.UserSid, remote);
            return new() { Accepted = true, AgentVersion = Version };
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Se rechazó la autodesinstalación de Kiosk.");
            return new() { Accepted = false, AgentVersion = Version, Error = ex.Message };
        }
    }

    private async Task ReleaseBusyWhenRunnerExits(System.Diagnostics.Process runnerProcess)
    {
        using (runnerProcess)
        {
            try { await runnerProcess.WaitForExitAsync(); }
            catch (Exception ex) { _log.LogDebug(ex, "No se pudo observar el final del ejecutor de mantenimiento."); }
            finally { Interlocked.Exchange(ref _busy, 0); }
        }
    }

    internal static string ResolveKioskUninstaller(string appRoot)
    {
        using RegistryKey hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using RegistryKey? key = hklm.OpenSubKey(UninstallRegistryPath);
        string command = key?.GetValue("UninstallString") as string
            ?? throw new InvalidOperationException("La instalación de Kiosk no está registrada en Windows.");
        string executable = ExtractExecutable(command);
        string root = Path.GetFullPath(appRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string full = Path.GetFullPath(executable);
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ||
            !System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(full), @"^unins\d{3}\.exe$", System.Text.RegularExpressions.RegexOptions.IgnoreCase) ||
            !File.Exists(full))
            throw new InvalidOperationException("El desinstalador registrado es inválido o ha desaparecido; reinstala Kiosk para repararlo.");
        return full;
    }

    internal static string ExtractExecutable(string command)
    {
        command = command.Trim();
        if (command.StartsWith('"'))
        {
            int end = command.IndexOf('"', 1);
            if (end <= 1) throw new InvalidDataException("UninstallString no es válido.");
            return command[1..end];
        }
        int exeEnd = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exeEnd < 0) throw new InvalidDataException("UninstallString no contiene un ejecutable.");
        return command[..(exeEnd + 4)];
    }

    private static string ValidateUserDataDirectory(string? requested, string sid)
    {
        if (string.IsNullOrWhiteSpace(requested)) throw new InvalidDataException("No se indicó la carpeta de datos del usuario.");
        using RegistryKey hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using RegistryKey? profile = hklm.OpenSubKey($@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\{sid}");
        string profileRoot = Environment.ExpandEnvironmentVariables(profile?.GetValue("ProfileImagePath") as string
            ?? throw new InvalidDataException("No se pudo resolver el perfil del usuario."));
        string full = Path.GetFullPath(requested);
        string allowedRoot = Path.GetFullPath(profileRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(full).Equals("KioskClinicaPC", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("La carpeta de datos del usuario no es válida.");
        return full;
    }

    private static void CreateProtectedDirectory(string path)
    {
        Directory.CreateDirectory(path);
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(path).SetAccessControl(security);
    }

    private static bool TrustOrValidateOrigin(string? serverUrl, bool allowEnrollment, out string? error)
    {
        error = null;
        if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out var uri)) { error = "El servidor no está configurado."; return false; }
        bool localHttp = uri.Scheme == Uri.UriSchemeHttp && (uri.IsLoopback || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase));
        if (uri.Scheme != Uri.UriSchemeHttps && !localHttp) { error = "El agente exige HTTPS (salvo localhost)."; return false; }
        string normalized = uri.GetComponents(UriComponents.SchemeAndServer, UriFormat.UriEscaped).ToLowerInvariant()
            + uri.GetComponents(UriComponents.Path, UriFormat.UriEscaped).TrimEnd('/');
        using RegistryKey? key = allowEnrollment
            ? Registry.LocalMachine.CreateSubKey(RegistryPath, writable: true)
            : Registry.LocalMachine.OpenSubKey(RegistryPath, writable: false);
        if (key == null) { error = "El agente todavía no está emparejado con un servidor."; return false; }
        string? trusted = key.GetValue(TrustedServerValue) as string;
        if (string.IsNullOrWhiteSpace(trusted) && allowEnrollment)
        {
            key.SetValue(TrustedServerValue, normalized, RegistryValueKind.String);
            return true;
        }
        if (!string.Equals(trusted?.TrimEnd('/'), normalized, StringComparison.Ordinal))
        {
            error = "El servidor no coincide con el origen emparejado del agente.";
            return false;
        }
        return true;
    }

    private static bool IsHex(string? value, int length) => value?.Length == length && value.All(Uri.IsHexDigit);
    private static string Hash(string path) { using var sha = SHA256.Create(); using var fs = File.OpenRead(path); return Convert.ToHexString(sha.ComputeHash(fs)); }
    private static void TryDeleteDirectory(string path) { try { Directory.Delete(path, true); } catch { } }
    private static string Version => Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
    private static bool RunnerAvailable => File.Exists(Path.Combine(AppContext.BaseDirectory, "Maintenance", "KioskMaintenanceRunner.exe"));

    private sealed record ClientIdentity(int ProcessId, string UserSid);
}

internal sealed class InstallerExitException(int exitCode) : Exception($"El instalador terminó con código {exitCode}.") { public int ExitCode { get; } = exitCode; }

internal enum SignatureResult { Valid, Unsigned, Invalid }

internal static class Authenticode
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private sealed class WinTrustFileInfo
    {
        public int StructSize = Marshal.SizeOf<WinTrustFileInfo>(); public IntPtr FilePath; public IntPtr FileHandle; public IntPtr KnownSubject;
        public WinTrustFileInfo(string path) { FilePath = Marshal.StringToCoTaskMemUni(path); }
        ~WinTrustFileInfo() { if (FilePath != IntPtr.Zero) Marshal.FreeCoTaskMem(FilePath); }
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private sealed class WinTrustData
    {
        public int StructSize = Marshal.SizeOf<WinTrustData>(); public IntPtr PolicyCallbackData; public IntPtr SIPClientData;
        public int UIChoice = 2; public int RevocationChecks = 0; public int UnionChoice = 1; public IntPtr FileInfo;
        public int StateAction = 0; public IntPtr StateData; public string? URLReference; public int ProvFlags = 0x1000; public int UIContext;
        public WinTrustData(WinTrustFileInfo file) { FileInfo = Marshal.AllocCoTaskMem(Marshal.SizeOf<WinTrustFileInfo>()); Marshal.StructureToPtr(file, FileInfo, false); }
        ~WinTrustData() { if (FileInfo != IntPtr.Zero) Marshal.FreeCoTaskMem(FileInfo); }
    }
    [DllImport("wintrust.dll", ExactSpelling = true, PreserveSig = true, SetLastError = false)]
    private static extern uint WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid action, WinTrustData data);
    public static SignatureResult Verify(string file)
    {
        var info = new WinTrustFileInfo(file); var data = new WinTrustData(info);
        uint result = WinVerifyTrust(new IntPtr(-1), new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE"), data);
        return result switch { 0 => SignatureResult.Valid, 0x800B0100 or 0x800B0003 or 0x800B0001 => SignatureResult.Unsigned, _ => SignatureResult.Invalid };
    }
}
