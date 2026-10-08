using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kiosk.Deployment;
using Kiosk.Server.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Kiosk.Server.Tests;

public sealed class DeploymentIntegrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "clinicapc-deployment-http-tests", Guid.NewGuid().ToString("N"));
    private readonly WebApplicationFactory<Program> _factory;
    public DeploymentIntegrationTests()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            foreach (var entry in new[] { ("DataDir", "data"), ("AssetsDir", "assets"), ("InstallersDir", "installers"), ("SetupDir", "setups"), ("UpdatesDir", "updates") })
                builder.UseSetting("Kiosk:" + entry.Item1, Path.Combine(_root, entry.Item2));
            builder.UseSetting("Kiosk:ApiKey", "fleet-key"); builder.UseSetting("Kiosk:InitialSetupKey", new string('b', 64));
            builder.UseSetting("Kiosk:ReleasePublishKey", "release-key"); builder.UseSetting("Kiosk:PanelInitialPassword", "test-password");
            builder.ConfigureTestServices(services => services.AddAuthentication(options =>
            { options.DefaultAuthenticateScheme = "DeploymentTest"; options.DefaultChallengeScheme = "DeploymentTest"; })
                .AddScheme<AuthenticationSchemeOptions, TestAuth>("DeploymentTest", _ => { }));
        });
    }
    private HttpClient Client(bool panel = false)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        if (panel) client.DefaultRequestHeaders.Add("X-Test-Panel", "yes"); return client;
    }
    private DeploymentStore Store => _factory.Services.GetRequiredService<DeploymentStore>();
    [Fact]
    public async Task Station_authentication_is_required_even_with_panel_fleet_setup_or_query_credentials()
    {
        using var client = Client(true);
        client.DefaultRequestHeaders.Add("X-Api-Key", "fleet-key"); client.DefaultRequestHeaders.Add("X-Setup-Key", new string('b', 64));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/deployment/v1/configuration")).StatusCode);
        var code = Store.CreateCode(); var enrolled = Store.Enroll(new(1, code.Code, "Station"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/deployment/v1/configuration?access_token=" + enrolled.Credential)).StatusCode);
        client.DefaultRequestHeaders.Add("X-Deployment-Credential", enrolled.Credential);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/deployment/v1/configuration")).StatusCode);
        Store.Revoke(enrolled.StationId);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/deployment/v1/configuration")).StatusCode);
    }
    [Fact]
    public async Task New_station_negotiates_current_application_policy_and_old_station_keeps_v1_configuration()
    {
        using var client = Client(); var enrolled = Store.Enroll(new(1, Store.CreateCode().Code, "Station"));
        client.DefaultRequestHeaders.Add("X-Deployment-Credential", enrolled.Credential);
        var old = await client.GetFromJsonAsync<DeploymentConfiguration>("/api/deployment/v1/configuration");
        Assert.Equal(1, old!.ComponentPolicyVersion); Assert.Null(old.Definition);
        client.DefaultRequestHeaders.Add("X-Deployment-Component-Policy", "2");
        var current = await client.GetFromJsonAsync<DeploymentConfiguration>("/api/deployment/v1/configuration");
        Assert.Equal(2, current!.ComponentPolicyVersion); Assert.NotNull(current.Definition);
    }
    [Fact]
    public async Task Enrollment_is_separate_single_use_and_rejects_password_fields()
    {
        using var client = Client(); var code = Store.CreateCode();
        var response = await client.PostAsJsonAsync("/api/deployment/v1/enrollment", new EnrollmentRequest(1, code.Code, "Station"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var enrolled = await response.Content.ReadFromJsonAsync<EnrollmentResponse>(); Assert.NotNull(enrolled);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/deployment/v1/enrollment", new EnrollmentRequest(1, code.Code, "Again"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/deployment/v1/enrollment", new { protocolVersion = 1, code = Store.CreateCode().Code, name = "Station", password = "must-not-be-accepted" })).StatusCode);
        client.DefaultRequestHeaders.Add("X-Deployment-Credential", enrolled.Credential);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/config")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/setup/v2/catalog")).StatusCode);
    }
    [Fact]
    public async Task Panel_mutations_require_cookie_identity_and_antiforgery_token()
    {
        using var guest = Client(); using var panel = Client(true);
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.PostAsJsonAsync("/panel/deployment/enrollment", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await panel.PostAsJsonAsync("/panel/deployment/enrollment", new { })).StatusCode);
        string page = await panel.GetStringAsync("/login");
        string token = Regex.Match(page, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(token); panel.DefaultRequestHeaders.Add("RequestVerificationToken", WebUtility.HtmlDecode(token));
        Assert.Equal(HttpStatusCode.OK, (await panel.PostAsJsonAsync("/panel/deployment/enrollment", new { })).StatusCode);
    }
    [Fact]
    public async Task Deployment_download_has_independent_marker_and_authentication_and_requires_validated_package()
    {
        using var guest = Client(); using var panel = Client(true);
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.GetAsync("/panel/deployment/download")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await panel.GetAsync("/panel/deployment/download")).StatusCode);
        byte[] bytes = [1, 2, 3, 4]; var hashes = new Dictionary<string, string> { ["ipxe-shim.efi"] = new('a', 64), ["ipxe.efi"] = new('b', 64), ["wimboot"] = new('c', 64), ["boot.wim"] = new('d', 64) };
        var release = new DeploymentRelease(1, 1, "deployment-wpf", "0.1.0", "Setup-InstalacionRedClinicaPC-0.1.0.exe", bytes.Length,
            Convert.ToHexString(SHA256.HashData(bytes)), new('a', 40), "10.1.26100.9457", hashes, true);
        await _factory.Services.GetRequiredService<DeploymentReleaseStore>().Import(release, new MemoryStream(bytes), default);
        string id = Guid.NewGuid().ToString("N");
        using var request = new HttpRequestMessage(HttpMethod.Get, "/panel/deployment/download?downloadId=" + id);
        request.Headers.Range = new(0, 1); var response = await panel.SendAsync(request);
        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode); Assert.Equal(new byte[] { 1, 2 }, await response.Content.ReadAsByteArrayAsync());
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("kioskDeploymentDownload=" + id));
        Assert.DoesNotContain(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("kioskSetupDownload="));
        string installer = await panel.GetStringAsync("/instalador");
        Assert.Contains("Preparar este equipo", installer); Assert.Contains("Instalar sistemas por red", installer);
        Assert.Contains("data-download-cookie=\"kioskDeploymentDownload\"", installer);
    }
    [Fact]
    public async Task Publishing_uses_release_key_and_never_accepts_fleet_credentials()
    {
        using var client = Client(); client.DefaultRequestHeaders.Add("X-Api-Key", "fleet-key");
        using var form = new MultipartFormDataContent();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/releases/deployment", form)).StatusCode);
        client.DefaultRequestHeaders.Add("X-Release-Publish-Key", "release-key");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/releases/deployment", new MultipartFormDataContent())).StatusCode);
    }
    [Fact]
    public async Task Readiness_checks_deployment_state_and_advertises_compatible_protocol_without_changing_body()
    {
        using var client = Client(); var healthy = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, healthy.StatusCode); Assert.Equal("1", healthy.Headers.GetValues("X-Deployment-Protocol").Single());
        Assert.Equal("2", healthy.Headers.GetValues("X-Deployment-Component-Policy").Single());
        Assert.Equal("3", healthy.Headers.GetValues("X-Setup-Catalog-Version").Single());
        Assert.Equal("ok", (await healthy.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
        File.WriteAllText(Path.Combine(_root, "data", "deployment-v1.json"), "{");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/health/ready")).StatusCode);
    }
    public void Dispose() { _factory.Dispose(); if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    private sealed class TestAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(Request.Headers["X-Test-Panel"] == "yes"
            ? AuthenticateResult.Success(new(new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "panel"), new Claim(ClaimTypes.Name, "panel")], Scheme.Name)), Scheme.Name))
            : AuthenticateResult.NoResult());
    }
}
