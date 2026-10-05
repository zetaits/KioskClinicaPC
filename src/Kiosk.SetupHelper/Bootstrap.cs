using System.Diagnostics;
using System.IO;

namespace Kiosk.SetupHelper;

internal static class Bootstrap
{
    public static async Task<WinGetEngine> CreateEngine(Action<string> status, CancellationToken ct)
    {
        try { var engine = new WinGetEngine(); engine.RequireSupportedRuntime(); return engine; }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or TypeInitializationException or InvalidCastException)
        {
            status("Preparando WinGet oficial; esta operación puede tardar varios minutos…");
            // Fixed script: no catalogue values, credentials or arbitrary commands are interpolated.
            const string command = "$ErrorActionPreference='Stop'; [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12; " +
                "Install-PackageProvider -Name NuGet -MinimumVersion 2.8.5.201 -Force -Scope CurrentUser | Out-Null; " +
                "Install-Module Microsoft.WinGet.Client -RequiredVersion 1.12.440 -Repository PSGallery -Scope CurrentUser -Force -AllowClobber; " +
                "Import-Module Microsoft.WinGet.Client -RequiredVersion 1.12.440; Repair-WinGetPackageManager -Version 1.29.380 -Force";
            var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"))
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
            foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", command }) start.ArgumentList.Add(arg);
            using var process = Process.Start(start) ?? throw new IOException("No se pudo preparar WinGet.");
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            // Cancellation must not terminate a package manager that may be installing dependencies.
            status("Preparando WinGet. Si cancelas, se esperará a que esta preparación termine.");
            await process.WaitForExitAsync();
            _ = await stderr;
            ct.ThrowIfCancellationRequested();
            if (process.ExitCode != 0) throw new IOException("No se pudo instalar WinGet oficial.");
            _ = await stdout; var repaired = new WinGetEngine(); repaired.RequireSupportedRuntime(); return repaired;
        }
    }
}
