using System.Text.Json;
using Kiosk.Server.Services;
using KioskClinicaPC.Equipment;
using Xunit;

namespace Kiosk.Server.Tests;

public sealed class SetupReleaseRetentionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "setup-retention-" + Guid.NewGuid().ToString("N"));
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly SetupReleaseStore _store;

    public SetupReleaseRetentionTests() => _store = new(_root);
    private string ReleaseDirectory(string version) => Path.Combine(_root, "v3", "releases", version);
    private string ComponentFile(SetupComponent component) => Path.Combine(_root, "v3", "components", component.Kind, component.Sha256);
    private int VersionCount => Directory.GetDirectories(Path.Combine(_root, "v3", "releases")).Length;

    private async Task Publish(SetupRelease release, Dictionary<string, byte[]> bytes)
    {
        var files = bytes.ToDictionary(entry => entry.Key, entry => (
            Name: entry.Key is "online" or "complete" ? release.Editions.Single(e => e.Edition == entry.Key).FileName
                : entry.Key == "worker" ? "worker.zip" : "kiosk.exe",
            Stream: (Stream)new MemoryStream(entry.Value)));
        try { await _store.Import(release, files, default); }
        finally { foreach (var file in files.Values) file.Stream.Dispose(); }
    }

    // Reproduce an existing VPS store before the retention feature was deployed.
    private (SetupRelease Release, Dictionary<string, byte[]> Bytes) Seed(string version)
    {
        var bundle = SetupReleaseTests.Bundle(version);
        string directory = ReleaseDirectory(version); Directory.CreateDirectory(directory);
        foreach (var edition in bundle.Release.Editions)
            File.WriteAllBytes(Path.Combine(directory, edition.FileName), bundle.Bytes[edition.Edition]);
        foreach (var component in bundle.Release.Components)
            File.WriteAllBytes(ComponentFile(component), bundle.Bytes[component.Kind]);
        File.WriteAllText(Path.Combine(directory, "manifest.json"), JsonSerializer.Serialize(bundle.Release, Json));
        return bundle;
    }

    [Fact]
    public async Task Sixth_import_removes_oldest_exes_and_unused_worker_but_keeps_shared_kiosk()
    {
        var first = SetupReleaseTests.Bundle("1.5.9");
        await Publish(first.Release, first.Bytes);
        for (int version = 10; version <= 13; version++)
        { var bundle = SetupReleaseTests.Bundle($"1.5.{version}"); await Publish(bundle.Release, bundle.Bytes); }
        Assert.Equal(5, VersionCount); Assert.NotNull(_store.Find("1.5.9"));
        var sixth = SetupReleaseTests.Bundle("1.5.14"); await Publish(sixth.Release, sixth.Bytes);
        Assert.Equal(5, VersionCount);
        Assert.Equal(new[] { "1.5.14", "1.5.13", "1.5.12", "1.5.11", "1.5.10" }, _store.List().Select(r => r.Version));
        Assert.False(Directory.Exists(ReleaseDirectory("1.5.9")));
        Assert.False(File.Exists(ComponentFile(first.Release.Components.Single(c => c.Kind == "worker"))));
        Assert.True(File.Exists(ComponentFile(first.Release.Components.Single(c => c.Kind == "kiosk"))));
        Assert.Null(_store.Find("1.5.9"));
        Assert.Equal(4, (await _store.HistoryMetadataAsync("1.5.14", default)).Length);
    }

    [Fact]
    public async Task Old_active_and_previous_versions_occupy_slots_and_remain_recoverable()
    {
        for (int version = 0; version <= 1; version++)
        {
            var bundle = SetupReleaseTests.Bundle($"1.5.{version}"); await Publish(bundle.Release, bundle.Bytes);
            await _store.Activate(bundle.Release.Version, default);
        }
        for (int version = 2; version <= 7; version++)
        { var bundle = SetupReleaseTests.Bundle($"1.5.{version}"); await Publish(bundle.Release, bundle.Bytes); }
        Assert.Equal(5, VersionCount);
        Assert.Equal(new[] { "1.5.7", "1.5.6", "1.5.5", "1.5.1", "1.5.0" }, _store.List().Select(r => r.Version));
        Assert.Equal("1.5.1", _store.ActiveVersion); Assert.Equal("1.5.0", _store.PreviousVersion);
        Assert.True(_store.Ready());
        await _store.Activate("1.5.7", default);
        var next = SetupReleaseTests.Bundle("1.5.8"); await Publish(next.Release, next.Bytes);
        Assert.Equal(5, VersionCount); Assert.False(Directory.Exists(ReleaseDirectory("1.5.0")));
        Assert.Equal("1.5.7", _store.ActiveVersion); Assert.Equal("1.5.1", _store.PreviousVersion);
        Assert.True(_store.Ready()); Assert.NotNull(_store.Find("1.5.1"));
    }

    [Fact]
    public async Task Hosted_service_cleans_existing_versions_at_startup()
    {
        for (int version = 0; version <= 7; version++) Seed($"1.5.{version}");
        File.WriteAllText(Path.Combine(_root, "v3", "active.json"), "{\"version\":\"1.5.1\",\"previousVersion\":\"1.5.0\"}");
        using var service = new SetupReleaseRetentionService(_store);
        await service.StartAsync(default);
        try { Assert.Equal(5, VersionCount); Assert.True(_store.Ready()); Assert.NotNull(_store.Find("1.5.0")); }
        finally { await service.StopAsync(default); }
    }

    [Fact]
    public async Task Activation_prunes_existing_backlog_with_the_new_recovery_pointer()
    {
        for (int version = 0; version < 7; version++) Seed($"1.5.{version}");
        File.WriteAllText(Path.Combine(_root, "v3", "active.json"), "{\"version\":\"1.5.0\",\"previousVersion\":\"1.5.1\"}");
        await _store.Activate("1.5.6", default);
        Assert.Equal(5, VersionCount);
        Assert.Equal(new[] { "1.5.6", "1.5.5", "1.5.4", "1.5.3", "1.5.0" }, _store.List().Select(r => r.Version));
        Assert.Equal("1.5.6", _store.ActiveVersion); Assert.Equal("1.5.0", _store.PreviousVersion);
        Assert.True(_store.Ready());
    }

    [Fact]
    public async Task Identical_import_retry_also_applies_retention()
    {
        for (int version = 0; version < 5; version++) Seed($"1.5.{version}");
        var latest = Seed("1.5.5");
        await Publish(latest.Release, latest.Bytes);
        Assert.Equal(5, VersionCount); Assert.NotNull(_store.Find("1.5.5"));
        Assert.False(Directory.Exists(ReleaseDirectory("1.5.0")));
    }

    [Fact]
    public async Task Retired_version_number_cannot_be_reimported_even_with_the_original_files()
    {
        var retired = Seed("1.5.0");
        for (int version = 1; version < 6; version++) Seed($"1.5.{version}");
        await _store.Prune(default);
        Assert.Equal(5, VersionCount);
        await Assert.ThrowsAsync<InvalidDataException>(() => Publish(retired.Release, retired.Bytes));
        Assert.Equal(5, VersionCount); Assert.False(Directory.Exists(ReleaseDirectory("1.5.0")));
    }

    [Fact]
    public async Task Failed_import_does_not_remove_existing_versions()
    {
        for (int version = 0; version < 6; version++) Seed($"1.5.{version}");
        var invalid = SetupReleaseTests.Bundle("1.5.6"); invalid.Bytes["online"] = [9, 9, 9];
        await Assert.ThrowsAsync<InvalidDataException>(() => Publish(invalid.Release, invalid.Bytes));
        Assert.Equal(6, VersionCount); Assert.NotNull(_store.Find("1.5.0"));
    }

    [Fact]
    public async Task Unreadable_pointer_preserves_every_release_and_component()
    {
        var oldest = Seed("1.5.0");
        for (int version = 1; version < 6; version++) Seed($"1.5.{version}");
        File.WriteAllText(Path.Combine(_root, "v3", "active.json"), "{");
        await _store.Prune(default);
        Assert.Equal(6, VersionCount);
        Assert.All(oldest.Release.Components, component => Assert.True(File.Exists(ComponentFile(component))));
    }

    [Fact]
    public async Task Unreadable_remaining_manifest_prevents_deleting_possibly_shared_components()
    {
        var oldest = Seed("1.5.0");
        for (int version = 1; version < 6; version++) Seed($"1.5.{version}");
        File.WriteAllText(Path.Combine(ReleaseDirectory("1.5.5"), "manifest.json"), "{");
        await _store.Prune(default);
        Assert.Equal(5, VersionCount); Assert.False(Directory.Exists(ReleaseDirectory("1.5.0")));
        Assert.All(oldest.Release.Components, component => Assert.True(File.Exists(ComponentFile(component))));
    }

    [Fact]
    public async Task Cancellation_before_cleanup_preserves_existing_store()
    {
        for (int version = 0; version < 6; version++) Seed($"1.5.{version}");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _store.Prune(cancellation.Token));
        Assert.Equal(6, VersionCount);
    }

    [Fact]
    public async Task Cleanup_stays_inside_v3_and_preserves_unmanaged_files()
    {
        string deployment = Path.Combine(_root, "deployment"); Directory.CreateDirectory(deployment);
        string otherPackage = Path.Combine(deployment, "old.exe"); File.WriteAllText(otherPackage, "network package");
        string legacy = Path.Combine(_root, "Setup-EquipoClinicaPC-1.4.0.exe"); File.WriteAllText(legacy, "legacy package");
        string marker = Path.Combine(_root, "v3", "components", "worker", "keep.txt"); File.WriteAllText(marker, "unmanaged");
        for (int version = 0; version < 6; version++) Seed($"1.5.{version}");
        await _store.Prune(default);
        Assert.Equal(5, VersionCount);
        Assert.Equal("network package", File.ReadAllText(otherPackage));
        Assert.Equal("legacy package", File.ReadAllText(legacy)); Assert.Equal("unmanaged", File.ReadAllText(marker));
    }

    [Fact]
    public async Task Redirected_child_of_an_old_release_is_never_followed_or_deleted()
    {
        for (int version = 0; version < 6; version++) Seed($"1.5.{version}");
        string target = Path.Combine(_root, "outside-v3"); Directory.CreateDirectory(target);
        string protectedFile = Path.Combine(target, "keep.txt"); File.WriteAllText(protectedFile, "protected");
        string link = Path.Combine(ReleaseDirectory("1.5.0"), "redirected");
        if (OperatingSystem.IsWindows())
        {
            static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
            var start = new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"))
            {
                UseShellExecute = false, CreateNoWindow = true,
                Arguments = "-NoProfile -NonInteractive -Command \"New-Item -ItemType Junction -Path " + Quote(link) + " -Target " + Quote(target) + " -ErrorAction Stop | Out-Null\""
            };
            using var process = System.Diagnostics.Process.Start(start)!;
            await process.WaitForExitAsync(); Assert.Equal(0, process.ExitCode);
        }
        else Directory.CreateSymbolicLink(link, target);
        try
        {
            await _store.Prune(default);
            Assert.Equal(6, VersionCount); Assert.True(Directory.Exists(link));
            Assert.Equal("protected", File.ReadAllText(protectedFile));
        }
        finally { Directory.Delete(link); }
    }

    [Fact]
    public async Task Concurrent_imports_keep_the_five_numerically_newest_versions()
    {
        await Task.WhenAll(Enumerable.Range(8, 8).Select(async version =>
        { var bundle = SetupReleaseTests.Bundle($"1.5.{version}"); await Publish(bundle.Release, bundle.Bytes); }));
        Assert.Equal(5, VersionCount);
        Assert.Equal(new[] { "1.5.15", "1.5.14", "1.5.13", "1.5.12", "1.5.11" }, _store.List().Select(r => r.Version));
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
}
