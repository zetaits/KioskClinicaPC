using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using KioskClinicaPC.Equipment;

namespace Kiosk.EquipmentSetup;
internal sealed class KioskPayload(string work) : IEquipmentKiosk
{
    internal static string InstallDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "KioskClinicaPC");
    internal static string ClientExe => Path.Combine(InstallDirectory, "KioskClinicaPC.exe");
    private sealed record KioskState(bool Active, DateTime BootTime, bool RebootRequired);
    private static string StatePath => Path.Combine(MachineState.Root, "kiosk-run.json");
    public bool RebootRequired { get; private set; }
    internal static bool Verified()
    {
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var uninstall = machine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{A7E3C9F1-2B4D-4E6A-9C8B-1F0D5E2A6B33}_is1");
        if (!Version.TryParse(Payload.Manifest.KioskVersion, out var expected) ||
            !Version.TryParse(uninstall?.GetValue("DisplayVersion") as string, out var installed) || installed < expected ||
            !string.Equals((uninstall?.GetValue("InstallLocation") as string)?.TrimEnd('\\'), InstallDirectory, StringComparison.OrdinalIgnoreCase)) return false;
        foreach (string relative in new[] { "KioskClinicaPC.exe", "unins000.exe", @"Agent\KioskInstallerAgent.exe", @"Agent\Maintenance\KioskMaintenanceRunner.exe", @"Agent\Update\KioskUpdateRunner.exe" })
        {
            string path = Path.Combine(InstallDirectory, relative);
            if (!File.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return false;
        }
        return Version.TryParse(FileVersionInfo.GetVersionInfo(ClientExe).FileVersion, out var executable) && executable >= expected;
    }
    public async Task<bool> InstallAndVerify(bool resume, Action<EquipmentEvent> progress, CancellationToken ct)
    {
        DateTime boot = DateTime.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64);
        var previous = File.Exists(StatePath) ? JsonSerializer.Deserialize<KioskState>(await File.ReadAllTextAsync(StatePath, ct), Payload.Json) : null;
        RebootRequired = previous?.RebootRequired == true && Math.Abs((boot - previous.BootTime).TotalMinutes) < 1;
        if (Verified()) { await ProvisionPanelPassword(InstallDirectory, progress); await Save(new(false, boot, RebootRequired)); progress(new("phase", "Kiosk ya está instalado y verificado.")); return true; }
        if (previous?.Active == true && Math.Abs((boot - previous.BootTime).TotalMinutes) < 1)
            throw new NativeStateException("El instalador anterior de Kiosk puede seguir activo. Reinicia manualmente antes de reintentar.");
        string exe = await Payload.Extract("kiosk.exe", Payload.Manifest.KioskSha256, work, progress, ct);
        ct.ThrowIfCancellationRequested();
        progress(new("phase", "Instalando Kiosk en silencio. La cancelación esperará a que termine su instalador…"));
        await Save(new(true, boot, RebootRequired));
        var start = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = work };
        foreach (string argument in new[] { "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/SP-", "/COMPONENTS=kiosk", "/RESTARTEXITCODE=3010", "/LOG=" + Path.Combine(MachineState.Root, "logs", "kiosk-" + Path.GetFileName(work) + ".log") }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("No se pudo iniciar Inno.");
        await process.WaitForExitAsync();
        RebootRequired |= process.ExitCode == 3010;
        bool verified = process.ExitCode is 0 or 3010 && Verified();
        if (verified) await ProvisionPanelPassword(InstallDirectory, progress);
        await Save(new(!verified, boot, RebootRequired));
        progress(new("phase", verified ? "Kiosk instalado y verificado." : "No se puede confirmar la instalación de Kiosk."));
        return verified;
    }
    internal static async Task ProvisionPanelPassword(string directory, Action<EquipmentEvent> progress)
    {
        var seed = Payload.PanelPassword();
        if (seed is null) return;
        string target = Path.Combine(directory, PanelPasswordProvisioning.FileName);
        string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        SetupComponentCache.SafePath(target);
        SetupComponentCache.SafePath(temporary);
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(seed, Payload.Json));
            File.Move(temporary, target, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        progress(new("phase", "Contraseña inicial preparada. Kiosk usará la del panel si aún no tiene una contraseña local."));
    }
    private static async Task Save(KioskState state)
    {
        await File.WriteAllTextAsync(StatePath + ".tmp", JsonSerializer.Serialize(state, Payload.Json));
        File.Move(StatePath + ".tmp", StatePath, true);
    }
    internal static async Task RegisterAutostart()
    {
        var start = new ProcessStartInfo(ClientExe) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("--register-autostart-only");
        using var process = Process.Start(start) ?? throw new IOException("No se pudo registrar el inicio de Kiosk.");
        await process.WaitForExitAsync();
        using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run");
        if (process.ExitCode != 0 || key?.GetValue("KioskHardwareDisplay") is not string value || !value.Contains(ClientExe, StringComparison.OrdinalIgnoreCase))
            throw new IOException("No se pudo verificar el inicio automático de Kiosk para este usuario.");
    }
}
