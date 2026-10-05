using Kiosk.Deployment;
using KioskClinicaPC.Equipment;
using Xunit;

namespace Kiosk.Server.Tests;
public sealed class DeploymentPreparationTests
{
    [Fact]
    public async Task Failed_credential_cleanup_prevents_all_native_installers_and_success()
    {
        bool executed = false;
        var result = await DeploymentPreparation.Run(() => throw new IOException("Cannot remove answer file"), () => true, () => true,
            () => { executed = true; return Task.FromResult(new EquipmentEvent("result", "Done", ExitCode: 0)); });
        Assert.False(executed); Assert.False(result.WindowsVerified); Assert.NotEqual(0, result.Components.ExitCode);
    }
    [Theory]
    [InlineData(false, true)] [InlineData(true, false)]
    public async Task Wrong_windows_edition_or_wrong_user_blocks_application_installation(bool windows, bool account)
    {
        bool cleaned = false, executed = false;
        var result = await DeploymentPreparation.Run(() => { cleaned = true; return Task.CompletedTask; }, () => windows, () => account,
            () => { executed = true; return Task.FromResult(new EquipmentEvent("result", "Done", ExitCode: 0)); });
        Assert.True(cleaned); Assert.False(executed); Assert.NotEqual(0, result.Components.ExitCode);
        Assert.Equal(windows, result.WindowsVerified); Assert.Equal(account, result.AccountVerified);
    }
    [Fact]
    public async Task Verified_windows_and_user_allow_components_only_after_cleanup_and_preserve_reboot_result()
    {
        var order = new List<string>();
        var result = await DeploymentPreparation.Run(() => { order.Add("clean"); return Task.CompletedTask; },
            () => { order.Add("windows"); return true; }, () => { order.Add("account"); return true; },
            () => { order.Add("components"); return Task.FromResult(new EquipmentEvent("result", "Verified", ExitCode: 0, RebootRequired: true)); });
        Assert.Equal(new[] { "clean", "windows", "account", "components" }, order);
        Assert.True(result.WindowsVerified); Assert.True(result.AccountVerified); Assert.True(result.Components.RebootRequired);
    }
}
