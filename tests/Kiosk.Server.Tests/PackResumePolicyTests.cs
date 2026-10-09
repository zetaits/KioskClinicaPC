using KioskClinicaPC.Core.Sync;
using KioskClinicaPC.Equipment;
using Xunit;
namespace Kiosk.Server.Tests;
public sealed class PackResumePolicyTests
{
    private static PackApplication App => new(new string('a', 32), "Vendor.App", "App", "2");
    [Fact]
    public void Latest_resume_keeps_verified_concrete_version_and_resolves_only_pending_items_again()
    {
        var other = App with { Id = new string('b', 32), WingetId = "Vendor.Other" };
        var definition = PackDefinition.FromCatalog(new(8, [App, other]));
        var previous = new PackRun { Items = [new() { Application = App with { PinnedVersion = "1" }, State = PackItemState.Succeeded, InstalledVersion = "1", ResolvedChannel = "" },
            new() { Application = other with { PinnedVersion = "1" }, State = PackItemState.Failed }] };
        var resumed = PackResumePolicy.Create(definition.ForExecution(), previous, true);
        Assert.Equal("1", resumed.Items[0].Application.PinnedVersion); Assert.False(resumed.Items[0].ResolveLatest);
        Assert.Equal("1", resumed.Items[0].InstalledVersion); Assert.True(resumed.Items[1].ResolveLatest);
        Assert.Empty(resumed.Items[1].Application.PinnedVersion);
        Assert.All(PackResumePolicy.Create(definition.ForExecution(), previous, false).Items, i => Assert.True(i.ResolveLatest));
        Assert.Equal("1", previous.Items[0].Application.PinnedVersion);
    }
    [Fact]
    public void Resume_uses_authorized_versions_and_carries_verification_guards_even_if_resume_is_disabled()
    {
        var previous = new PackRun { Items = [new() { Application = App with { PinnedVersion = "1" }, State = PackItemState.Installing }] };
        var run = PackResumePolicy.Create(new(7, [App]), previous, false);
        var item = Assert.Single(run.Items);
        Assert.Equal("2", item.Application.PinnedVersion); Assert.Equal(PackItemState.Pending, item.State); Assert.True(item.RequiresRebootBeforeRetry);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Reboot_clears_previous_native_guard_before_a_new_preflight_even_when_resuming(bool resume)
    {
        var previous = new PackRun { Items = [new() { Application = App, State = PackItemState.VerificationPending,
            RequiresRebootBeforeRetry = true }] };
        previous.Items[0].LastAttemptBootTimeUtc = previous.HostBootTimeUtc.AddDays(-1);
        var item = Assert.Single(PackResumePolicy.Create(new(7, [App]), previous, resume).Items);
        Assert.False(item.RequiresRebootBeforeRetry);
        Assert.True(previous.Items[0].RequiresRebootBeforeRetry);
    }
    [Fact]
    public void New_selection_does_not_bypass_a_native_installer_outside_the_selection()
    {
        var previous = new PackRun { Items = [new() { Application = App, State = PackItemState.VerificationPending }] };
        var selection = new PackCatalog(7, [App with { Id = new string('b', 32), WingetId = "Vendor.Other" }]);
        Assert.Throws<NativeStateException>(() => PackResumePolicy.Create(selection, previous, false));
        previous.HostBootTimeUtc = previous.HostBootTimeUtc.AddDays(-1);
        Assert.Single(PackResumePolicy.Create(selection, previous, true).Items);
    }
    [Fact]
    public void Reboot_flag_is_preserved_until_the_boot_changes_and_verified_state_is_rechecked_by_execution()
    {
        var previous = new PackRun { Items = [new() { Application = App, State = PackItemState.Succeeded, RebootRequired = true }] };
        var resumed = PackResumePolicy.Create(new(7, [App]), previous, true);
        Assert.Equal(PackItemState.Succeeded, Assert.Single(resumed.Items).State); Assert.True(resumed.RebootRequired);
        previous.HostBootTimeUtc = previous.HostBootTimeUtc.AddDays(-1);
        Assert.False(PackResumePolicy.Create(new(7, [App]), previous, true).RebootRequired);
    }
}
