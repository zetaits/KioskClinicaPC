using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using Kiosk.Deployment;
using Kiosk.Server.Services;
using KioskClinicaPC.Core.Sync;
using Xunit;

namespace Kiosk.Server.Tests;

public sealed class DeploymentTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "clinicapc-deployment-tests", Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 10, 0, 0, TimeSpan.Zero);
    private sealed class Clock : TimeProvider { public DateTimeOffset Now { get; set; } = DeploymentTests.Now; public override DateTimeOffset GetUtcNow() => Now; }
    private static DeploymentImage Image => new(new string('a', 64), "Windows 11", "x64", 26100, "es-ES",
        [new(1, "Windows 11 Home", "Core"), new(6, "Windows 11 Pro", "Professional")], true, Now);
    private static DeploymentProfile Profile => new(new string('b', 32), 1, "Solo Windows", Image.Id, 6, "es-ES", "Usuario", new PackCatalog(1, []), false);
    private static BootSession Session(int number = 1) => new(number.ToString("x32"), $"PC{number}", Now,
        new("Vendor", "Model", "Serial" + number, number.ToString("x8") + "-0000-0000-0000-000000000001", number.ToString("x12"), true, true,
            8L * 1024 * 1024 * 1024, [new(0, "disk" + number, "NVMe", "DS" + number, 256L * 1024 * 1024 * 1024, "NVMe", true)]));
    private DeploymentQueue Queue(int sessions = 1)
    {
        var queue = new DeploymentQueue(Path.Combine(_root, Guid.NewGuid() + ".json")); queue.AddImage(Image);
        for (int i = 1; i <= sessions; i++) queue.Observe(Session(i)); return queue;
    }
    private static DeploymentConfirmation Confirmation(int count = 1) => new(Guid.NewGuid().ToString("N"), Profile.Id, Profile.Revision,
        Enumerable.Range(1, count).Select(i => new DeploymentSelection(Session(i).Id, "disk" + i, "Usuario", 0)).ToList());
    private static DeploymentJob ConfirmAndClaim(DeploymentQueue queue)
    {
        var batch = queue.Confirm(Confirmation(), Profile, true, Now); queue.Acknowledge(batch.JobIds);
        return queue.Claim(Session().Id, _ => true, Now)!;
    }
    [Fact]
    public void Enrollment_codes_expire_are_single_use_and_store_only_hashes()
    {
        var clock = new Clock(); var store = new DeploymentStore(_root, clock); var code = store.CreateCode();
        var station = store.Enroll(new(1, code.Code, "Station"));
        Assert.Equal(station.StationId, store.Authenticate(station.Credential));
        Assert.Null(store.Authenticate("fleet-key")); Assert.Null(store.Authenticate("setup-key"));
        Assert.Throws<InvalidDataException>(() => store.Enroll(new(1, code.Code, "Another")));
        string persisted = File.ReadAllText(Path.Combine(_root, "deployment-v1.json"));
        Assert.DoesNotContain(code.Code, persisted); Assert.DoesNotContain(station.Credential, persisted);
        var expired = store.CreateCode(); clock.Now = expired.ExpiresAtUtc;
        Assert.Throws<InvalidDataException>(() => store.Enroll(new(1, expired.Code, "Expired")));
        new DeploymentStore(_root, clock).Revoke(station.StationId);
        Assert.Null(new DeploymentStore(_root, clock).Authenticate(station.Credential));
    }
    [Theory]
    [InlineData(0)] [InlineData(9)]
    public void Invalid_capacity_is_rejected(int value) => Assert.Throws<InvalidDataException>(() => Queue().Capacity(value));
    [Fact]
    public void Confirmation_requires_online_panel_and_acknowledgement_before_destructive_claim()
    {
        var queue = Queue(); var confirm = Confirmation();
        Assert.Throws<InvalidDataException>(() => queue.Confirm(confirm, Profile, false, Now));
        var batch = queue.Confirm(confirm, Profile, true, Now);
        Assert.Null(queue.Claim(Session().Id, _ => true, Now));
        queue.Acknowledge(batch.JobIds);
        Assert.NotNull(queue.Claim(Session().Id, _ => true, Now));
        Assert.Null(queue.Claim(Session().Id, _ => true, Now));
    }
    [Fact]
    public void Batch_is_idempotent_and_each_target_is_reserved_once()
    {
        var queue = Queue(3); var request = Confirmation(3); var batch = queue.Confirm(request, Profile, true, Now);
        Assert.Equal(batch.Id, queue.Confirm(request, Profile, true, Now).Id); Assert.Equal(3, queue.Snapshot().Jobs.Count);
        Assert.Throws<InvalidDataException>(() => queue.Confirm(Confirmation(3), Profile, true, Now));
        Assert.Throws<InvalidDataException>(() => queue.Confirm(request with { Targets = [new(Session().Id, "disk1", "Different", 0)] }, Profile, true, Now));
    }
    [Fact]
    public void Three_jobs_run_while_the_fourth_waits_and_one_failure_does_not_stop_others()
    {
        var queue = Queue(4); var batch = queue.Confirm(Confirmation(4), Profile, true, Now); queue.Acknowledge(batch.JobIds);
        var first = queue.Claim(Session(1).Id, _ => true, Now)!;
        Assert.NotNull(queue.Claim(Session(2).Id, _ => true, Now)); Assert.NotNull(queue.Claim(Session(3).Id, _ => true, Now));
        Assert.Null(queue.Claim(Session(4).Id, _ => true, Now));
        queue.Progress(new(first.Id, 1, DeploymentState.Attention, "Setup failed", null));
        Assert.NotNull(queue.Claim(Session(4).Id, _ => true, Now));
        Assert.Equal(3, queue.Snapshot().Jobs.Count(j => DeploymentPolicy.Active(j.State)));
    }
    [Fact]
    public async Task Concurrent_claims_cannot_exceed_capacity_or_duplicate_target()
    {
        var queue = Queue(8); queue.Capacity(2); var batch = queue.Confirm(Confirmation(8), Profile, true, Now); queue.Acknowledge(batch.JobIds);
        await Task.WhenAll(Enumerable.Range(1, 8).Select(i => Task.Run(() => queue.Claim(Session(i).Id, _ => true, Now))));
        Assert.Equal(2, queue.Snapshot().Jobs.Count(j => j.DestructiveStarted));
    }
    [Fact]
    public void Profile_and_pending_username_changes_force_review_then_freeze_options()
    {
        var queue = Queue();
        Assert.Throws<InvalidDataException>(() => queue.Confirm(Confirmation(), Profile with { Revision = 2 }, true, Now));
        queue.PendingOptions([Session() with { PendingUsername = "Encargado", OptionsRevision = 1 }]);
        Assert.Throws<InvalidDataException>(() => queue.Confirm(Confirmation(), Profile, true, Now));
        var request = Confirmation() with { Targets = [new(Session().Id, "disk1", "Encargado", 1)] };
        queue.Confirm(request, Profile, true, Now);
        queue.PendingOptions([Session() with { PendingUsername = "Changed", OptionsRevision = 2 }]);
        Assert.Equal("Encargado", queue.Snapshot().Jobs.Single().Username);
        Assert.Equal(1, queue.Snapshot().Jobs.Single().Profile.Revision);
    }
    [Fact]
    public void Ambiguous_identity_disk_change_usb_and_missing_disk_are_blocked()
    {
        var queue = Queue(2);
        queue.Observe(Session(2) with { Hardware = Session(2).Hardware with { Uuid = Session(1).Hardware.Uuid } });
        Assert.Throws<InvalidDataException>(() => queue.Confirm(Confirmation(), Profile, true, Now));
        var usbQueue = Queue(); usbQueue.Observe(Session() with { Hardware = Session().Hardware with { Disks = [Session().Hardware.Disks[0] with { BusType = "USB" }] } });
        Assert.Throws<InvalidDataException>(() => usbQueue.Confirm(Confirmation(), Profile, true, Now));
        var changed = Queue(); var batch = changed.Confirm(Confirmation(), Profile, true, Now); changed.Acknowledge(batch.JobIds);
        changed.Observe(Session() with { Hardware = Session().Hardware with { Disks = [Session().Hardware.Disks[0] with { UniqueId = "changed" }] } });
        Assert.Null(changed.Claim(Session().Id, _ => true, Now)); Assert.Equal(DeploymentState.Attention, changed.Snapshot().Jobs.Single().State);
        Assert.False(changed.Snapshot().Jobs.Single().DestructiveStarted);
    }
    [Fact]
    public void Multi_disk_selection_keeps_exact_disk_and_unattend_wipes_only_that_disk()
    {
        var queue = Queue(); var disk = Session().Hardware.Disks[0] with { Number = 2, UniqueId = "second" };
        queue.Observe(Session() with { Hardware = Session().Hardware with { Disks = [Session().Hardware.Disks[0], disk] } });
        var request = Confirmation() with { Targets = [new(Session().Id, "second", "Usuario", 0)] }; queue.Confirm(request, Profile, true, Now);
        string secret = "Secret<&>\"123";
        var doc = XDocument.Parse(UnattendWriter.Create(queue.Snapshot().Jobs.Single(), secret)); XNamespace ns = "urn:schemas-microsoft-com:unattend";
        Assert.All(doc.Descendants(ns + "DiskID"), e => Assert.Equal("2", e.Value));
        Assert.Single(doc.Descendants(ns + "WillWipeDisk")); Assert.Equal("6", doc.Descendants(ns + "MetaData").Single().Element(ns + "Value")!.Value);
        Assert.Equal(secret, doc.Descendants(ns + "LocalAccount").Single().Element(ns + "Password")!.Element(ns + "Value")!.Value);
        Assert.Equal("1", doc.Descendants(ns + "LogonCount").Single().Value);
        Assert.DoesNotContain("Secret", JsonSerializer.Serialize(queue.Snapshot()));
    }
    [Theory]
    [InlineData("User/Name")] [InlineData("Administrador")] [InlineData("SYSTEM")] [InlineData("Trailing.")] [InlineData(" ")] [InlineData("WayTooLongUsername1234567")]
    public void Unsafe_account_names_are_rejected(string name) => Assert.Throws<InvalidDataException>(() => DeploymentPolicy.Username(name));
    [Fact]
    public void Restart_and_reconnection_never_repeat_destructive_claim()
    {
        string path = Path.Combine(_root, "restart.json"); var queue = new DeploymentQueue(path); queue.AddImage(Image); queue.Observe(Session());
        var job = ConfirmAndClaim(queue); var restored = new DeploymentQueue(path); restored.Observe(Session());
        Assert.Equal(DeploymentState.Attention, restored.Snapshot().Jobs.Single().State);
        Assert.True(restored.Snapshot().Jobs.Single().DestructiveStarted);
        Assert.Null(restored.Claim(Session().Id, _ => true, Now));
        Assert.Throws<InvalidDataException>(() => restored.Confirm(Confirmation(), Profile, true, Now));
    }
    [Fact]
    public void Duplicate_events_are_idempotent_and_success_requires_all_verifications()
    {
        var queue = Queue(); var job = ConfirmAndClaim(queue);
        var progress = new DeploymentProgress(job.Id, 1, DeploymentState.PostInstall, "Preparing desktop", null);
        queue.Progress(progress); queue.Progress(progress); Assert.Equal(1, queue.Snapshot().Jobs.Single().LastSequence);
        Assert.Throws<InvalidDataException>(() => queue.Progress(new(job.Id, 3, DeploymentState.Completed, "Skipped", 100, true, true, true)));
        Assert.Throws<InvalidDataException>(() => queue.Progress(new(job.Id, 2, DeploymentState.Completed, "Unverified", 100, true, false, true)));
        queue.Progress(new(job.Id, 2, DeploymentState.Completed, "Verified", null, true, true, true, true));
        Assert.True(queue.Snapshot().Jobs.Single().RebootRequired);
    }
    [Fact]
    public void Recovery_retries_components_only_and_cancellation_is_limited_to_queue()
    {
        var queue = Queue(2); var batch = queue.Confirm(Confirmation(2), Profile, true, Now); queue.Acknowledge(batch.JobIds);
        queue.Cancel(batch.JobIds[1]); var job = queue.Claim(Session().Id, _ => true, Now)!;
        Assert.Throws<InvalidDataException>(() => queue.Cancel(job.Id));
        queue.Progress(new(job.Id, 1, DeploymentState.Attention, "Application failed", null, true, true, false));
        queue.ResumeComponents(job.Id); Assert.Equal(DeploymentState.PostInstall, queue.Snapshot().Jobs[0].State);
        Assert.Null(queue.Claim(Session().Id, _ => true, Now));
        queue.Progress(new(job.Id, 2, DeploymentState.Completed, "Apps verified", null, true, true, true));
    }
    [Fact]
    public void Corrupt_state_and_image_integrity_fail_closed_and_job_images_are_retained()
    {
        var queue = Queue(); var batch = queue.Confirm(Confirmation(), Profile, true, Now); queue.Acknowledge(batch.JobIds);
        Assert.Throws<InvalidDataException>(() => queue.RemoveImage(Image.Id));
        Assert.Null(queue.Claim(Session().Id, _ => false, Now));
        Assert.False(queue.Snapshot().Jobs.Single().DestructiveStarted);
        string path = Path.Combine(_root, "corrupt.json"); File.WriteAllText(path, "{");
        Assert.Throws<JsonException>(() => new DeploymentQueue(path)); Assert.Equal("{", File.ReadAllText(path));
    }
    [Theory]
    [InlineData("x86", 26100, "es-ES", true)] [InlineData("x64", 22000, "es-ES", true)] [InlineData("x64", 26100, "en-US", true)] [InlineData("x64", 26100, "es-ES", false)]
    public void Unsupported_images_are_rejected(string architecture, int build, string language, bool verified)
        => Assert.Throws<InvalidDataException>(() => DeploymentPolicy.Image(Image with { Architecture = architecture, Build = build, Language = language, Verified = verified }));
    [Fact]
    public void Panel_profile_revisions_and_pending_options_cannot_mutate_confirmed_jobs()
    {
        var store = new DeploymentStore(_root, new Clock()); var code = store.CreateCode(); var station = store.Enroll(new(1, code.Code, "Station"));
        store.Sync(station.StationId, new(1, 3, [Image], [Session()], [], []));
        var app = new PackApplication(new string('c', 32), "Vendor.App", "App", "1.2"); var catalog = new PackCatalog(1, [app]);
        var profile = store.SaveProfile(new(null, 0, "With pack", Image.Id, 6, "es-ES", "Usuario", [app.Id], false), catalog);
        var queue = Queue(); var confirm = Confirmation() with { ProfileId = profile.Id, ProfileRevision = profile.Revision };
        var batch = queue.Confirm(confirm, profile, true, Now);
        store.PendingUsername(station.StationId, Session().Id, new("Changed", 0));
        var q = queue.Snapshot(); Assert.Throws<InvalidDataException>(() => store.Sync(station.StationId, new(1, 3, q.Images, q.Sessions, q.Batches, q.Jobs)));
        queue = Queue(); queue.PendingOptions(store.Configuration(station.StationId, catalog).PendingOptions);
        confirm = confirm with { Id = Guid.NewGuid().ToString("N"), Targets = [new(Session().Id, "disk1", "Changed", 1)] };
        queue.Confirm(confirm, profile, true, Now); q = queue.Snapshot(); store.Sync(station.StationId, new(1, 3, q.Images, q.Sessions, q.Batches, q.Jobs));
        Assert.Throws<InvalidDataException>(() => store.PendingUsername(station.StationId, Session().Id, new("Late", 1)));
        store.SaveProfile(new(profile.Id, profile.Revision, "New version", Image.Id, 6, "es-ES", "NewUser", [app.Id], false), catalog with { Revision = 2, Applications = [app with { PinnedVersion = "2.0" }] });
        var frozen = store.Snapshot().Jobs[station.StationId].Single(); Assert.Equal("Changed", frozen.Username); Assert.Equal("1.2", frozen.Profile.Applications.Applications.Single().PinnedVersion);
        Assert.Equal(2, store.Snapshot().Profiles.Single(p => p.Id == profile.Id).Revision);
    }
    [Fact]
    public async Task Only_compatible_validated_private_packages_are_offered_and_hash_changes_remove_download()
    {
        byte[] bytes = [1, 2, 3, 4]; var hashes = new Dictionary<string, string> { ["ipxe-shim.efi"] = new('a', 64), ["ipxe.efi"] = new('b', 64), ["wimboot"] = new('c', 64), ["boot.wim"] = new('d', 64) };
        var release = new DeploymentRelease(1, 1, "deployment-wpf", "0.1.0", "Setup-InstalacionRedClinicaPC-0.1.0.exe", 4,
            Convert.ToHexString(SHA256.HashData(bytes)), new('a', 40), "10.1.26100.9457", hashes, true);
        var store = new DeploymentReleaseStore(_root); Assert.Null(store.Latest());
        await Assert.ThrowsAsync<InvalidDataException>(() => store.Import(release with { RealValidationPassed = false }, new MemoryStream(bytes), default));
        await store.Import(release, new MemoryStream(bytes), default); Assert.NotNull(store.Latest());
        await Assert.ThrowsAsync<InvalidDataException>(() => store.Import(release, new MemoryStream(bytes), default));
        File.WriteAllBytes(store.Latest()!.Value.Path, [5, 6, 7, 8]); Assert.Null(store.Latest());
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
