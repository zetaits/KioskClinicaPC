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

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
}
