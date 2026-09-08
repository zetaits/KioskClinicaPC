using Kiosk.Server.Services;
using KioskClinicaPC.Core.Sync;
using Xunit;

namespace Kiosk.Server.Tests;

public sealed class InstallerCatalogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "kiosk-installer-tests", Guid.NewGuid().ToString("N"));
    private InstallerCatalog Catalog(long max = 1024 * 1024) => new(_root, Path.Combine(_root, "files"), max);

    [Fact]
    public async Task Adds_unsigned_msi_only_after_explicit_confirmation_and_persists_metadata()
    {
        byte[] msi = { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 };
        var catalog = Catalog();

        await Assert.ThrowsAsync<UnsignedInstallerException>(() =>
            catalog.AddAsync("Chrome", "chrome.msi", new MemoryStream(msi), allowUnsigned: false));

        var package = await catalog.AddAsync("Chrome", "chrome.msi", new MemoryStream(msi), allowUnsigned: true);
        Assert.Equal(InstallerPackageKind.Msi, package.Kind);
        Assert.True(package.AllowUnsigned);
        Assert.Equal(64, package.Sha256.Length);
        Assert.True(File.Exists(catalog.ResolveFile(package)));

        var reloaded = Catalog().Find(package.Id);
        Assert.NotNull(reloaded);
        Assert.Equal("Chrome", reloaded!.DisplayName);
    }

    [Fact]
    public async Task Rejects_unknown_exe_and_duplicate_visible_names()
    {
        byte[] unknown = new byte[128]; unknown[0] = (byte)'M'; unknown[1] = (byte)'Z';
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Catalog().AddAsync("Desconocido", "setup.exe", new MemoryStream(unknown), true));

        byte[] inno = new byte[256]; inno[0] = (byte)'M'; inno[1] = (byte)'Z';
        System.Text.Encoding.ASCII.GetBytes("Inno Setup Setup Data").CopyTo(inno, 40);
        var catalog = Catalog();
        await catalog.AddAsync("Utilidad", "setup.exe", new MemoryStream(inno), true);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            catalog.AddAsync("utilidad", "otro.exe", new MemoryStream(inno), true));
    }

    [Fact]
    public async Task Enforces_configured_size_limit()
    {
        byte[] msi = { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 };
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Catalog(4).AddAsync("Grande", "large.msi", new MemoryStream(msi), true));
    }

    [Fact]
    public async Task Initial_setup_flags_are_opt_in_ordered_and_persisted()
    {
        byte[] msi = { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 };
        var catalog = Catalog();
        var first = await catalog.AddAsync("Primera", "one.msi", new MemoryStream(msi), true);
        var second = await catalog.AddAsync("Segunda", "two.msi", new MemoryStream(msi), true);

        catalog.ConfigureInitialSetup(first.Id, true, true, 20);
        catalog.ConfigureInitialSetup(second.Id, true, false, 10);

        Assert.Equal(new[] { second.Id, first.Id }, catalog.InitialSetupList().Select(p => p.Id));
        var reloaded = Catalog().Find(first.Id)!;
        Assert.True(reloaded.AvailableInInitialSetup);
        Assert.True(reloaded.SelectedByDefault);
        catalog.ConfigureInitialSetup(first.Id, false, true, 20);
        Assert.False(catalog.Find(first.Id)!.SelectedByDefault);
    }

    [Fact]
    public async Task Archive_removes_binary_but_keeps_historical_metadata()
    {
        byte[] msi = { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 };
        var catalog = Catalog();
        var package = await catalog.AddAsync("Chrome", "chrome.msi", new MemoryStream(msi), true);
        string binary = catalog.ResolveFile(package);

        catalog.Archive(package.Id);

        Assert.False(File.Exists(binary));
        Assert.Null(catalog.Find(package.Id));
        Assert.Empty(catalog.List());
        string metadata = File.ReadAllText(Path.Combine(_root, "installers.json"));
        Assert.Contains(package.Id, metadata);
        Assert.Contains("ArchivedAtUtc", metadata);
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
}
