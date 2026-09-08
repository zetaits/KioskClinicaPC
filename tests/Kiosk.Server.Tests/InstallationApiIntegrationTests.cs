using System.Net;
using System.Net.Http.Json;
using Kiosk.Server.Services;
using KioskClinicaPC.Core.Sync;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kiosk.Server.Tests;

public sealed class InstallationApiIntegrationTests : IAsyncLifetime
{
    private const string SetupKey = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "kiosk-api-install-tests", Guid.NewGuid().ToString("N"));
    private WebApplicationFactory<Program> _factory = null!;

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Kiosk:DataDir", _root);
            b.UseSetting("Kiosk:AssetsDir", Path.Combine(_root, "assets"));
            b.UseSetting("Kiosk:InstallersDir", Path.Combine(_root, "installers"));
            b.UseSetting("Kiosk:SetupDir", Path.Combine(_root, "setups"));
            b.UseSetting("Kiosk:ApiKey", "secret");
            b.UseSetting("Kiosk:InitialSetupKey", SetupKey);
            b.UseSetting("Kiosk:PanelInitialPassword", "test-panel-password");
        });
        _ = _factory.Server;
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Initial_setup_catalog_and_download_use_scoped_session_token()
    {
        var catalog = _factory.Services.GetRequiredService<InstallerCatalog>();
        byte[] msi = { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 };
        var package = await catalog.AddAsync("Chrome", "chrome.msi", new MemoryStream(msi), true);
        catalog.ConfigureInitialSetup(package.Id, true, true, 1);

        var noKey = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await noKey.GetAsync("/api/setup/catalog")).StatusCode);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Setup-Key", SetupKey);
        var listed = await client.GetFromJsonAsync<InitialSetupCatalog>("/api/setup/catalog");
        Assert.True(listed!.Packages.Single().SelectedByDefault);
        var createResponse = await client.PostAsJsonAsync("/api/setup/sessions", new InitialSetupSessionRequest
        {
            MachineName = "PC NUEVO", SetupVersion = "1.2.3", PackageIds = [package.Id]
        });
        createResponse.EnsureSuccessStatusCode();
        var session = await createResponse.Content.ReadFromJsonAsync<InitialSetupSessionResponse>();
        Assert.NotNull(session);

        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/setup/sessions/{session!.SessionId}/packages/{package.Id}/download")).StatusCode);
        client.DefaultRequestHeaders.Add("X-Setup-Token", session.Token);
        Assert.Equal(msi, await client.GetByteArrayAsync($"/api/setup/sessions/{session.SessionId}/packages/{package.Id}/download"));

        var update = await client.PostAsJsonAsync($"/api/setup/sessions/{session.SessionId}/packages/{package.Id}/status",
            new InitialSetupStatusUpdate { State = InstallationJobState.Succeeded, ProgressPercent = 100, ExitCode = 0 });
        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);
        Assert.True(_factory.Services.GetRequiredService<InitialSetupSessionStore>().Recent().Single().IsTerminal);
    }

    [Fact]
    public async Task Manifest_and_download_require_api_key_and_job_token()
    {
        var catalog = _factory.Services.GetRequiredService<InstallerCatalog>();
        byte[] msi = { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 };
        var package = await catalog.AddAsync("Chrome", "chrome.msi", new MemoryStream(msi), true);
        var jobs = _factory.Services.GetRequiredService<InstallationJobStore>();
        var created = jobs.Create(new FleetDevice { Id = "d1", Name = "PC1" }, package);

        var noKey = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await noKey.GetAsync($"/api/installations/{created.Job.Id}/manifest")).StatusCode);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "secret");
        client.DefaultRequestHeaders.Add("X-Install-Token", "wrong");
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/installations/{created.Job.Id}/manifest")).StatusCode);

        client.DefaultRequestHeaders.Remove("X-Install-Token");
        client.DefaultRequestHeaders.Add("X-Install-Token", created.Token);
        var manifest = await client.GetFromJsonAsync<InstallationManifest>($"/api/installations/{created.Job.Id}/manifest");
        Assert.Equal(package.Sha256, manifest!.Sha256);
        Assert.Equal(msi, await client.GetByteArrayAsync($"/api/installations/{created.Job.Id}/download"));
    }

    [Fact]
    public async Task Maintenance_status_requires_scoped_token_and_marks_job_terminal()
    {
        var jobs = _factory.Services.GetRequiredService<MaintenanceJobStore>();
        var fleet = _factory.Services.GetRequiredService<FleetRegistry>();
        fleet.Upsert("connection-1", "127.0.0.1", new KioskHeartbeat { DeviceId = "d1", Name = "PC1" });
        var created = jobs.Create(fleet.Find("d1")!);
        jobs.MarkDispatched(created.Job.Id);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "secret");
        client.DefaultRequestHeaders.Add("X-Maintenance-Token", created.Token);

        var response = await client.PostAsJsonAsync($"/api/maintenance/{created.Job.Id}/status",
            new MaintenanceStatusUpdate { DeviceId = "d1", State = MaintenanceJobState.Succeeded, ExitCode = 0 });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(MaintenanceJobState.Succeeded, jobs.Recent().Single().State);
        Assert.Null(jobs.Authorize(created.Job.Id, created.Token, "d1"));
        Assert.Equal(KioskStatus.Uninstalled, fleet.Find("d1")!.Status);
        Assert.False(fleet.Find("d1")!.IsOnline);
    }

    public Task DisposeAsync() { _factory.Dispose(); try { Directory.Delete(_root, true); } catch { } return Task.CompletedTask; }
}
