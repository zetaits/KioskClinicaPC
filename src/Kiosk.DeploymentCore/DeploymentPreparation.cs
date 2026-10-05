using KioskClinicaPC.Equipment;

namespace Kiosk.Deployment;

public sealed record DeploymentPreparationResult(bool WindowsVerified, bool AccountVerified, EquipmentEvent Components);
public static class DeploymentPreparation
{
    public static async Task<DeploymentPreparationResult> Run(Func<Task> cleanup, Func<bool> windows, Func<bool> account,
        Func<Task<EquipmentEvent>> components)
    {
        bool windowsVerified = false, accountVerified = false;
        try
        {
            await cleanup();
            windowsVerified = windows(); accountVerified = account();
            DeploymentPolicy.Require(windowsVerified && accountVerified, "Windows o la cuenta autorizada no superan la verificación.");
            return new(windowsVerified, accountVerified, await components());
        }
        catch
        {
            return new(windowsVerified, accountVerified, new("result", "Revisa Windows, la cuenta y la limpieza del inicio temporal antes de reanudar.", ExitCode: 2));
        }
    }
}
