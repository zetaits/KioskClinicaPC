using System.Security.Cryptography;
using System.Text.Json;
using Kiosk.Server.Services;
using KioskClinicaPC.Core.Sync;
using Xunit;

namespace Kiosk.Server.Tests;

public sealed class KioskUpdateStoreTests : IDisposable
{
    private const string DeviceId = "11111111111111111111111111111111";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "kiosk-update-tests", Guid.NewGuid().ToString("N"));
    private readonly ECDsa _signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    [Fact]
    public async Task Signed_release_is_assigned_and_only_the_scoped_token_authorizes_it()
    {
        var store = Store();
        await Import(store, "1.2.0");
        store.Activate("1.2.0", [Device("1.1.0")]);

        KioskUpdateAssignment assignment = store.GetAssignment(DeviceId, "PC 1", "1.1.0")!;
        Assert.Equal("1.2.0", assignment.Version);
        Assert.Equal(KioskUpdateOperation.Upgrade, assignment.Operation);
        Assert.NotNull(store.Authorize(assignment.JobId, assignment.Token, DeviceId));
        Assert.Null(store.Authorize(assignment.JobId, new string('0', 64), DeviceId));

        Assert.True(store.Update(assignment.JobId, assignment.Token, new KioskUpdateStatusUpdate
        { DeviceId = DeviceId, State = KioskUpdateState.AwaitingRestart }));
        Assert.Null(store.GetAssignment(DeviceId, "PC 1", "1.2.0"));
        Assert.Equal(KioskUpdateState.Succeeded, store.RecentJobs().Single().State);
    }

    [Fact]
    public async Task Activating_an_older_signed_release_creates_an_explicit_rollback()
    {
        var store = Store();
        await Import(store, "1.1.0");
        store.Activate("1.1.0", [Device("1.2.0")]);

        Assert.Equal(KioskUpdateOperation.Rollback,
            store.GetAssignment(DeviceId, "PC 1", "1.2.0")!.Operation);
    }

    [Fact]
    public async Task Tampered_manifest_or_binary_is_rejected()
    {
        var store = Store();
        (byte[] manifest, string signature, byte[] setup) = Signed("1.2.0");
        string tamperedJson = System.Text.Encoding.UTF8.GetString(manifest)
            .Replace("\"version\":\"1.2.0\"", "\"version\":\"1.2.9\"", StringComparison.Ordinal);
        manifest = System.Text.Encoding.UTF8.GetBytes(tamperedJson);
        await Assert.ThrowsAnyAsync<CryptographicException>(() => store.ImportAsync(
            manifest, signature, "Setup-KioskClinicaPC-1.2.0.exe", new MemoryStream(setup)));

        (manifest, signature, setup) = Signed("1.2.1");
        setup[0] ^= 1;
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ImportAsync(
            manifest, signature, "Setup-KioskClinicaPC-1.2.1.exe", new MemoryStream(setup)));
    }

    [Fact]
    public async Task Only_the_ten_most_recent_inactive_releases_are_retained()
    {
        var store = Store();
        for (int patch = 0; patch < 11; patch++) await Import(store, $"2.0.{patch}");

        Assert.Equal(10, store.Releases().Count);
        Assert.DoesNotContain(store.Releases(), release => release.Version == "2.0.0");
        Assert.False(Directory.Exists(Path.Combine(_root, "updates", "2.0.0")));
    }

    private KioskUpdateStore Store() => new(_root, Path.Combine(_root, "updates"), 1024 * 1024,
        TimeZoneInfo.Utc, "unit-test-token-secret", new Dictionary<string, string> { ["test"] = _signer.ExportSubjectPublicKeyInfoPem() });

    private async Task Import(KioskUpdateStore store, string version)
    {
        var signed = Signed(version);
        await store.ImportAsync(signed.manifest, signed.signature, $"Setup-KioskClinicaPC-{version}.exe", new MemoryStream(signed.setup));
    }

    private (byte[] manifest, string signature, byte[] setup) Signed(string version)
    {
        byte[] setup = [0x4d, 0x5a, 1, 2, 3, 4];
        var manifest = new KioskReleaseManifest
        {
            Version = version, FileName = $"Setup-KioskClinicaPC-{version}.exe", SizeBytes = setup.Length,
            Sha256 = Convert.ToHexString(SHA256.HashData(setup)).ToLowerInvariant(), PublishedAtUtc = DateTime.UtcNow,
            KeyId = "test", MinimumUpdaterVersion = "1.2.0", GitHubFallbackUrl = "https://github.com/example/release.exe"
        };
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        string signature = Convert.ToBase64String(_signer.SignData(bytes, HashAlgorithmName.SHA256));
        return (bytes, signature, setup);
    }

    private static FleetDevice Device(string version) => new() { Id = DeviceId, Name = "PC 1", AppVersion = version };
    public void Dispose() { _signer.Dispose(); try { Directory.Delete(_root, true); } catch { } }
}
