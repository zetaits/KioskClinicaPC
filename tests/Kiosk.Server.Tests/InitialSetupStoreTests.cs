using System.Security.Cryptography;
using System.Text.Json;
using Kiosk.Server.Services;
using KioskClinicaPC.Core.Sync;
using Xunit;

namespace Kiosk.Server.Tests;

public sealed class InitialSetupStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "kiosk-setup-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Legacy_bundle_is_retained_but_never_offered_when_no_v2_exists()
    {
        Directory.CreateDirectory(_root);
        WriteBundle("1.2.0", [1, 2, 3]);
        string path = Path.Combine(_root, "Setup-EquipoClinicaPC-1.2.0.bundle.json");
        var old = JsonSerializer.Deserialize<InitialSetupBundleManifest>(File.ReadAllText(path))!;
        old.SchemaVersion = 1; old.InstallerKind = "inno";
        File.WriteAllText(path, JsonSerializer.Serialize(old));
        var store = new InitialSetupBundleStore(_root);
        Assert.Null(store.Latest(out var error)); Assert.Contains("Equipment Setup", error);
        Assert.True(File.Exists(Path.Combine(_root, old.FileName)));
    }
    [Theory]
    [InlineData("schema")]
    [InlineData("kind")]
    [InlineData("catalog")]
    [InlineData("source")]
    [InlineData("assistant")]
    [InlineData("worker")]
    [InlineData("kiosk")]
    [InlineData("server")]
    public async Task Incompatible_metadata_is_rejected_before_storing_binary(string invalid)
    {
        Directory.CreateDirectory(_root); WriteBundle("1.3.0", [1, 2, 3]);
        string path = Path.Combine(_root, "Setup-EquipoClinicaPC-1.3.0.bundle.json");
        var manifest = JsonSerializer.Deserialize<InitialSetupBundleManifest>(File.ReadAllText(path))!;
        File.Delete(path); File.Delete(Path.Combine(_root, manifest.FileName));
        switch (invalid)
        {
            case "schema": manifest.SchemaVersion = 1; break;
            case "kind": manifest.InstallerKind = "inno"; break;
            case "catalog": manifest.CatalogApiVersion = 1; break;
            case "source": manifest.SourceCommit = "unknown"; break;
            case "assistant": manifest.AssistantVersion = "1.2.0"; break;
            case "worker": manifest.WorkerVersion = ""; break;
            case "kiosk": manifest.KioskVersion = ""; break;
            case "server": manifest.ServerUrl = "http://panel.invalid"; break;
        }
        using var input = new MemoryStream([1, 2, 3]);
        var store = new InitialSetupBundleStore(_root);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ImportAsync(manifest, manifest.FileName, input, CancellationToken.None));
        Assert.Empty(Directory.EnumerateFiles(_root));
    }
    [Fact]
    public void Session_tokens_are_scoped_and_terminal_results_are_persisted()
    {
        var package = new InstallerPackage { Id = "p1", DisplayName = "App", Sha256 = new string('a', 64) };
        var store = new InitialSetupSessionStore(_root);
        var created = store.Create("PC1", "1.2.3", [package]);

        Assert.Null(store.AuthorizePackage(created.Session.Id, package.Id, "wrong"));
        Assert.NotNull(store.AuthorizePackage(created.Session.Id, package.Id, created.Token));
        Assert.True(store.HasActivePackage(package.Id));
        Assert.True(store.Update(created.Session.Id, package.Id, created.Token,
            new InitialSetupStatusUpdate { State = InstallationJobState.Succeeded, ProgressPercent = 100 }));
        Assert.False(store.HasActivePackage(package.Id));

        var reloaded = new InitialSetupSessionStore(_root).Recent().Single();
        Assert.True(reloaded.IsTerminal);
        Assert.Equal("PC1", reloaded.MachineName);
    }

    [Fact]
    public void Bundle_store_rejects_tampering_and_selects_latest_valid_version()
    {
        Directory.CreateDirectory(_root);
        WriteBundle("1.0.0", [1, 2, 3]);
        WriteBundle("1.2.0", [4, 5, 6]);
        var store = new InitialSetupBundleStore(_root);

        Assert.Equal("1.2.0", store.Latest(out _)!.Manifest.Version);

        File.AppendAllText(Path.Combine(_root, "Setup-EquipoClinicaPC-1.2.0.exe"), "alterado");
        Assert.Equal("1.0.0", store.Latest(out string? error)!.Manifest.Version);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    private void WriteBundle(string version, byte[] content)
    {
        string fileName = $"Setup-EquipoClinicaPC-{version}.exe";
        File.WriteAllBytes(Path.Combine(_root, fileName), content);
        var manifest = new InitialSetupBundleManifest
        {
            SchemaVersion = 2, InstallerKind = "equipment-wpf", CatalogApiVersion = 2, SourceCommit = new string('a', 40),
            AssistantVersion = version, WorkerVersion = "1.3.0", KioskVersion = "1.2.0",
            Version = version, FileName = fileName, SizeBytes = content.Length,
            Sha256 = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant(),
            ServerUrl = "https://panel.example", CreatedAtUtc = DateTime.UtcNow
        };
        File.WriteAllText(Path.Combine(_root, $"Setup-EquipoClinicaPC-{version}.bundle.json"), JsonSerializer.Serialize(manifest));
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
}
