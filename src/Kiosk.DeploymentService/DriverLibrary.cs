using System.Security.Cryptography;
using Kiosk.Deployment;

namespace Kiosk.DeploymentService;

public sealed class DriverLibrary(StationState state, NetworkBoot boot)
{
    public async Task Import(string source, CancellationToken ct)
    {
        DeploymentPolicy.Require(!state.Settings.Enabled && !state.Queue.Snapshot().Jobs.Any(j => DeploymentPolicy.Active(j.State)), "Desactiva PXE y espera a los trabajos antes de importar controladores.");
        boot.ValidateBundle();
        string directory = Path.Combine(StationState.Root, "drivers", Guid.NewGuid().ToString("N")); StationState.SecureDirectory(directory);
        string customBoot = Path.Combine(directory, "boot.wim");
        await PowerShellRunner.Run<System.Text.Json.JsonElement>("Import-Drivers.ps1", new { Source = source, Target = Path.Combine(directory, "package"),
            BaseBoot = Path.Combine(boot.BootRoot, "boot.wim"), Boot = customBoot }, ct);
        using var input = File.OpenRead(customBoot);
        string bootHash = Convert.ToHexString(SHA256.HashData(input));
        string package = Path.Combine(directory, "drivers.zip");
        System.IO.Compression.ZipFile.CreateFromDirectory(Path.Combine(directory, "package"), package);
        using var zip = File.OpenRead(package); string packageHash = Convert.ToHexString(SHA256.HashData(zip));
        string retained = Path.Combine(StationState.Root, "drivers", packageHash + ".zip");
        if (!File.Exists(retained)) File.Copy(package, retained);
        AtomicState.Write(Path.Combine(StationState.Root, "driver-boot.json"), new DriverBoot(customBoot, bootHash, packageHash));
    }
}
public sealed record DriverBoot(string Path, string Hash, string PackageHash);
