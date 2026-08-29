using Kiosk.Server.Services;
using KioskClinicaPC.Core.Sync;
using Xunit;

namespace Kiosk.Server.Tests;

public sealed class InstallationJobStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "kiosk-job-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Token_is_scoped_to_job_and_device_and_terminal_job_cannot_be_reused()
    {
        var store = new InstallationJobStore(_root);
        var created = store.Create(Device(), Package());

        Assert.NotNull(store.Authorize(created.Job.Id, created.Token, "d1"));
        Assert.Null(store.Authorize(created.Job.Id, "00", "d1"));
        Assert.Null(store.Authorize(created.Job.Id, created.Token, "other"));

        store.MarkDispatched(created.Job.Id);
        Assert.True(store.Update(created.Job.Id, created.Token, new InstallationStatusUpdate
        { DeviceId = "d1", State = InstallationJobState.Installing }));
        Assert.True(store.Update(created.Job.Id, created.Token, new InstallationStatusUpdate
        { DeviceId = "d1", State = InstallationJobState.Succeeded, ExitCode = 0 }));
        Assert.Null(store.Authorize(created.Job.Id, created.Token, "d1"));
    }

    [Fact]
    public void Only_one_active_job_is_allowed_per_device()
    {
        var store = new InstallationJobStore(_root);
        store.Create(Device(), Package());
        Assert.Throws<InvalidOperationException>(() => store.Create(Device(), Package()));
    }

    [Fact]
    public void Invalid_or_backward_transitions_are_rejected()
    {
        var store = new InstallationJobStore(_root);
        var created = store.Create(Device(), Package());
        store.MarkDispatched(created.Job.Id);
        Assert.True(store.Update(created.Job.Id, created.Token, new InstallationStatusUpdate
        { DeviceId = "d1", State = InstallationJobState.Installing }));
        Assert.False(store.Update(created.Job.Id, created.Token, new InstallationStatusUpdate
        { DeviceId = "d1", State = InstallationJobState.Downloading }));
    }

    private static FleetDevice Device() => new() { Id = "d1", Name = "PC 1", ConnectionId = "c1", InstallerAgentVersion = "1.0.0" };
    private static InstallerPackage Package() => new() { Id = "p1", DisplayName = "Chrome", Sha256 = new string('a', 64) };
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
}
