using System.Net;
using System.Net.Http.Json;
using Kiosk.Server.Services;
using KioskClinicaPC.Core.Sync;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kiosk.Server.Tests;
public sealed class PackApiTests : IDisposable
{
    private const string Key = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "clinicapc-pack-api-" + Guid.NewGuid().ToString("N"));
    private readonly WebApplicationFactory<Program> _factory;
    public PackApiTests()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            foreach (string directory in new[] { "Data", "Assets", "Installers", "Setup", "Updates" }) b.UseSetting("Kiosk:" + directory + "Dir", Path.Combine(_root, directory));
            b.UseSetting("Kiosk:ApiKey", "general-api"); b.UseSetting("Kiosk:InitialSetupKey", Key); b.UseSetting("Kiosk:ReleasePublishKey", "publish-key");
            b.UseSetting("Kiosk:PanelInitialPassword", "test-password");
        });
    }
    [Fact]
    public async Task Scoped_catalog_read_creates_neither_sessions_nor_fleet_records()
    {
        using var client = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/setup/v2/catalog")).StatusCode);
        client.DefaultRequestHeaders.Add("X-Api-Key", "general-api");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/setup/v2/catalog")).StatusCode);
        client.DefaultRequestHeaders.Add("X-Setup-Key", Key);
        Assert.Equal("2", (await client.GetAsync("/api/setup/v2/catalog")).Headers.GetValues("X-Setup-Catalog-Version").Single());
        Assert.NotNull(await client.GetFromJsonAsync<PackCatalog>("/api/setup/v2/catalog"));
        Assert.Empty(_factory.Services.GetRequiredService<InitialSetupSessionStore>().Recent());
        Assert.Empty(_factory.Services.GetRequiredService<FleetRegistry>().Devices);
    }
    [Fact]
    public async Task Index_import_requires_separate_publish_key_and_invalid_import_keeps_catalogue()
    {
        using var client = _factory.CreateClient();
        var index = new WingetIndex(DateTime.UtcNow, [new("Vendor.App", "App", "Vendor", "1", true)]);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/releases/winget-index", index)).StatusCode);
        client.DefaultRequestHeaders.Add("X-Release-Publish-Key", "publish-key");
        // ReadFromJson request streams may be chunked: endpoint must support bounded chunked JSON too.
        var response = await client.PostAsJsonAsync("/api/releases/winget-index", index);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/releases/winget-index", new WingetIndex(DateTime.UtcNow, []))).StatusCode);
        Assert.Single(_factory.Services.GetRequiredService<PackCatalogStore>().Search("Vendor"));
    }
    [Fact]
    public async Task Private_setup_import_checks_hash_and_is_immutable()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Release-Publish-Key", "publish-key");
        byte[] bytes = [1, 2, 3, 4];
        var manifest = new InitialSetupBundleManifest
        {
            SchemaVersion = 2, InstallerKind = "equipment-wpf", CatalogApiVersion = 2, SourceCommit = new string('a', 40),
            AssistantVersion = "9.0.0", WorkerVersion = "1.3.0", KioskVersion = "1.2.0",
            Version = "9.0.0", FileName = "Setup-EquipoClinicaPC-9.0.0.exe", SizeBytes = bytes.Length,
            Sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)), ServerUrl = "https://panel.clinicapc.es"
        };
        MultipartFormDataContent Form(byte[] content)
        {
            var form = new MultipartFormDataContent();
            form.Add(new StringContent(Newtonsoft.Json.JsonConvert.SerializeObject(manifest)), "manifest", "setup.bundle.json");
            form.Add(new ByteArrayContent(content), "setup", manifest.FileName); return form;
        }
        using (var bad = Form([4, 3, 2, 1])) Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/releases/setup", bad)).StatusCode);
        Assert.Null(_factory.Services.GetRequiredService<InitialSetupBundleStore>().Latest(out _));
        using (var good = Form(bytes)) Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/releases/setup", good)).StatusCode);
        using (var duplicate = Form(bytes)) Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/releases/setup", duplicate)).StatusCode);
        Assert.Equal("9.0.0", _factory.Services.GetRequiredService<InitialSetupBundleStore>().Latest(out _)!.Manifest.Version);
        using var anonymous = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.NotEqual(HttpStatusCode.OK, (await anonymous.GetAsync("/panel/setup/download")).StatusCode);
    }
    public void Dispose() { _factory.Dispose(); if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
