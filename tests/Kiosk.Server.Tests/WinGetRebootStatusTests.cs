using Kiosk.SetupHelper;
using Xunit;

namespace Kiosk.Server.Tests;

public sealed class WinGetRebootStatusTests
{
    [Theory]
    [InlineData(false, 0u, false, false)]
    [InlineData(true, 0u, false, true)]
    [InlineData(false, 3010u, false, true)]
    [InlineData(false, 1641u, false, true)]
    [InlineData(false, 0u, true, true)]
    [InlineData(false, 1603u, false, false)]
    public void Native_and_system_reboot_evidence_are_independent(bool api, uint code, bool system, bool expected)
        => Assert.Equal(expected, WinGetRebootStatus.Required(api, code, system));

    [Theory]
    [InlineData(WinGetRebootStatus.Servicing)]
    [InlineData(WinGetRebootStatus.WindowsUpdate)]
    public void Windows_reboot_marker_is_detected(string marker)
        => Assert.True(WinGetRebootStatus.Read(key => key == marker, _ => null));

    [Theory]
    [InlineData("PendingFileRenameOperations")]
    [InlineData("PendingFileRenameOperations2")]
    public void Pending_file_deletion_with_empty_destination_still_requires_reboot(string name)
        => Assert.True(WinGetRebootStatus.Read(_ => false,
            value => value == name ? new[] { @"\??\C:\Program Files\App\in-use.dll", "" } : null));

    [Fact]
    public void Missing_empty_or_unexpected_registry_values_do_not_report_reboot()
    {
        Assert.False(WinGetRebootStatus.Read(_ => false, _ => null));
        Assert.False(WinGetRebootStatus.Read(_ => false, _ => Array.Empty<string>()));
        Assert.False(WinGetRebootStatus.Read(_ => false, _ => new[] { "", " " }));
        Assert.False(WinGetRebootStatus.Read(_ => false, _ => 1));
    }
}
