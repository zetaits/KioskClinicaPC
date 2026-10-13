using System.Diagnostics;
using System.IO;

namespace Kiosk.SetupHelper;

/// <summary>Fixed official bootstrap, independent of the COM activation and package selection.</summary>
internal static class WinGetPreparation
{
    internal const string Command = """
        $ErrorActionPreference = 'Stop'
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        $moduleReady = $false
        try {
            Import-Module Microsoft.WinGet.Client -RequiredVersion 1.12.440 -ErrorAction Stop
            $moduleReady = $true
        } catch { }
        if (-not $moduleReady) {
            try {
                Import-PackageProvider -Name NuGet -MinimumVersion 2.8.5.201 -ForceBootstrap:$false -ErrorAction Stop | Out-Null
            } catch {
                Install-PackageProvider -Name NuGet -MinimumVersion 2.8.5.201 -Force -Scope CurrentUser | Out-Null
            }
            Install-Module Microsoft.WinGet.Client -RequiredVersion 1.12.440 -Repository PSGallery -Scope CurrentUser -Force -AllowClobber
            Import-Module Microsoft.WinGet.Client -RequiredVersion 1.12.440 -ErrorAction Stop
        }
        Repair-WinGetPackageManager -Version 1.29.380 -Force
        """;

    internal static async Task Run(CancellationToken ct, Func<ProcessStartInfo, Task<int>>? execute = null)
    {
        ct.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", Command }) start.ArgumentList.Add(arg);
        // A native repair may already be installing dependencies. Drain both pipes and
        // wait for it to finish even if the caller cancels; never kill that process.
        int exitCode = await (execute ?? Execute)(start);
        ct.ThrowIfCancellationRequested();
        if (exitCode != 0) throw new IOException("No se pudo instalar WinGet oficial.");
    }
    private static async Task<int> Execute(ProcessStartInfo start)
    {
        using var process = Process.Start(start) ?? throw new IOException("No se pudo preparar WinGet.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        await Task.WhenAll(stdout, stderr);
        return process.ExitCode;
    }
}
