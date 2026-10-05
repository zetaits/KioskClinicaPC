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

    private HttpClient PanelClient()
    {
        var client = _factory.CreateClient();
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
        Assert.Contains("Instalación remota en kioskos", html);
        Assert.DoesNotMatch("<details[^>]*\\sopen(?:[\\s=>])", html);

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
    public async Task Remote_device_and_upload_links_automatically_expand_the_remote_section(string query)
    {
        using var client = PanelClient();
        string html = await client.GetStringAsync("/aplicaciones?" + query);
        Assert.Matches("<details[^>]*\\sopen(?:[\\s=>])", html);
        Assert.Contains("action=\"/panel/installers/upload\"", html);
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
        Assert.DoesNotContain("Actualizar todas las versiones", html);
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
    public async Task Both_pages_still_require_panel_authentication(string route)
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
