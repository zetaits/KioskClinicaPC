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
    public async Task V3_has_scoped_auth_and_no_versions_while_v2_receives_updated_concrete_projection()
    {
        using var client = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/setup/v3/catalog")).StatusCode);
        client.DefaultRequestHeaders.Add("X-Api-Key", "general-api");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/setup/v3/catalog")).StatusCode);
        client.DefaultRequestHeaders.Add("X-Setup-Key", Key);
        var store = _factory.Services.GetRequiredService<PackCatalogStore>();
        store.Import(new(DateTime.UtcNow.AddMinutes(-1), [new("Google.Chrome", "Chrome", "Google", "154", true)])); store.Add("Google.Chrome");
        long revision = store.Definition().Revision;
        store.Import(new(DateTime.UtcNow, [new("Google.Chrome", "Chrome", "Google", "155", true)]));
        using var v3 = await client.GetAsync("/api/setup/v3/catalog");
        Assert.Equal("3", v3.Headers.GetValues("X-Setup-Catalog-Version").Single());
        string json = await v3.Content.ReadAsStringAsync(); Assert.DoesNotContain("pinnedVersion", json);
        Assert.Equal(revision, (await v3.Content.ReadFromJsonAsync<PackDefinition>())!.Revision);
        var old = await client.GetFromJsonAsync<PackCatalog>("/api/setup/v2/catalog");
        Assert.Equal("155", old!.Applications.Single().PinnedVersion); Assert.Null(old.Definition);
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
    public async Task Retired_setup_import_is_unavailable()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Release-Publish-Key", "publish-key");
        using var form = new MultipartFormDataContent();
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/api/releases/setup", form)).StatusCode);
    }
    public void Dispose() { _factory.Dispose(); if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
