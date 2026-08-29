using Kiosk.Server.Services;
using Xunit;

namespace Kiosk.Server.Tests;

public sealed class PanelAuthStoreTests
{
    [Fact]
    public void First_start_requires_a_strong_initial_password()
    {
        string dir = TempDir();
        try
        {
            var error = Assert.Throws<InvalidOperationException>(() => new PanelAuthStore(dir));
            Assert.Contains("PanelInitialPassword", error.Message);
            Assert.Throws<InvalidOperationException>(() => new PanelAuthStore(dir, "short"));
        }
        finally { TryDelete(dir); }
    }

    [Fact]
    public void Initial_password_is_hashed_and_only_needed_once()
    {
        string dir = TempDir();
        try
        {
            const string password = "a-secure-test-password";
            var created = new PanelAuthStore(dir, password);
            Assert.True(created.Verify(password));
            Assert.DoesNotContain(password, File.ReadAllText(Path.Combine(dir, "panel.json")));

            var reopened = new PanelAuthStore(dir);
            Assert.True(reopened.Verify(password));
        }
        finally { TryDelete(dir); }
    }

    private static string TempDir()
    {
        string path = Path.Combine(Path.GetTempPath(), "kiosk-panel-auth-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDelete(string path)
    {
        try { Directory.Delete(path, recursive: true); } catch { }
    }
}
