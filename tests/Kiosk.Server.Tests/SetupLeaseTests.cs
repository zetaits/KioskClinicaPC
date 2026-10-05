using KioskClinicaPC.Equipment;
using Xunit;
namespace Kiosk.Server.Tests;
public sealed class SetupLeaseTests
{
    [Fact]
    public void Coordinator_and_legacy_worker_locks_exclude_competing_installs_and_release_after_failure()
    {
        string root = Path.Combine(Path.GetTempPath(), "equipment-lease-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            using (var coordinator = SetupLease.Equipment(root, true))
            {
                Assert.Throws<IOException>(() => SetupLease.Equipment(root, true));
                using var worker = SetupLease.Pack(root);
                Assert.Throws<IOException>(() => SetupLease.Pack(root));
            }
            using (var legacyWorker = SetupLease.Pack(root))
                Assert.Throws<IOException>(() => SetupLease.Equipment(root, false));
            // A failed acquisition must not leave equipment.lock held.
            using var kioskOnly = SetupLease.Equipment(root, false);
            Assert.Throws<IOException>(() => SetupLease.Pack(root));
        }
        finally { Directory.Delete(root, true); }
    }
}
