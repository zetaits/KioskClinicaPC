using System.Net;
using System.Net.Http.Json;
using KioskClinicaPC.Core.Sync;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Kiosk.Server.Tests;

public sealed class QrWebIntegrationTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "kiosk-qr-web-tests", Guid.NewGuid().ToString("N"));
    private WebApplicationFactory<Program> _factory = null!;

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Kiosk:DataDir", _root);
            builder.UseSetting("Kiosk:AssetsDir", Path.Combine(_root, "assets"));
            builder.UseSetting("Kiosk:InstallersDir", Path.Combine(_root, "installers"));
            builder.UseSetting("Kiosk:ApiKey", "secret");
            builder.UseSetting("Kiosk:PanelInitialPassword", "test-panel-password");
        });
        _ = _factory.Server;
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Ficha_is_public_self_contained_and_hardened()
    {
        HttpClient client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        HttpResponseMessage redirect = await client.GetAsync("/ficha");
        Assert.Equal(HttpStatusCode.Redirect, redirect.StatusCode);
        Assert.Equal("/ficha/", redirect.Headers.Location?.ToString());

        HttpResponseMessage page = await client.GetAsync("/ficha/");
        string html = await page.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("vendor/pako-2.1.0.min.js", html);
        Assert.DoesNotContain("cdn.jsdelivr.net", html);
        Assert.Equal("nosniff", page.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Contains("noindex", page.Headers.GetValues("X-Robots-Tag").Single());
    }

    [Fact]
    public async Task Asset_manifest_requires_key_and_contains_seeded_brands()
    {
        HttpClient client = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/assets/manifest")).StatusCode);
        client.DefaultRequestHeaders.Add("X-Api-Key", "secret");
        AssetManifest? manifest = await client.GetFromJsonAsync<AssetManifest>("/api/assets/manifest");
        Assert.NotNull(manifest);
        Assert.Contains(manifest!.Files, x => x.Category == "Brands" && x.FileName == "lenovo.png");
    }

    [Fact]
    public async Task Readiness_detects_damaged_persistent_content()
    {
        HttpClient client = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);

        File.WriteAllText(Path.Combine(_root, "KioskConfig.json"), "not-json");

        HttpResponseMessage readiness = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, readiness.StatusCode);
        Assert.Equal("{\"status\":\"unavailable\"}", await readiness.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        try { Directory.Delete(_root, true); } catch { }
        return Task.CompletedTask;
    }
}
