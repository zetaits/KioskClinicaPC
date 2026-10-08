using Kiosk.Server.Services;
using KioskClinicaPC.Core.Sync;
using Xunit;

namespace Kiosk.Server.Tests;

public sealed class PackCatalogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "clinicapc-pack-tests-" + Guid.NewGuid().ToString("N"));
    private static WingetIndex Index(string version = "1.0", bool eligible = true) => new(DateTime.UtcNow,
        [new("Vendor.App", "Aplicación", "Vendor", version, eligible, eligible ? null : "Solo usuario")]);
    [Fact]
    public void Migration_preserves_ids_order_selection_and_backup_and_is_idempotent()
    {
        Directory.CreateDirectory(_root);
        var original = new PackCatalog(17, [new(new string('a', 32), "Google.Chrome", "Chrome", "154", false, 42)]);
        string path = Path.Combine(_root, "pack-applications.json");
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(original, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)));
        string before = File.ReadAllText(path);
        var store = new PackCatalogStore(_root); var definition = store.Definition();
        Assert.Equal(PackDefinition.FromCatalog(original).Applications, definition.Applications); Assert.Equal(17, definition.Revision);
        Assert.Equal(before, File.ReadAllText(path + ".before-v3.bak"));
        definition.Applications.Clear(); Assert.Single(store.Definition().Applications);
        var restarted = new PackCatalogStore(_root);
        Assert.Equal(17, restarted.Definition().Revision); Assert.Equal(before, File.ReadAllText(path + ".before-v3.bak"));
    }
    [Fact]
    public void Legacy_projection_updates_versions_without_changing_definition_revision()
    {
        var store = new PackCatalogStore(_root); store.Import(Index()); store.Add("Vendor.App"); store.Add("vendor.app");
        Assert.Single(store.Snapshot().Applications);
        long revision = store.Definition().Revision;
        store.Import(Index("2.0")); Assert.Equal("2.0", store.Snapshot().Applications.Single().PinnedVersion);
        Assert.Equal(revision, store.Definition().Revision);
        Assert.Empty(store.UpdateVersions()); Assert.Equal("2.0", new PackCatalogStore(_root).Snapshot().Applications.Single().PinnedVersion);
    }
    [Fact]
    public void Ineligible_and_invalid_indices_do_not_replace_last_good_data()
    {
        var store = new PackCatalogStore(_root); store.Import(Index()); store.Add("Vendor.App");
        Assert.Throws<InvalidDataException>(() => store.Import(new(DateTime.UtcNow, [])));
        store.Import(Index("2.0", false)); Assert.Single(store.UpdateVersions());
        Assert.Equal("1.0", store.Snapshot().Applications.Single().PinnedVersion);
        store.Remove(store.Snapshot().Applications.Single().Id);
        Assert.Throws<InvalidDataException>(() => store.Add("Vendor.App"));
    }
    [Fact]
    public void Missing_index_entry_preserves_configured_application_and_pinned_version()
    {
        var store = new PackCatalogStore(_root);
        var originalIndex = Index();
        store.Import(originalIndex); store.Add("Vendor.App");
        var originalApplication = store.Snapshot().Applications.Single();
        store.Import(new(originalIndex.GeneratedAtUtc.AddSeconds(1),
            [new("Vendor.Other", "Other application", "Vendor", "2.0", true)]));

        Assert.Equal(new[] { originalApplication.DisplayName }, store.UpdateVersions());
        Assert.Equal(originalApplication, new PackCatalogStore(_root).Snapshot().Applications.Single());
        Assert.Empty(store.Search("Vendor.App"));
    }
    [Fact]
    public void Configure_preserves_identity_and_pinned_version()
    {
        var store = new PackCatalogStore(_root); store.Import(Index()); store.Add("Vendor.App");
        var app = store.Snapshot().Applications.Single(); store.Configure(app.Id, false, 42);
        var actual = new PackCatalogStore(_root).Snapshot().Applications.Single();
        Assert.False(actual.SelectedByDefault); Assert.Equal(42, actual.Order); Assert.Equal(app.PinnedVersion, actual.PinnedVersion);
        Assert.Throws<InvalidDataException>(() => store.Configure(app.Id, true, -1));
    }
    [Fact]
    public void Corrupt_saved_pack_is_not_silently_discarded()
    {
        Directory.CreateDirectory(_root); File.WriteAllText(Path.Combine(_root, "pack-applications.json"), "invalid");
        Assert.Throws<System.Text.Json.JsonException>(() => new PackCatalogStore(_root));
    }
    [Theory]
    [InlineData(PackItemState.Succeeded, true)]
    [InlineData(PackItemState.AlreadyInstalled, true)]
    [InlineData(PackItemState.Failed, false)]
    [InlineData(PackItemState.VerificationPending, false)]
    [InlineData(PackItemState.Pending, false)]
    public void Reboot_is_only_remaining_task_when_all_items_verified(PackItemState state, bool complete)
    {
        var run = new PackRun { Items = [new() { Application = new("id", "Vendor.App", "App", "1"), State = state, RebootRequired = true }] };
        Assert.Equal(complete, run.Complete); Assert.Equal(complete, run.Summary.Contains("Solo queda reiniciar"));
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
