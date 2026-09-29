using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kiosk.Server.Services;
using KioskClinicaPC.Core.Sync;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kiosk.Server.Tests;

public sealed class KioskUpdateApiIntegrationTests : IAsyncLifetime
{
    private const string DeviceId = "11111111111111111111111111111111";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "kiosk-update-api-tests", Guid.NewGuid().ToString("N"));
    private readonly ECDsa _signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
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
            b.UseSetting("Kiosk:UpdatesDir", Path.Combine(_root, "updates"));
            b.UseSetting("Kiosk:ApiKey", "fleet-secret");
            b.UseSetting("Kiosk:ReleasePublishKey", "publish-secret");
            b.UseSetting("Kiosk:UpdateTokenKey", "token-secret");
            b.UseSetting("Kiosk:UpdateSigningKeys:test", _signer.ExportSubjectPublicKeyInfoPem());
            b.UseSetting("Kiosk:PanelInitialPassword", "test-panel-password");
        });
        _ = _factory.Server;
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Publish_assignment_download_and_status_are_scoped()
    {
        byte[] setup = [0x4d, 0x5a, 1, 2, 3];
        var manifest = new KioskReleaseManifest
        {
            Version = "1.2.0", FileName = "Setup-KioskClinicaPC-1.2.0.exe", SizeBytes = setup.Length,
            Sha256 = Convert.ToHexString(SHA256.HashData(setup)).ToLowerInvariant(), PublishedAtUtc = DateTime.UtcNow,
            KeyId = "test", MinimumUpdaterVersion = "1.2.0"
        };
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        string signature = Convert.ToBase64String(_signer.SignData(bytes, HashAlgorithmName.SHA256));

        using var form = new MultipartFormDataContent();
        var manifestPart = new ByteArrayContent(bytes); manifestPart.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        form.Add(manifestPart, "manifest", "manifest.json");
        form.Add(new StringContent(signature, Encoding.ASCII), "signature");
        form.Add(new ByteArrayContent(setup), "setup", manifest.FileName);
        var publisher = _factory.CreateClient();
        publisher.DefaultRequestHeaders.Add("X-Release-Publish-Key", "publish-secret");
        (await publisher.PostAsync("/api/releases", form)).EnsureSuccessStatusCode();

        var store = _factory.Services.GetRequiredService<KioskUpdateStore>();
        store.Activate("1.2.0", []);
        var kiosk = _factory.CreateClient();
        kiosk.DefaultRequestHeaders.Add("X-Api-Key", "fleet-secret");
        var assignment = await kiosk.GetFromJsonAsync<KioskUpdateAssignment>(
            $"/api/updates/assignment?deviceId={DeviceId}&deviceName=PC1&currentVersion=1.1.0");
        Assert.NotNull(assignment);

        Assert.Equal(HttpStatusCode.NotFound,
            (await kiosk.GetAsync($"/api/updates/{assignment!.JobId}/manifest")).StatusCode);
        kiosk.DefaultRequestHeaders.Add("X-Update-Token", assignment.Token);
        using var manifestResponse = await kiosk.GetAsync($"/api/updates/{assignment.JobId}/manifest");
        manifestResponse.EnsureSuccessStatusCode();
        Assert.Equal(signature, manifestResponse.Headers.GetValues("X-Update-Signature").Single());
        Assert.Equal(setup, await kiosk.GetByteArrayAsync($"/api/updates/{assignment.JobId}/download"));

        var status = await kiosk.PostAsJsonAsync($"/api/updates/{assignment.JobId}/status", new KioskUpdateStatusUpdate
        { DeviceId = DeviceId, State = KioskUpdateState.Downloading, ProgressPercent = 25 });
        Assert.Equal(HttpStatusCode.NoContent, status.StatusCode);

        using var invalidStatus = new StringContent(
            $"{{\"deviceId\":\"{DeviceId}\",\"state\":999}}", Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.BadRequest,
            (await kiosk.PostAsync($"/api/updates/{assignment.JobId}/status", invalidStatus)).StatusCode);
    }

    public Task DisposeAsync()
    {
        _factory.Dispose(); _signer.Dispose();
        try { Directory.Delete(_root, true); } catch { }
        return Task.CompletedTask;
    }
}
