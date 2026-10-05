using Microsoft.Win32;

namespace Kiosk.DeploymentPostInstall;

internal static class CredentialCleanup
{
    internal static void Run()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon", true)
            ?? throw new IOException("No se puede limpiar el inicio temporal.");
        key.SetValue("AutoAdminLogon", "0", RegistryValueKind.String);
        foreach (var name in new[] { "DefaultPassword", "DefaultUserName", "DefaultDomainName", "AutoLogonCount", "ForceAutoLogon" }) key.DeleteValue(name, false);
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        foreach (var relative in new[] { @"Panther\unattend.xml", @"Panther\Unattend\unattend.xml", @"System32\Sysprep\unattend.xml" })
        {
            string path = Path.Combine(windows, relative);
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path)) throw new IOException("No se pudo limpiar un archivo de respuestas.");
        }
        if (key.GetValue("DefaultPassword") is not null || key.GetValue("AutoAdminLogon") as string != "0")
            throw new IOException("Inicio temporal no verificado.");
    }
}
