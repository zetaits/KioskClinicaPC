using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using Kiosk.Server.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Xunit;

namespace Kiosk.Server.Tests;

public sealed class SetupDownloadIntegrationTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "kiosk-setup-download-tests", Guid.NewGuid().ToString("N"));
    private WebApplicationFactory<Program> _factory = null!;

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Kiosk:DataDir", Path.Combine(_root, "data"));
            builder.UseSetting("Kiosk:AssetsDir", Path.Combine(_root, "assets"));
            builder.UseSetting("Kiosk:InstallersDir", Path.Combine(_root, "installers"));
            builder.UseSetting("Kiosk:SetupDir", Path.Combine(_root, "setups"));
            builder.UseSetting("Kiosk:ApiKey", "test-key");
            builder.UseSetting("Kiosk:PanelInitialPassword", "test-panel-password");
            builder.ConfigureTestServices(services => services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = "Test";
                    options.DefaultChallengeScheme = "Test";
                }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { }));
        });
        _ = _factory.Server;
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Download_signals_start_only_after_bundle_validation_and_supports_ranges()
    {
        string setups = Path.Combine(_root, "setups");
        string name = "Setup-EquipoClinicaPC-1.2.0.exe";
        byte[] bytes = [1, 2, 3, 4, 5, 6];
        File.WriteAllBytes(Path.Combine(setups, name), bytes);
        var manifest = new InitialSetupBundleManifest
        {
            Version = "1.2.0", FileName = name, SizeBytes = bytes.Length,
            Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            ServerUrl = "https://panel.clinicapc.es"
        };
        File.WriteAllText(Path.Combine(setups, "Setup-EquipoClinicaPC-1.2.0.bundle.json"),
            JsonConvert.SerializeObject(manifest));

        HttpClient client = _factory.CreateClient();
        string downloadId = Guid.NewGuid().ToString("N");
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/panel/setup/download?downloadId={downloadId}");
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 1);
        HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal([1, 2], await response.Content.ReadAsByteArrayAsync());
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), x =>
            x.StartsWith($"kioskSetupDownload={downloadId};", StringComparison.Ordinal));

        HttpResponseMessage plain = await client.GetAsync("/panel/setup/download");
        Assert.Equal(HttpStatusCode.OK, plain.StatusCode);
        Assert.Equal(bytes, await plain.Content.ReadAsByteArrayAsync());
        Assert.False(plain.Headers.Contains("Set-Cookie"));

        File.WriteAllBytes(Path.Combine(setups, name), [7, 8, 9, 10, 11, 12]);
        HttpResponseMessage invalid = await client.GetAsync($"/panel/setup/download?downloadId={downloadId}");
        Assert.Equal(HttpStatusCode.NotFound, invalid.StatusCode);
        Assert.False(invalid.Headers.Contains("Set-Cookie"));
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        try { Directory.Delete(_root, true); } catch { }
        return Task.CompletedTask;
    }

    private sealed class TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, "test")], Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }
}
