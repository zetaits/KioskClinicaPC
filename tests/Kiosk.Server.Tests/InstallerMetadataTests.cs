using System.Security.Cryptography;
using System.Text.Json;
using Kiosk.Deployment;
using Kiosk.Server.Services;
using KioskClinicaPC.Equipment;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kiosk.Server.Tests;

public sealed class InstallerMetadataTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "installer-metadata-" + Guid.NewGuid().ToString("N"));
    private readonly SetupReleaseStore _setups;
    private readonly DeploymentReleaseStore _deployments;
    private readonly InstallerPageQuery _query;

    public InstallerMetadataTests()
    {
        _setups = new(_root);
        _deployments = new(_root);
        _query = new(_setups, _deployments, NullLogger<InstallerPageQuery>.Instance);
    }

    private async Task<SetupRelease> PublishSetup(string version)
    {
        var (release, bytes) = SetupReleaseTests.Bundle(version);
        var files = bytes.ToDictionary(entry => entry.Key, entry => (
            Name: entry.Key is "online" or "complete" ? release.Editions.Single(e => e.Edition == entry.Key).FileName
                : entry.Key == "worker" ? "worker.zip" : "kiosk.exe",
            Stream: (Stream)new MemoryStream(entry.Value)));
        try { await _setups.Import(release, files, default); }
        finally { foreach (var file in files.Values) file.Stream.Dispose(); }
        return release;
    }

    internal static DeploymentRelease DeploymentBundle(string version, byte[] bytes) => new(1, 1, "deployment-wpf", version,
        $"Setup-InstalacionRedClinicaPC-{version}.exe", bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)),
        new('a', 40), "10.1.26100.9457", new Dictionary<string, string>
        { ["ipxe-shim.efi"] = new('a', 64), ["ipxe.efi"] = new('b', 64), ["wimboot"] = new('c', 64), ["boot.wim"] = new('d', 64) }, true);

    [Fact]
    public async Task No_publications_is_empty_and_a_new_visit_observes_publication_and_activation()
    {
        Assert.Equal(InstallerSectionStatus.Empty, (await _query.GetSetupSummaryAsync(default)).Status);
        Assert.Equal(InstallerSectionStatus.Empty, (await _query.GetDeploymentSummaryAsync(default)).Status);
        await PublishSetup("1.5.9");
        Assert.Null((await _query.GetSetupSummaryAsync(default)).Data!.ActiveVersion);
        await _setups.Activate("1.5.9", default);
        Assert.Equal("1.5.9", (await _query.GetSetupSummaryAsync(default)).Data!.ActiveVersion);
    }

    [Fact]
    public async Task Latest_is_numeric_and_does_not_open_old_manifests_or_binary_content()
    {
        var old = await PublishSetup("1.5.9");
        var latest = await PublishSetup("1.5.10");
        using var oldManifest = new FileStream(Path.Combine(_root, "v3", "releases", old.Version, "manifest.json"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using var binary = new FileStream(_setups.EditionPath(latest, "complete"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var summary = await _query.GetSetupSummaryAsync(default);
        Assert.Equal(InstallerSectionStatus.Ready, summary.Status);
        Assert.Equal(latest.Version, summary.Data!.Latest.Version);
        Assert.Equal(new[] { "online", "complete" }, summary.Data.Latest.Editions.Select(e => e.Edition));
        Assert.Null(_setups.Find(latest.Version)); // The verified path still attempts to read the locked binary.
    }

    [Fact]
    public async Task Same_size_corruption_can_be_listed_but_cannot_be_activated_or_verified()
    {
        var release = await PublishSetup("1.5.9");
        File.WriteAllBytes(_setups.EditionPath(release, "online"), [3, 2, 1]);
        Assert.Equal(release.Version, (await _query.GetSetupSummaryAsync(default)).Data!.Latest.Version);
        Assert.Null(_setups.Find(release.Version));
        await Assert.ThrowsAsync<InvalidDataException>(() => _setups.Activate(release.Version, default));
    }

    [Fact]
    public async Task History_is_ordered_excludes_displayed_version_and_observes_new_publications()
    {
        await PublishSetup("1.5.8"); await PublishSetup("1.5.9"); await PublishSetup("1.5.10");
        var history = await _query.GetHistoryAsync("1.5.10", default);
        Assert.Equal(new[] { "1.5.9", "1.5.8" }, history.Data!.Releases.Select(r => r.Version));
        await PublishSetup("1.6.0");
        Assert.Equal("1.6.0", (await _query.GetSetupSummaryAsync(default)).Data!.Latest.Version);
        Assert.Equal(3, (await _query.GetHistoryAsync("1.6.0", default)).Data!.Releases.Length);
    }

    [Fact]
    public async Task Single_publication_has_an_empty_history()
    {
        await PublishSetup("1.5.9");
        Assert.Equal(InstallerSectionStatus.Empty, (await _query.GetHistoryAsync("1.5.9", default)).Status);
    }

    [Theory]
    [InlineData("json")]
    [InlineData("missing")]
    [InlineData("size")]
    [InlineData("name")]
    public async Task Unusable_latest_is_skipped_and_an_unusable_only_candidate_is_an_error(string damage)
    {
        var release = await PublishSetup("1.5.9");
        string manifest = Path.Combine(_root, "v3", "releases", release.Version, "manifest.json");
        switch (damage)
        {
            case "json": File.WriteAllText(manifest, "{"); break;
            case "missing": File.Delete(_setups.EditionPath(release, "online")); break;
            case "size": File.WriteAllBytes(_setups.EditionPath(release, "online"), [1]); break;
            case "name": File.WriteAllText(manifest, JsonSerializer.Serialize(release with
                { Editions = [release.Editions[0] with { FileName = "../escape.exe" }, release.Editions[1]] }, new JsonSerializerOptions(JsonSerializerDefaults.Web))); break;
        }
        Assert.Equal(InstallerSectionStatus.Error, (await _query.GetSetupSummaryAsync(default)).Status);
        await PublishSetup("1.5.8");
        Assert.Equal("1.5.8", (await _query.GetSetupSummaryAsync(default)).Data!.Latest.Version);
        Assert.Equal(InstallerSectionStatus.Error, (await _query.GetHistoryAsync("1.5.8", default)).Status);
    }

    [Fact]
    public async Task Damaged_active_pointer_does_not_hide_the_independent_deployment_section_and_retry_recovers()
    {
        await PublishSetup("1.5.9");
        byte[] bytes = [1, 2, 3, 4];
        await _deployments.Import(DeploymentBundle("0.1.0", bytes), new MemoryStream(bytes), default);
        string pointer = Path.Combine(_root, "v3", "active.json");
        File.WriteAllText(pointer, "{");
        Assert.Equal(InstallerSectionStatus.Error, (await _query.GetSetupSummaryAsync(default)).Status);
        Assert.Equal(InstallerSectionStatus.Ready, (await _query.GetDeploymentSummaryAsync(default)).Status);
        File.Delete(pointer);
        Assert.Equal(InstallerSectionStatus.Ready, (await _query.GetSetupSummaryAsync(default)).Status);
        File.WriteAllText(pointer, "{\"version\":\"9.9.9\"}");
        Assert.Equal(InstallerSectionStatus.Error, (await _query.GetSetupSummaryAsync(default)).Status);
    }

    [Fact]
    public async Task Deployment_metadata_does_not_open_binary_and_full_verification_rejects_same_size_corruption()
    {
        byte[] bytes = [1, 2, 3, 4]; var release = DeploymentBundle("0.1.0", bytes);
        await _deployments.Import(release, new MemoryStream(bytes), default);
        string binaryPath = Path.Combine(_root, "deployment", release.FileName);
        using (var locked = new FileStream(binaryPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.Equal(release.Version, (await _query.GetDeploymentSummaryAsync(default)).Data!.Version);
        File.WriteAllBytes(binaryPath, [4, 3, 2, 1]);
        Assert.Equal(release.Version, (await _query.GetDeploymentSummaryAsync(default)).Data!.Version);
        Assert.Null(_deployments.Find(release.Version));
        File.WriteAllText(binaryPath + ".json", "{");
        Assert.Equal(InstallerSectionStatus.Error, (await _query.GetDeploymentSummaryAsync(default)).Status);
    }

    [Fact]
    public async Task Inaccessible_catalog_is_an_error_and_cancellation_is_not_reported_as_a_storage_error()
    {
        Directory.Delete(Path.Combine(_root, "v3", "releases"));
        Assert.Equal(InstallerSectionStatus.Error, (await _query.GetSetupSummaryAsync(default)).Status);
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _query.GetSetupSummaryAsync(canceled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _query.GetDeploymentSummaryAsync(canceled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _query.GetHistoryAsync("1.5.9", canceled.Token));
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
