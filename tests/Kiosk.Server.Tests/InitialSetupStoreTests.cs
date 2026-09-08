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
            Version = version, FileName = fileName, SizeBytes = content.Length,
            Sha256 = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant(),
            ServerUrl = "https://panel.example", CreatedAtUtc = DateTime.UtcNow
        };
        File.WriteAllText(Path.Combine(_root, $"Setup-EquipoClinicaPC-{version}.bundle.json"), JsonSerializer.Serialize(manifest));
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
}
