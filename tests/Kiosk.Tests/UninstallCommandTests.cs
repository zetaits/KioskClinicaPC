using Kiosk.InstallerAgent;
using Xunit;

namespace KioskClinicaPC.Tests;

public sealed class UninstallCommandTests
{
    [Theory]
    [InlineData("\"C:\\Program Files\\KioskClinicaPC\\unins000.exe\"", "C:\\Program Files\\KioskClinicaPC\\unins000.exe")]
    [InlineData("\"C:\\Program Files\\KioskClinicaPC\\unins001.exe\" /SILENT", "C:\\Program Files\\KioskClinicaPC\\unins001.exe")]
    [InlineData("C:\\Kiosk\\unins000.exe /SILENT", "C:\\Kiosk\\unins000.exe")]
    public void ExtractExecutable_handles_registered_Inno_commands(string command, string expected) =>
        Assert.Equal(expected, InstallerWorker.ExtractExecutable(command));

    [Theory]
    [InlineData("")]
    [InlineData("not an executable command")]
    [InlineData("\"unterminated")]
    public void ExtractExecutable_rejects_non_executable_or_malformed_commands(string command) =>
        Assert.ThrowsAny<Exception>(() => InstallerWorker.ExtractExecutable(command));
}
