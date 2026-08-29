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
            b.UseSetting("Kiosk:ApiKey", "secret");
            b.UseSetting("Kiosk:PanelInitialPassword", "test-panel-password");
        });
        _ = _factory.Server;
        return Task.CompletedTask;
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
