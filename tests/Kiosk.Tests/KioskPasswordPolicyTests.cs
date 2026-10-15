using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using KioskClinicaPC.Core;
using KioskClinicaPC.Equipment;
using KioskClinicaPC.Services;
using Newtonsoft.Json;
using Xunit;

namespace KioskClinicaPC.Tests;

public sealed class KioskPasswordPolicyTests
{
    [Fact]
    public void Installer_verifier_migrates_old_policy_without_requesting_password_renewal()
    {
        string path = Path.GetTempFileName();
        try
        {
            var seed = new PanelPasswordProvisioning(1, PasswordService.Hash("clave-vigente-del-panel"), 1);
            File.WriteAllText(path, JsonConvert.SerializeObject(seed));
            var settings = new KioskSettings
            {
                ServerUrl = "https://panel.test",
                PasswordHash = PasswordService.Hash("antigua-clave-local"),
                PasswordPolicyVersion = 0,
                DeviceId = "equipo-1"
            };
            Assert.True(settings.ApplyPanelPasswordSeed(path));
            Assert.True(PasswordService.Verify("clave-vigente-del-panel", settings.PasswordHash));
            Assert.Equal("equipo-1", settings.DeviceId);
            Assert.False(settings.ApplyPanelPasswordSeed(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Local_password_is_preserved_when_no_server_is_configured()
    {
        string path = Path.GetTempFileName();
        try
        {
            var seed = new PanelPasswordProvisioning(1, PasswordService.Hash("clave-del-panel-test"), 1);
            File.WriteAllText(path, JsonConvert.SerializeObject(seed));
            var settings = new KioskSettings
            {
                PasswordHash = PasswordService.Hash("clave-local-anterior"),
                PasswordPolicyVersion = 0
            };
            Assert.False(settings.ApplyPanelPasswordSeed(path));
            Assert.True(PasswordService.Verify("clave-local-anterior", settings.PasswordHash));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Server_validates_current_password_and_updates_offline_cache()
    {
        string path = Path.GetTempFileName();
        try
        {
            var verifier = new PanelPasswordProvisioning(1, PasswordService.Hash("nueva-clave-del-panel"), 1);
            var settings = new KioskSettings
            {
                ServerUrl = "https://panel.test",
                ServerApiKey = "api-test",
                PasswordHash = PasswordService.Hash("clave-anterior-del-panel")
            };
            using var http = new HttpClient(new Handler(request =>
            {
                Assert.Equal("https://panel.test/api/kiosk/password/verify", request.RequestUri!.ToString());
                Assert.Equal("api-test", request.Headers.GetValues("X-Api-Key").Single());
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonConvert.SerializeObject(verifier))
                };
            }));
            var result = await new PanelPasswordAuthenticator(http)
                .VerifyAsync(settings, "nueva-clave-del-panel", path);
            Assert.True(result.Allowed);
            Assert.True(PasswordService.Verify("nueva-clave-del-panel", KioskSettings.Load(path).PasswordHash));
            Assert.False(PasswordService.Verify("clave-anterior-del-panel", settings.PasswordHash));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Server_rejection_never_falls_back_to_old_cached_password()
    {
        var settings = new KioskSettings
        {
            ServerUrl = "https://panel.test",
            PasswordHash = PasswordService.Hash("clave-anterior-del-panel")
        };
        using var http = new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)));
        var result = await new PanelPasswordAuthenticator(http)
            .VerifyAsync(settings, "clave-anterior-del-panel", "unused.json");
        Assert.False(result.Allowed);
    }

    [Fact]
    public async Task Offline_access_uses_last_verified_password()
    {
        var settings = new KioskSettings
        {
            ServerUrl = "https://panel.test",
            PasswordHash = PasswordService.Hash("clave-validada-anterior")
        };
        using var http = new HttpClient(new Handler(_ => throw new HttpRequestException("offline")));
        var authenticator = new PanelPasswordAuthenticator(http);
        Assert.True((await authenticator.VerifyAsync(settings, "clave-validada-anterior", "unused.json")).Allowed);
        Assert.False((await authenticator.VerifyAsync(settings, "otra-clave", "unused.json")).Allowed);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(send(request));
    }
}
