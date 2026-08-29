using Kiosk.Server.Services;
using KioskClinicaPC.Core.Sync;
using Xunit;

namespace Kiosk.Server.Tests;

public sealed class MaintenanceJobStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "kiosk-maintenance-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Token_is_scoped_to_device_and_terminal_jobs_cannot_be_reused()
    {
        var store = new MaintenanceJobStore(_root);
        var created = store.Create(new FleetDevice { Id = "d1", Name = "PC1" });

        Assert.NotNull(store.Authorize(created.Job.Id, created.Token, "d1"));
        Assert.Null(store.Authorize(created.Job.Id, created.Token, "other"));
        Assert.Null(store.Authorize(created.Job.Id, "not-a-token", "d1"));

        store.MarkDispatched(created.Job.Id);
        Assert.True(store.Update(created.Job.Id, created.Token, new MaintenanceStatusUpdate
        { DeviceId = "d1", State = MaintenanceJobState.Uninstalling }));
        Assert.True(store.Update(created.Job.Id, created.Token, new MaintenanceStatusUpdate
        { DeviceId = "d1", State = MaintenanceJobState.Succeeded, ExitCode = 0 }));
        Assert.Null(store.Authorize(created.Job.Id, created.Token, "d1"));
    }

    [Fact]
    public void Only_one_active_maintenance_is_allowed_per_device()
    {
        var store = new MaintenanceJobStore(_root);
        var device = new FleetDevice { Id = "d1", Name = "PC1" };
        store.Create(device);
        Assert.Throws<InvalidOperationException>(() => store.Create(device));
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
}
