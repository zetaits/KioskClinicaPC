using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using Kiosk.Server.Services;
using KioskClinicaPC.Core.Sync;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Kiosk.Server.Tests;

public sealed class PanelApplicationPagesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "clinicapc-panel-pages-" + Guid.NewGuid().ToString("N"));
    private readonly ECDsa _signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly WebApplicationFactory<Program> _factory;

    public PanelApplicationPagesTests()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            foreach (string directory in new[] { "Data", "Assets", "Installers", "Setup", "Updates" })
                builder.UseSetting("Kiosk:" + directory + "Dir", Path.Combine(_root, directory));
            builder.UseSetting("Kiosk:ApiKey", "test-api-key");
            builder.UseSetting("Kiosk:PanelInitialPassword", "test-panel-password");
            builder.UseSetting("Kiosk:UpdateSigningKeys:test", _signer.ExportSubjectPublicKeyInfoPem());
            builder.ConfigureTestServices(services => services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = "Test";
                    options.DefaultChallengeScheme = "Test";
                }).AddScheme<AuthenticationSchemeOptions, PanelTestAuthHandler>("Test", _ => { }));
        });
    }

    private HttpClient PanelClient(bool autoRedirect = true)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = autoRedirect });
        client.DefaultRequestHeaders.Add("X-Test-Panel-User", "manager");
        return client;
    }

    private async Task ImportInternalSetup()
    {
        byte[] bytes = [0x4d, 0x5a, 1, 2];
        var manifest = new InitialSetupBundleManifest
        {
            Version = "1.2.0", FileName = "Setup-EquipoClinicaPC-1.2.0.exe", SizeBytes = bytes.Length,
            Sha256 = Convert.ToHexString(SHA256.HashData(bytes)), ServerUrl = "https://panel.clinicapc.es"
        };
        using var input = new MemoryStream(bytes);
        await _factory.Services.GetRequiredService<InitialSetupBundleStore>().ImportAsync(manifest, manifest.FileName, input, CancellationToken.None);
    }

    [Fact]
    public async Task Applications_contains_pack_editor_with_styled_explicit_text_input_and_preserves_pack()
    {
        using var client = PanelClient();
        var catalog = _factory.Services.GetRequiredService<PackCatalogStore>();
        catalog.Import(new(DateTime.UtcNow, [new("Vendor.App", "Pack test application", "Vendor", "1.0", true)]));
        catalog.Add("Vendor.App");
        var before = catalog.Snapshot();

        string html = WebUtility.HtmlDecode(await client.GetStringAsync("/aplicaciones"));
        Assert.Contains("Añadir aplicaciones al pack", html);
        Assert.Contains("Pack test application", html);
        Assert.Matches("<input[^>]*type=\"text\"[^>]*id=\"pack-search\"", html);
        Assert.Contains("href=\"/instalador\"", html);
        Assert.Contains("data-pack-count=\"1\"", html);
        Assert.Contains("data-preselected-count=\"1\"", html);
        Assert.Contains("data-pack-application=\"Vendor.App\"", html);
        Assert.Contains("1 aplicación · 1 seleccionada por defecto", html);
        Assert.DoesNotContain("Instalación remota", html);
        Assert.DoesNotContain("/panel/installers/upload", html);
        Assert.DoesNotContain("allowUnsigned", html);
        Assert.DoesNotContain("El catálogo está vacío", html);
        Assert.DoesNotContain("Todavía no hay aplicaciones en el pack", html);
        Assert.DoesNotContain("Historial", html);

        Assert.Equal(before.Revision, catalog.Snapshot().Revision);
        Assert.Equal(before.Applications.Single(), catalog.Snapshot().Applications.Single());
        Assert.Empty(_factory.Services.GetRequiredService<InstallationJobStore>().Recent(50));
        Assert.Empty(_factory.Services.GetRequiredService<FleetRegistry>().Devices);
        Assert.Empty(_factory.Services.GetRequiredService<InitialSetupSessionStore>().Recent());
    }

    [Theory]
    [InlineData("device=11111111111111111111111111111111")]
    [InlineData("ok=Uploaded")]
    [InlineData("error=UploadFailed")]
    public async Task Remote_device_and_upload_links_use_a_separate_fleet_page(string query)
    {
        using var client = PanelClient();
        string html = WebUtility.HtmlDecode(await client.GetStringAsync("/ordenadores/instalaciones?" + query));
        Assert.Contains("Instalación remota en la flota", html);
        Assert.Contains("action=\"/panel/installers/upload\"", html);
        Assert.Contains("Instaladores privados de la flota", html);
        Assert.DoesNotContain("pack-search", html);
        Assert.DoesNotContain("data-pack-count", html);
        using var oldLink = await client.GetAsync("/aplicaciones?" + query);
        Assert.Equal("/ordenadores/instalaciones", oldLink.RequestMessage!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Pack_counts_and_list_follow_saved_add_selection_and_remove_changes()
    {
        using var client = PanelClient();
        var catalog = _factory.Services.GetRequiredService<PackCatalogStore>();
        catalog.Import(new(DateTime.UtcNow, [new("Vendor.Alpha", "Alpha app", "Vendor", "1", true), new("Vendor.Beta", "Beta app", "Vendor", "2", true)]));
        catalog.Add("Vendor.Alpha"); catalog.Add("Vendor.Beta");
        string html = await client.GetStringAsync("/aplicaciones");
        Assert.Contains("data-pack-count=\"2\"", html);
        Assert.Contains("data-preselected-count=\"2\"", html);
        var alpha = catalog.Snapshot().Applications.Single(x => x.WingetId == "Vendor.Alpha");
        catalog.Configure(alpha.Id, false, 42);
        html = await client.GetStringAsync("/aplicaciones");
        Assert.Contains("data-pack-count=\"2\"", html);
        Assert.Contains("data-preselected-count=\"1\"", html);
        Assert.Contains("data-pack-application=\"Vendor.Alpha\"", html);
        Assert.Contains("value=\"42\"", html);
        catalog.Remove(alpha.Id);
        html = await client.GetStringAsync("/aplicaciones");
        Assert.Contains("data-pack-count=\"1\"", html);
        Assert.Contains("data-preselected-count=\"1\"", html);
        Assert.DoesNotContain("data-pack-application=\"Vendor.Alpha\"", html);
        Assert.Contains("data-pack-application=\"Vendor.Beta\"", html);
    }

    [Fact]
    public async Task Private_executables_never_appear_in_the_pack_and_fleet_keeps_its_management_link()
    {
        using var client = PanelClient();
        byte[] msi = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];
        using var input = new MemoryStream(msi);
        await _factory.Services.GetRequiredService<InstallerCatalog>().AddAsync("Private fleet installer", "fleet.msi", input, true);
        string pack = WebUtility.HtmlDecode(await client.GetStringAsync("/aplicaciones"));
        Assert.Contains("data-pack-count=\"0\"", pack);
        Assert.Contains("Todavía no hay aplicaciones en el pack", pack);
        Assert.DoesNotContain("Private fleet installer", pack);
        Assert.DoesNotContain("/panel/installers/upload", pack);
        string remote = await client.GetStringAsync("/ordenadores/instalaciones");
        Assert.Contains("Private fleet installer", remote);
        string fleet = await client.GetStringAsync("/ordenadores");
        Assert.Contains("href=\"/ordenadores/instalaciones\"", fleet);
    }

    [Fact]
    public async Task Private_upload_redirects_to_fleet_installer_page_without_changing_pack()
    {
        using var client = PanelClient(autoRedirect: false);
        string html = await client.GetStringAsync("/ordenadores/instalaciones");
        var token = System.Text.RegularExpressions.Regex.Match(html, "<input[^>]*name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(token.Success);
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(WebUtility.HtmlDecode(token.Groups[1].Value)), "__RequestVerificationToken");
        form.Add(new StringContent("Private uploaded MSI"), "displayName");
        form.Add(new StringContent("on"), "allowUnsigned");
        form.Add(new ByteArrayContent([0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1]), "installer", "private.msi");
        using var response = await client.PostAsync("/panel/installers/upload", form);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/ordenadores/instalaciones?ok=", response.Headers.Location!.OriginalString);
        Assert.Single(_factory.Services.GetRequiredService<InstallerCatalog>().List());
        Assert.Empty(_factory.Services.GetRequiredService<PackCatalogStore>().Snapshot().Applications);
    }

    [Fact]
    public async Task Installer_only_shows_downloads_and_link_back_to_applications()
    {
        using var client = PanelClient();
        await ImportInternalSetup();

        string html = WebUtility.HtmlDecode(await client.GetStringAsync("/instalador"));
        Assert.Contains("Instalador de equipos", html);
        Assert.Contains("solo pack, solo Kiosk o ambos", html);
        Assert.Contains("href=\"/panel/setup/download\"", html);
        Assert.Contains("data-setup-download-label", html);
        Assert.Contains("data-setup-download-status", html);
        Assert.Contains("href=\"/aplicaciones\"", html);
        Assert.DoesNotContain("pack-search", html);
        Assert.DoesNotContain("Actualizar versiones del pack", html);
        Assert.DoesNotContain("Preseleccionada", html);
        Assert.DoesNotContain("github.com", html);
    }

    [Fact]
    public async Task Installer_keeps_single_internal_download_even_when_public_kiosk_releases_exist()
    {
        using var client = PanelClient();
        var updates = _factory.Services.GetRequiredService<KioskUpdateStore>();
        async Task Import(string version)
        {
            byte[] setup = [0x4d, 0x5a, 1, 2];
            var manifest = new KioskReleaseManifest
            {
                Version = version, FileName = $"Setup-KioskClinicaPC-{version}.exe", SizeBytes = setup.Length,
                Sha256 = Convert.ToHexString(SHA256.HashData(setup)), PublishedAtUtc = DateTime.UtcNow, KeyId = "test",
                GitHubFallbackUrl = $"https://github.com/zetaits/KioskClinicaPC/releases/download/v{version}/Setup-KioskClinicaPC-{version}.exe"
            };
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            using var input = new MemoryStream(setup);
            await updates.ImportAsync(bytes, Convert.ToBase64String(_signer.SignData(bytes, HashAlgorithmName.SHA256)), manifest.FileName, input);
        }
        await Import("1.2.0"); await Import("2.0.0"); updates.Withdraw("2.0.0");
        await ImportInternalSetup();

        string html = await client.GetStringAsync("/instalador");
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(html, "href=\"/panel/setup/download\""));
        Assert.DoesNotContain("github.com", html);
        Assert.DoesNotContain("Descargar Kiosk", html);
        Assert.Null(updates.ActiveVersion);
        Assert.Empty(updates.RecentJobs());
    }

    [Theory]
    [InlineData("/aplicaciones")]
    [InlineData("/instalador")]
    [InlineData("/ordenadores/instalaciones")]
    public async Task Pages_still_require_panel_authentication(string route)
    {
        using var anonymous = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(route)).StatusCode);
    }

    public void Dispose()
    {
        _factory.Dispose(); _signer.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private sealed class PanelTestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (Request.Headers["X-Test-Panel-User"] != "manager") return Task.FromResult(AuthenticateResult.NoResult());
            var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "test-manager")], Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }
}
