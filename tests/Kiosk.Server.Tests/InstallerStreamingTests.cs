using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Kiosk.Server.Components.Pages;
using Kiosk.Server.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Kiosk.Server.Tests;

public sealed class InstallerStreamingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "installer-streaming-" + Guid.NewGuid().ToString("N"));
    private readonly ControlledQuery _query = new();
    private readonly WebApplicationFactory<Program> _factory;

    public InstallerStreamingTests()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            foreach (string directory in new[] { "Data", "Assets", "Installers", "Setup", "Updates" })
                builder.UseSetting("Kiosk:" + directory + "Dir", Path.Combine(_root, directory));
            builder.UseSetting("Kiosk:PanelInitialPassword", "test-password");
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped<IInstallerPageQuery>(_ => _query);
                services.AddAuthentication(options =>
                { options.DefaultAuthenticateScheme = "StreamingTest"; options.DefaultChallengeScheme = "StreamingTest"; })
                    .AddScheme<AuthenticationSchemeOptions, Auth>("StreamingTest", _ => { });
            });
        });
    }

    [Fact]
    public async Task Shell_arrives_before_data_sections_finish_independently_and_history_is_not_queried()
    {
        using var client = _factory.CreateClient(); client.DefaultRequestHeaders.Add("X-Test-Panel", "yes");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var response = await client.GetAsync("/instalador", HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(timeout.Token));
        var capture = new StreamCapture(reader);
        string shell = await capture.ReadUntilAsync("installer-skeleton-deployment", timeout.Token);
        Assert.Contains("Preparar este equipo", shell);
        Assert.Contains("Instalar sistemas por red", shell);
        Assert.DoesNotContain("Todavía no", WebUtility.HtmlDecode(shell));
        Assert.False(_query.Setup.Task.IsCompleted);
        Assert.False(_query.Deployment.Task.IsCompleted);

        _query.Setup.SetResult(ReadySetup());
        string equipment = await capture.ReadUntilAsync("Descargar Online", timeout.Token);
        Assert.Contains("version=1.5.9", equipment);
        Assert.False(_query.Deployment.Task.IsCompleted);
        _query.Deployment.SetResult(InstallerSectionState<DeploymentSectionData>.Empty());
        await reader.ReadToEndAsync(timeout.Token);
        Assert.Equal(1, _query.SetupCalls);
        Assert.Equal(1, _query.DeploymentCalls);
        Assert.Equal(0, _query.HistoryCalls);
    }

    [Fact]
    public async Task Section_error_offers_retry_without_hiding_successful_other_section()
    {
        _query.Setup.SetResult(InstallerSectionState<SetupSectionData>.Error("Fallo de prueba"));
        _query.Deployment.SetResult(InstallerSectionState<DeploymentSectionData>.Ready(new("0.1.0")));
        using var client = _factory.CreateClient(); client.DefaultRequestHeaders.Add("X-Test-Panel", "yes");
        string html = WebUtility.HtmlDecode(await client.GetStringAsync("/instalador"));
        Assert.Contains("Fallo de prueba", html);
        Assert.Contains(">Reintentar</button>", html);
        Assert.Contains("/panel/deployment/download?version=0.1.0", html);
        Assert.DoesNotContain("Todavía no se han publicado", html);
    }

    [Fact]
    public async Task Restored_terminal_states_do_not_query_again()
    {
        using var page = new TestInstaller
        {
            Query = _query,
            Setup = ReadySetup(),
            Deployment = InstallerSectionState<DeploymentSectionData>.Empty()
        };
        await page.InitializeAsync();
        Assert.Equal(0, _query.SetupCalls);
        Assert.Equal(0, _query.DeploymentCalls);
    }

    [Fact]
    public async Task Disposing_page_cancels_pending_queries_without_replacing_state_with_error()
    {
        var page = new TestInstaller { Query = _query };
        Task loading = page.InitializeAsync();
        page.Dispose();
        await loading.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(InstallerSectionStatus.Loading, page.Setup!.Status);
        Assert.Equal(InstallerSectionStatus.Loading, page.Deployment!.Status);
        Assert.True(_query.SetupToken.IsCancellationRequested);
        Assert.True(_query.DeploymentToken.IsCancellationRequested);
    }

    private static InstallerSectionState<SetupSectionData> ReadySetup() => InstallerSectionState<SetupSectionData>.Ready(
        new(new("1.5.9", [new("online", 1234), new("complete", 5678)]), null));

    private sealed class TestInstaller : Installer
    {
        public Task InitializeAsync() => base.OnInitializedAsync();
    }

    private sealed class StreamCapture(StreamReader reader)
    {
        private readonly StringBuilder _text = new();
        public async Task<string> ReadUntilAsync(string marker, CancellationToken ct)
        {
            var buffer = new char[1024];
            while (!_text.ToString().Contains(marker, StringComparison.Ordinal))
            {
                int count = await reader.ReadAsync(buffer.AsMemory(), ct);
                Assert.True(count > 0, $"La respuesta terminó antes de mostrar {marker}.");
                _text.Append(buffer, 0, count);
            }
            return _text.ToString();
        }
    }

    private sealed class ControlledQuery : IInstallerPageQuery
    {
        public readonly TaskCompletionSource<InstallerSectionState<SetupSectionData>> Setup = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource<InstallerSectionState<DeploymentSectionData>> Deployment = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int SetupCalls, DeploymentCalls, HistoryCalls;
        public CancellationToken SetupToken, DeploymentToken;
        public Task<InstallerSectionState<SetupSectionData>> GetSetupSummaryAsync(CancellationToken ct)
        { Interlocked.Increment(ref SetupCalls); SetupToken = ct; return Setup.Task.WaitAsync(ct); }
        public Task<InstallerSectionState<DeploymentSectionData>> GetDeploymentSummaryAsync(CancellationToken ct)
        { Interlocked.Increment(ref DeploymentCalls); DeploymentToken = ct; return Deployment.Task.WaitAsync(ct); }
        public Task<InstallerSectionState<InstallerHistoryData>> GetHistoryAsync(string version, CancellationToken ct)
        { Interlocked.Increment(ref HistoryCalls); return Task.FromResult(InstallerSectionState<InstallerHistoryData>.Empty()); }
    }

    private sealed class Auth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(Request.Headers["X-Test-Panel"] == "yes"
            ? AuthenticateResult.Success(new(new(new ClaimsIdentity([new Claim(ClaimTypes.Name, "manager")], Scheme.Name)), Scheme.Name))
            : AuthenticateResult.NoResult());
    }

    public void Dispose() { _factory.Dispose(); if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
