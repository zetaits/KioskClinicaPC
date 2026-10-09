using System.IO.Compression;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using Kiosk.Server.Services;
using KioskClinicaPC.Equipment;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Kiosk.Server.Tests;
public sealed class SetupReleaseTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "setup-v3-" + Guid.NewGuid().ToString("N"));
    private readonly WebApplicationFactory<Program> _factory;
    private const string Key = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public SetupReleaseTests()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            foreach (string dir in new[] { "Data", "Assets", "Installers", "Setup", "Updates" }) builder.UseSetting("Kiosk:" + dir + "Dir", Path.Combine(_root, dir));
            builder.UseSetting("Kiosk:InitialSetupKey", Key); builder.UseSetting("Kiosk:ReleasePublishKey", "publisher");
            builder.UseSetting("Kiosk:PanelInitialPassword", "test-password");
            builder.ConfigureTestServices(services => services.AddAuthentication(options =>
            { options.DefaultAuthenticateScheme = "Test"; options.DefaultChallengeScheme = "Test"; })
                .AddScheme<AuthenticationSchemeOptions, Auth>("Test", _ => { }));
        });
    }
    private SetupReleaseStore Store => _factory.Services.GetRequiredService<SetupReleaseStore>();
    private sealed class Auth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(Request.Headers["X-Test-Admin"] == "1"
            ? AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "admin")], Scheme.Name)), Scheme.Name))
            : AuthenticateResult.NoResult());
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static (SetupRelease Release, Dictionary<string, byte[]> Bytes) Bundle(string version = "1.5.0", bool unsafeZip = false, bool activationDll = true)
    {
        using var worker = new MemoryStream();
        using (var archive = new ZipArchive(worker, ZipArchiveMode.Create, true))
        {
            foreach (var name in new[] { "KioskSetupHelper.exe", "KioskSetupHelper.dll", "KioskSetupHelper.runtimeconfig.json", "Microsoft.Management.Deployment.winmd" })
            { using var entry = archive.CreateEntry(name).Open(); entry.WriteByte(1); }
            if (activationDll) { using var entry = archive.CreateEntry("Microsoft.Management.Deployment.dll").Open(); entry.WriteByte(1); }
            using (var metadata = archive.CreateEntry("pack-worker.json").Open())
                JsonSerializer.Serialize(metadata, new PackWorkerManifest(1, 3, version, new string('a', 40)), Json);
            if (unsafeZip) { using var bad = archive.CreateEntry("../escape.exe").Open(); bad.WriteByte(1); }
        }
        var bytes = new Dictionary<string, byte[]> { ["online"] = [1, 2, 3], ["complete"] = [4, 5, 6, 7], ["kiosk"] = [8, 9], ["worker"] = worker.ToArray() };
        var editions = new[] { "online", "complete" }.Select(e => new SetupEdition(e, $"Setup-EquipoClinicaPC-{version}{(e == "complete" ? "-Completo" : "")}.exe", bytes[e].Length, Hash(bytes[e]))).ToList();
        var components = new[] { "kiosk", "worker" }.Select(c => new SetupComponent(c, c == "worker" ? version : "1.2.0", bytes[c].Length, Hash(bytes[c]))).ToList();
        return (new(3, "equipment-wpf", 3, 1, version, version, version, "1.2.0", new string('a', 40), "https://panel.invalid", DateTime.UtcNow, editions, components), bytes);
    }
    private static MultipartFormDataContent Form(SetupRelease release, Dictionary<string, byte[]> bytes, string? omit = null)
    {
        var form = new MultipartFormDataContent(); form.Add(new StringContent(JsonSerializer.Serialize(release, Json)), "manifest", "release.json");
        foreach (var (key, content) in bytes)
        {
            if (key == omit) continue;
            string name = key is "online" or "complete" ? release.Editions.Single(e => e.Edition == key).FileName : key == "worker" ? "worker.zip" : "kiosk.exe";
            form.Add(new ByteArrayContent(content), key, name);
        }
        return form;
    }
    private async Task Publish(HttpClient client, SetupRelease release, Dictionary<string, byte[]> bytes)
    { using var form = Form(release, bytes); Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/releases/setup/v3", form)).StatusCode); }
    [Fact]
    public async Task Historical_worker_without_native_activation_remains_readable_but_new_release_is_rejected()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Release-Publish-Key", "publisher");
        var (historical, historicalBytes) = Bundle("1.5.5", activationDll: false);
        await Publish(client, historical, historicalBytes);
        Assert.NotNull(Store.Find("1.5.5"));
        await Store.Activate("1.5.5", default);
        Assert.True(Store.Ready());
        var (current, currentBytes) = Bundle("1.5.6", activationDll: false);
        using var form = Form(current, currentBytes);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/releases/setup/v3", form)).StatusCode);
        Assert.Equal("1.5.5", Store.ActiveVersion);
        Assert.True(Store.Ready());
    }
    [Fact]
    public async Task Panel_password_default_requires_the_private_publisher_key_and_is_not_cached()
    {
        const string route = "/api/releases/setup/kiosk-password";
        using var client = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(route)).StatusCode);
        client.DefaultRequestHeaders.Add("X-Setup-Key", Key);
        client.DefaultRequestHeaders.Add("X-Api-Key", Key);
        client.DefaultRequestHeaders.Add("X-Test-Admin", "1");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(route)).StatusCode);
        client.DefaultRequestHeaders.Add("X-Release-Publish-Key", "incorrect-publisher");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(route)).StatusCode);
        client.DefaultRequestHeaders.Remove("X-Release-Publish-Key");
        client.DefaultRequestHeaders.Add("X-Release-Publish-Key", "publisher");
        using var response = await client.GetAsync(route);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.Equal("1", (await client.GetAsync("/health/ready")).Headers.GetValues("X-Kiosk-Password-Provisioning").Single());
        string body = await response.Content.ReadAsStringAsync();
        var seed = JsonSerializer.Deserialize<PanelPasswordProvisioning>(body, Json)!;
        Assert.True(seed.IsCompatible());
        Assert.True(KioskClinicaPC.Core.PasswordService.Verify("test-password", seed.PasswordHash));
        Assert.DoesNotContain("test-password", body);
        _factory.Services.GetRequiredService<PanelAuthStore>().SetPassword("changed-panel-test-password");
        var changed = JsonSerializer.Deserialize<PanelPasswordProvisioning>(await client.GetStringAsync(route), Json)!;
        Assert.True(KioskClinicaPC.Core.PasswordService.Verify("changed-panel-test-password", changed.PasswordHash));
        Assert.False(KioskClinicaPC.Core.PasswordService.Verify("test-password", changed.PasswordHash));
    }
    [Fact]
    public async Task Import_requires_distinct_key_is_atomic_idempotent_and_does_not_activate()
    {
        using var client = _factory.CreateClient(); var (release, bytes) = Bundle();
        using (var form = Form(release, bytes)) Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/releases/setup/v3", form)).StatusCode);
        client.DefaultRequestHeaders.Add("X-Setup-Key", Key);
        using (var form = Form(release, bytes)) Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/releases/setup/v3", form)).StatusCode);
        client.DefaultRequestHeaders.Add("X-Release-Publish-Key", "publisher");
        using (var missing = Form(release, bytes, "complete")) Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/releases/setup/v3", missing)).StatusCode);
        var badBytes = new Dictionary<string, byte[]>(bytes) { ["online"] = [3, 2, 1] };
        using (var bad = Form(release, badBytes)) Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/releases/setup/v3", bad)).StatusCode);
        Assert.Empty(Store.List()); Assert.Null(Store.ActiveVersion);
        await Publish(client, release, bytes); await Publish(client, release, bytes); Assert.Null(Store.ActiveVersion);
        var changed = release with { Editions = release.Editions.Select(e => e.Edition == "online" ? e with { Sha256 = Hash(badBytes["online"]) } : e).ToList() };
        using (var different = Form(changed, badBytes))
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/releases/setup/v3", different)).StatusCode);
        Assert.Single(Store.List());
    }
    [Fact]
    public async Task Components_require_setup_key_support_etag_ranges_and_reject_orphans()
    {
        using var client = _factory.CreateClient(); var (release, bytes) = Bundle(); client.DefaultRequestHeaders.Add("X-Release-Publish-Key", "publisher");
        await Publish(client, release, bytes);
        var component = release.Components.Single(c => c.Kind == "kiosk"); string route = $"/api/setup/v3/components/kiosk/{component.Sha256}";
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(route)).StatusCode);
        client.DefaultRequestHeaders.Add("X-Setup-Key", Key);
        using var request = new HttpRequestMessage(HttpMethod.Get, route); request.Headers.Range = new(1, null);
        request.Headers.IfRange = new(new System.Net.Http.Headers.EntityTagHeaderValue("\"" + component.Sha256 + "\""));
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode); Assert.Equal(new byte[] { 9 }, await response.Content.ReadAsByteArrayAsync());
        Assert.Equal("\"" + component.Sha256 + "\"", response.Headers.ETag!.ToString());
        using var wrongTag = new HttpRequestMessage(HttpMethod.Get, route); wrongTag.Headers.Range = new(1, null); wrongTag.Headers.IfRange = new(new System.Net.Http.Headers.EntityTagHeaderValue("\"stale\""));
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(wrongTag)).StatusCode);
        string orphan = new string('b', 64); File.WriteAllBytes(Path.Combine(_root, "Setup", "v3", "components", "kiosk", orphan), [1]);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/setup/v3/components/kiosk/{orphan}")).StatusCode);
        Assert.Equal("1", (await client.GetAsync("/health/ready")).Headers.GetValues("X-Setup-Component-Protocol").Single());
    }
    [Fact]
    public async Task Activation_requires_admin_and_antiforgery_and_downloads_keep_one_version()
    {
        using var client = _factory.CreateClient(new() { AllowAutoRedirect = false }); var (release, bytes) = Bundle(); client.DefaultRequestHeaders.Add("X-Release-Publish-Key", "publisher");
        await Publish(client, release, bytes);
        using (var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["version"] = release.Version }))
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/panel/setup/activate", form)).StatusCode);
        client.DefaultRequestHeaders.Add("X-Test-Admin", "1");
        using (var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["version"] = release.Version }))
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/panel/setup/activate", form)).StatusCode);
        Assert.Null(Store.ActiveVersion);
        string html = await client.GetStringAsync("/instalador");
        var token = System.Text.RegularExpressions.Regex.Match(html, "<input[^>]*name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\""); Assert.True(token.Success);
        using (var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["version"] = release.Version, ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token.Groups[1].Value) }))
            Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/panel/setup/activate", form)).StatusCode);
        Assert.Equal(release.Version, Store.ActiveVersion);
        Assert.Equal(bytes["online"], await client.GetByteArrayAsync("/panel/setup/download"));
        Assert.Equal(bytes["complete"], await client.GetByteArrayAsync("/panel/setup/download?edition=complete"));
        var fresh = new SetupReleaseStore(Path.Combine(_root, "Setup")); Assert.Equal(release.Version, fresh.ActiveVersion);
        var (second, secondBytes) = Bundle("1.6.0"); await Publish(client, second, secondBytes); Assert.Equal(release.Version, Store.ActiveVersion);
        await Store.Activate(second.Version, default); Assert.Equal(release.Version, Store.PreviousVersion);
        await Store.Activate(release.Version, default); Assert.Equal(release.Version, fresh.ActiveVersion);
        File.WriteAllBytes(Store.EditionPath(second, "complete"), [0]);
        await Assert.ThrowsAsync<InvalidDataException>(() => Store.Activate(second.Version, default)); Assert.Equal(release.Version, Store.ActiveVersion);
    }
    [Fact]
    public async Task Legacy_offer_survives_candidates_and_can_be_restored_without_deleting_versions()
    {
        using var client = _factory.CreateClient(); client.DefaultRequestHeaders.Add("X-Test-Admin", "1"); client.DefaultRequestHeaders.Add("X-Release-Publish-Key", "publisher");
        byte[] oldBytes = [10, 11, 12];
        var legacy = new InitialSetupBundleManifest { SchemaVersion = 2, InstallerKind = "equipment-wpf", CatalogApiVersion = 3,
            Version = "1.4.0", AssistantVersion = "1.4.0", WorkerVersion = "1.4.0", KioskVersion = "1.2.0", SourceCommit = new string('a', 40),
            ServerUrl = "https://panel.invalid", FileName = "Setup-EquipoClinicaPC-1.4.0.exe", SizeBytes = oldBytes.Length, Sha256 = Hash(oldBytes) };
        using (var stream = new MemoryStream(oldBytes)) await _factory.Services.GetRequiredService<InitialSetupBundleStore>().ImportAsync(legacy, legacy.FileName, stream, default);
        var (conflicting, conflictingBytes) = Bundle("1.4.0");
        using (var form = Form(conflicting, conflictingBytes)) Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/releases/setup/v3", form)).StatusCode);
        var (release, bytes) = Bundle(); await Publish(client, release, bytes);
        string html = WebUtility.HtmlDecode(await client.GetStringAsync("/instalador"));
        Assert.Contains($"href=\"/panel/setup/download?version={release.Version}&edition=online\"", html);
        Assert.Contains($"href=\"/panel/setup/download?version={release.Version}&edition=complete\"", html);
        Assert.Contains("Pendiente de activación", html);
        Assert.Contains("data-setup-download-status", html);
        Assert.DoesNotContain("1.4.0", html);
        Assert.DoesNotContain("version=legacy", html);
        Assert.Null(Store.ActiveVersion);
        var legacyConflict = new InitialSetupBundleManifest { SchemaVersion = 2, InstallerKind = "equipment-wpf", CatalogApiVersion = 3,
            Version = release.Version, AssistantVersion = release.Version, WorkerVersion = release.Version, KioskVersion = "1.2.0", SourceCommit = new string('a', 40),
            ServerUrl = "https://panel.invalid", FileName = $"Setup-EquipoClinicaPC-{release.Version}.exe", SizeBytes = oldBytes.Length, Sha256 = Hash(oldBytes) };
        using (var stream = new MemoryStream(oldBytes))
            await Assert.ThrowsAsync<InvalidDataException>(() => _factory.Services.GetRequiredService<InitialSetupBundleStore>().ImportAsync(legacyConflict, legacyConflict.FileName, stream, default));
        Assert.Equal(oldBytes, await client.GetByteArrayAsync("/panel/setup/download"));
        Assert.Equal(bytes["complete"], await client.GetByteArrayAsync($"/panel/setup/download?version={release.Version}&edition=complete"));
        await Store.Activate(release.Version, default); Assert.Equal(bytes["online"], await client.GetByteArrayAsync("/panel/setup/download"));
        await Store.Activate(null, default); Assert.Equal(oldBytes, await client.GetByteArrayAsync("/panel/setup/download")); Assert.Single(Store.List());
        var (latest, latestBytes) = Bundle("1.6.0"); await Publish(client, latest, latestBytes);
        await Store.Activate(release.Version, default);
        html = WebUtility.HtmlDecode(await client.GetStringAsync("/instalador"));
        Assert.True(html.IndexOf("Descargar Online", StringComparison.Ordinal) < html.IndexOf("Instalar sistemas por red", StringComparison.Ordinal));
        Assert.Contains($"href=\"/panel/setup/download?version={latest.Version}&edition=online\"", html);
        Assert.True(html.IndexOf("Versión 1.6.0", StringComparison.Ordinal) < html.IndexOf("Versión 1.5.0", StringComparison.Ordinal));
        Assert.Contains("Otras versiones y recuperación", html);
        Assert.DoesNotContain("1.4.0", html);
        Assert.Equal(release.Version, Store.ActiveVersion);
    }
    [Fact]
    public async Task Corrupt_active_pointer_or_active_files_make_readiness_unavailable()
    {
        using var client = _factory.CreateClient(); client.DefaultRequestHeaders.Add("X-Release-Publish-Key", "publisher");
        var (release, bytes) = Bundle(); await Publish(client, release, bytes); await Store.Activate(release.Version, default);
        File.WriteAllBytes(Store.EditionPath(release, "online"), [0]);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/health/ready")).StatusCode);
        File.WriteAllText(Path.Combine(_root, "Setup", "v3", "active.json"), "{");
        Assert.False(Store.Ready()); Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/health/ready")).StatusCode);
    }
    [Fact]
    public async Task Unsafe_worker_is_rejected_before_candidate_becomes_visible()
    {
        using var client = _factory.CreateClient(); client.DefaultRequestHeaders.Add("X-Release-Publish-Key", "publisher");
        var (release, bytes) = Bundle(unsafeZip: true);
        using var form = Form(release, bytes); Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/releases/setup/v3", form)).StatusCode);
        Assert.Empty(Store.List()); Assert.Null(Store.ActiveVersion);
    }
    public void Dispose() { _factory.Dispose(); if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
