using System.Diagnostics;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text.Json;
using KioskClinicaPC.Core.Sync;
using Microsoft.Win32;

internal static class Program
{
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{A7E3C9F1-2B4D-4E6A-9C8B-1F0D5E2A6B33}_is1";
    private const int MoveFileDelayUntilReboot = 0x4;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static string _logPath = "";

    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 1) return 64;
        string requestPath = Path.GetFullPath(args[0]);
        string workDirectory = Path.GetDirectoryName(requestPath)!;
        _logPath = Path.Combine(workDirectory, "maintenance.log");
        bool succeeded = false;
        KioskUninstallRunnerRequest? request = null;
        try
        {
            request = JsonSerializer.Deserialize<KioskUninstallRunnerRequest>(await File.ReadAllTextAsync(requestPath), Json)
                ?? throw new InvalidDataException("El trabajo de mantenimiento está vacío.");
            File.Delete(requestPath); // no conservar credenciales en disco durante la operación
            Validate(request);
            await Report(request, MaintenanceJobState.Uninstalling, null, "Desinstalación iniciada.");

            await WaitForKioskToExit(request.KioskProcessId);
            string innoLog = Path.Combine(workDirectory, "inno-uninstall.log");
            int exitCode = await Run(request.UninstallerPath,
                "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", $"/LOG={innoLog}");
            if (exitCode != 0 && exitCode is not 1641 and not 3010)
                throw new InvalidOperationException($"El desinstalador terminó con código {exitCode}.");

            CleanupUserState(request);
            await EnsureUserDataRemoved(request.UserDataDirectory);
            string? failure = await VerifyRemoval(request);
            if (failure != null) throw new InvalidOperationException(failure);

            bool reboot = exitCode is 1641 or 3010;
            await Report(request, reboot ? MaintenanceJobState.RebootRequired : MaintenanceJobState.Succeeded,
                exitCode, reboot ? "Desinstalación completada; Windows solicita reiniciar." : "Desinstalación completada y verificada.");
            succeeded = true;
            return 0;
        }
        catch (Exception ex)
        {
            Log(ex.ToString());
            if (request != null)
                await Report(request, MaintenanceJobState.Failed, null, ex.Message);
            return 1;
        }
        finally
        {
            if (succeeded) ScheduleSelfCleanup(workDirectory);
        }
    }

    private static void Validate(KioskUninstallRunnerRequest request)
    {
        if (request.KioskProcessId <= 0 || string.IsNullOrWhiteSpace(request.UserSid))
            throw new InvalidDataException("Identidad local incompleta.");
        if (!Directory.Exists(request.InstallDirectory))
            throw new DirectoryNotFoundException("La carpeta de instalación ya no existe.");
        string appRoot = Path.GetFullPath(request.InstallDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string uninstaller = Path.GetFullPath(request.UninstallerPath);
        if (!uninstaller.StartsWith(appRoot, StringComparison.OrdinalIgnoreCase) ||
            !System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(uninstaller), @"^unins\d{3}\.exe$", System.Text.RegularExpressions.RegexOptions.IgnoreCase) ||
            !File.Exists(uninstaller))
            throw new InvalidDataException("El desinstalador registrado no es válido.");
        if (!string.IsNullOrWhiteSpace(request.JobId) &&
            (request.JobId.Length != 32 || request.Token?.Length != 64 || request.DeviceId?.Length != 32 || request.ServerUrl == null))
            throw new InvalidDataException("Autorización remota incompleta.");
    }

    private static async Task WaitForKioskToExit(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (ArgumentException) { }
        catch (OperationCanceledException) { throw new TimeoutException("Kiosk no se cerró en 60 segundos."); }
    }

    private static async Task<int> Run(string executable, params string[] arguments)
    {
        var psi = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
        foreach (string argument in arguments) psi.ArgumentList.Add(argument);
        using Process process = Process.Start(psi) ?? throw new InvalidOperationException("No se pudo iniciar el desinstalador.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { try { process.Kill(true); } catch { } throw new TimeoutException("La desinstalación superó 15 minutos."); }
        return process.ExitCode;
    }

    private static void CleanupUserState(KioskUninstallRunnerRequest request)
    {
        using RegistryKey users = RegistryKey.OpenBaseKey(RegistryHive.Users, RegistryView.Default);
        using (RegistryKey? run = users.OpenSubKey($@"{request.UserSid}\SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
            run?.DeleteValue("KioskHardwareDisplay", false);
        using (RegistryKey? policies = users.OpenSubKey($@"{request.UserSid}\Software\Microsoft\Windows\CurrentVersion\Policies\System", true))
            policies?.DeleteValue("DisableTaskMgr", false);
        try
        {
            if (Directory.Exists(request.UserDataDirectory)) Directory.Delete(request.UserDataDirectory, true);
        }
        catch (Exception ex) { Log("No se pudo borrar toda la configuración de usuario: " + ex.Message); }
    }

    private static async Task EnsureUserDataRemoved(string directory)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
                return;
            }
            catch (Exception ex) when (attempt < 4)
            {
                Log("No se pudo borrar todavía la configuración de usuario: " + ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(1));
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("No se pudo eliminar por completo la configuración local del usuario.", ex);
            }
        }
    }

    private static async Task<string?> VerifyRemoval(KioskUninstallRunnerRequest request)
    {
        string? failure = null;
        for (int attempt = 0; attempt < 10; attempt++)
        {
            failure = VerifyRemovalOnce(request);
            if (failure == null && !await ScheduledTaskExists()) return null;
            if (failure == null) failure = "La tarea programada de actualización continúa registrada.";
            if (attempt < 9) await Task.Delay(TimeSpan.FromSeconds(1));
        }
        return failure;
    }

    private static string? VerifyRemovalOnce(KioskUninstallRunnerRequest request)
    {
        using RegistryKey hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using RegistryKey? uninstall = hklm.OpenSubKey(UninstallKey);
        if (uninstall != null) return "Windows todavía conserva la entrada de desinstalación de Kiosk.";
        if (File.Exists(Path.Combine(request.InstallDirectory, "KioskClinicaPC.exe")))
            return "El ejecutable de Kiosk continúa en la carpeta de instalación.";
        using RegistryKey? service = hklm.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\KioskClinicaPCInstallerAgent");
        if (service != null) return "El servicio InstallerAgent continúa registrado.";
        return null;
    }

    private static async Task<bool> ScheduledTaskExists()
    {
        string executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "schtasks.exe");
        var psi = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
        psi.ArgumentList.Add("/Query");
        psi.ArgumentList.Add("/TN");
        psi.ArgumentList.Add("KioskClinicaPC Updater");
        using Process process = Process.Start(psi) ?? throw new InvalidOperationException("No se pudo consultar la tarea programada.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { try { process.Kill(true); } catch { } throw new TimeoutException("No se pudo verificar la tarea programada."); }
        return process.ExitCode == 0;
    }

    private static async Task Report(KioskUninstallRunnerRequest request, MaintenanceJobState state, int? exitCode, string message)
    {
        if (string.IsNullOrWhiteSpace(request.ServerUrl) || string.IsNullOrWhiteSpace(request.JobId) ||
            string.IsNullOrWhiteSpace(request.Token) || string.IsNullOrWhiteSpace(request.DeviceId)) return;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                using var http = new HttpClient { BaseAddress = new Uri(request.ServerUrl.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(15) };
                if (!string.IsNullOrWhiteSpace(request.ApiKey)) http.DefaultRequestHeaders.Add("X-Api-Key", request.ApiKey);
                http.DefaultRequestHeaders.Add("X-Maintenance-Token", request.Token);
                using var response = await http.PostAsJsonAsync($"api/maintenance/{request.JobId}/status",
                    new MaintenanceStatusUpdate { DeviceId = request.DeviceId, State = state, ExitCode = exitCode, Message = message }, Json);
                response.EnsureSuccessStatusCode();
                return;
            }
            catch (Exception ex)
            {
                Log($"No se pudo reportar {state} (intento {attempt + 1}): {ex.Message}");
                if (attempt < 2) await Task.Delay(TimeSpan.FromSeconds(3));
            }
        }
    }

    private static void Log(string message)
    {
        try { File.AppendAllText(_logPath, $"{DateTime.UtcNow:O} {message}{Environment.NewLine}"); } catch { }
    }

    private static void ScheduleSelfCleanup(string directory)
    {
        try
        {
            foreach (string file in Directory.EnumerateFiles(directory))
            {
                if (!Path.GetFullPath(file).Equals(Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))
                    try { File.Delete(file); } catch { MoveFileEx(file, null, MoveFileDelayUntilReboot); }
            }
            if (Environment.ProcessPath != null) MoveFileEx(Environment.ProcessPath, null, MoveFileDelayUntilReboot);
            MoveFileEx(directory, null, MoveFileDelayUntilReboot);
        }
        catch { }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool MoveFileEx(string existingFileName, string? newFileName, int flags);
}
