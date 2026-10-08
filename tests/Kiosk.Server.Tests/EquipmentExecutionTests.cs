using System.Net;
using System.Net.Http.Json;
using KioskClinicaPC.Core.Sync;
using KioskClinicaPC.Equipment;
using Xunit;

namespace Kiosk.Server.Tests;
public sealed class EquipmentExecutionTests
{
    private static PackApplication App => new(new string('a', 32), "Vendor.App", "App", "2");
    private static PackCatalog Catalog => new(10, [App]);
    private static EquipmentRequest Request(bool pack = true, bool kiosk = true) => new(pack, kiosk, 10, pack ? [new(App.Id, App.PinnedVersion)] : [], true);
    private sealed class Pack(List<string> calls) : IEquipmentPackSession
    {
        public bool PreflightOk = true, Complete = true, AllowPartial;
        public bool RebootRequired { get; set; }
        public Task<bool> Preflight(PackCatalog snapshot, bool resume, Action<EquipmentEvent> progress, CancellationToken ct, bool allowPartial = false)
        { AllowPartial = allowPartial; calls.Add("check-all"); Assert.Equal(Catalog.Revision, snapshot.Revision); Assert.Equal(Catalog.Applications, snapshot.Applications); progress(new("applications", "Comprobado")); return Task.FromResult(PreflightOk); }
        public Task<bool> Install(Action<EquipmentEvent> progress, CancellationToken ct) { calls.Add("pack"); ct.ThrowIfCancellationRequested(); return Task.FromResult(Complete); }
        public ValueTask DisposeAsync() { calls.Add("dispose"); return ValueTask.CompletedTask; }
    }
    private sealed class Kiosk(List<string> calls) : IEquipmentKiosk
    {
        public bool Complete = true; public Action? DuringInstall;
        public bool RebootRequired { get; set; }
        public Task<bool> InstallAndVerify(bool resume, Action<EquipmentEvent> progress, CancellationToken ct) { calls.Add("kiosk"); DuringInstall?.Invoke(); return Task.FromResult(Complete); }
    }
    [Theory]
    [InlineData(true, true, "catalog,check-all,kiosk,pack,dispose")]
    [InlineData(true, false, "catalog,check-all,pack,dispose")]
    [InlineData(false, true, "kiosk")]
    public async Task Components_execute_in_authorized_order(bool packSelected, bool kioskSelected, string expected)
    {
        var calls = new List<string>(); var pack = new Pack(calls); var kiosk = new Kiosk(calls); var events = new List<EquipmentEvent>();
        var execution = new EquipmentExecution(_ => { calls.Add("catalog"); return Task.FromResult(Catalog); }, () => pack, kiosk);
        var result = await execution.Run(Request(packSelected, kioskSelected), events.Add, CancellationToken.None);
        Assert.Equal(0, result.ExitCode); Assert.Equal(kioskSelected, result.KioskVerified); Assert.Equal(expected, string.Join(',', calls));
        Assert.All(events, e => Assert.Null(e.Percent));
    }
    [Fact]
    public async Task Revision_or_versions_changed_before_installation_stop_before_bootstrap()
    {
        foreach (var catalog in new[] { Catalog with { Revision = 11 }, new(10, [App with { PinnedVersion = "3" }]), new(10, []) })
        {
            var calls = new List<string>();
            var execution = new EquipmentExecution(_ => Task.FromResult(catalog), () => throw new Exception("Must not prepare"), new Kiosk(calls));
            var result = await execution.Run(Request(), _ => { }, CancellationToken.None);
            Assert.Equal("review", result.Kind); Assert.Equal(1, result.ExitCode); Assert.Empty(calls);
        }
    }
    [Fact]
    public async Task Failed_preflight_prevents_kiosk_and_pack_installation()
    {
        var calls = new List<string>(); var pack = new Pack(calls) { PreflightOk = false };
        var result = await new EquipmentExecution(_ => Task.FromResult(Catalog), () => pack, new Kiosk(calls)).Run(Request(), _ => { }, CancellationToken.None);
        Assert.Equal(1, result.ExitCode); Assert.Equal(new[] { "check-all", "dispose" }, calls);
    }
    [Fact]
    public async Task Partial_pack_option_reaches_worker_and_preserves_incomplete_result_and_verified_kiosk()
    {
        var calls = new List<string>(); var pack = new Pack(calls) { Complete = false };
        var result = await new EquipmentExecution(_ => Task.FromResult(Catalog), () => pack, new Kiosk(calls))
            .Run(Request() with { AllowPartialPack = true }, _ => { }, CancellationToken.None);
        Assert.True(pack.AllowPartial); Assert.True(result.KioskVerified); Assert.Equal(2, result.ExitCode);
        Assert.Equal(new[] { "check-all", "kiosk", "pack", "dispose" }, calls);
    }
    [Fact]
    public async Task Cancel_after_kiosk_retains_verified_component_and_does_not_start_pack()
    {
        using var cancel = new CancellationTokenSource(); var calls = new List<string>(); var pack = new Pack(calls);
        var kiosk = new Kiosk(calls) { DuringInstall = cancel.Cancel };
        var result = await new EquipmentExecution(_ => Task.FromResult(Catalog), () => pack, kiosk).Run(Request(), _ => { }, cancel.Token);
        Assert.Equal(2, result.ExitCode); Assert.True(result.KioskVerified); Assert.DoesNotContain("pack", calls);
    }
    [Fact]
    public async Task Incomplete_pack_is_not_global_success_and_reboot_is_reported()
    {
        var calls = new List<string>(); var pack = new Pack(calls) { Complete = false, RebootRequired = true };
        var result = await new EquipmentExecution(_ => Task.FromResult(Catalog), () => pack, new Kiosk(calls)).Run(Request(), _ => { }, CancellationToken.None);
        Assert.Equal(2, result.ExitCode); Assert.True(result.KioskVerified); Assert.True(result.RebootRequired);
    }
    [Fact]
    public void Empty_components_and_duplicate_or_unselected_apps_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => EquipmentPolicy.ValidateRequest(Request(false, false)));
        Assert.Throws<ArgumentException>(() => EquipmentPolicy.ValidateRequest(Request() with { Applications = [] }));
        Assert.Throws<ArgumentException>(() => EquipmentPolicy.ValidateRequest(Request() with { Applications = [new(App.Id, "2"), new(App.Id, "2")] }));
        Assert.Throws<ArgumentException>(() => EquipmentPolicy.ValidateRequest(Request(false, true) with { Applications = [new(App.Id, "2")] }));
    }
    [Fact]
    public void Latest_selection_ignores_observed_versions_but_requires_same_definition_and_policy()
    {
        var authorized = PackDefinition.FromCatalog(Catalog).ForExecution();
        var request = Request() with { ResolveLatest = true, Applications = [new(App.Id, "stale observation")] };
        var snapshot = EquipmentPolicy.Snapshot(request, authorized);
        Assert.Empty(snapshot.Applications.Single().PinnedVersion); Assert.NotNull(snapshot.Definition);
        authorized.Definition!.Applications.Clear(); Assert.Single(snapshot.Definition!.Applications);
        Assert.Throws<SelectionChangedException>(() => EquipmentPolicy.Snapshot(request, Catalog));
        Assert.Throws<SelectionChangedException>(() => EquipmentPolicy.Snapshot(Request(), PackDefinition.FromCatalog(Catalog).ForExecution()));
        Assert.Throws<SelectionChangedException>(() => EquipmentPolicy.Snapshot(request with { CatalogRevision = 9 }, PackDefinition.FromCatalog(Catalog).ForExecution()));
    }
    [Fact]
    public void Authorized_snapshot_has_no_alias_to_mutable_catalogue_list()
    {
        var catalog = Catalog; var snapshot = EquipmentPolicy.Snapshot(Request(), catalog);
        catalog.Applications.Clear(); Assert.Single(snapshot.Applications);
    }
    private sealed class Handler(Func<int, HttpResponseMessage> response) : HttpMessageHandler
    {
        public int Attempts; public List<string> Paths = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            Assert.Equal("fictional-key", request.Headers.GetValues("X-Setup-Key").Single());
            return Task.FromResult(response(++Attempts));
        }
    }
    private static HttpResponseMessage Response(PackCatalog catalog, bool version = true)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(PackDefinition.FromCatalog(catalog)) };
        if (version) response.Headers.Add("X-Setup-Catalog-Version", "3"); return response;
    }
    [Fact]
    public async Task Catalogue_retries_transient_failures_only_and_never_uses_private_catalogue()
    {
        var handler = new Handler(attempt => attempt < 3 ? new(HttpStatusCode.ServiceUnavailable) : Response(Catalog));
        using var http = new HttpClient(handler);
        var catalog = await new EquipmentCatalogClient(http, new("https://panel.invalid", "fictional-key"), (_, _) => Task.CompletedTask).Load(CancellationToken.None);
        Assert.Equal(10, catalog.Revision); Assert.Equal(3, handler.Attempts);
        Assert.All(handler.Paths, path => Assert.Equal("/api/setup/v3/catalog", path));
    }
    [Theory]
    [InlineData(401, CatalogFailure.Unauthorized, 1)]
    [InlineData(403, CatalogFailure.Forbidden, 1)]
    [InlineData(400, CatalogFailure.Incompatible, 1)]
    [InlineData(404, CatalogFailure.Incompatible, 1)]
    [InlineData(503, CatalogFailure.Unavailable, 3)]
    [InlineData(429, CatalogFailure.Unavailable, 3)]
    public async Task Definitive_errors_have_distinct_safe_messages(int status, CatalogFailure failure, int attempts)
    {
        var handler = new Handler(_ => new((HttpStatusCode)status)); using var http = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<CatalogException>(() => new EquipmentCatalogClient(http, new("https://panel.invalid", "fictional-key"), (_, _) => Task.CompletedTask).Load(CancellationToken.None));
        Assert.Equal(failure, error.Failure); Assert.Equal(attempts, handler.Attempts); Assert.DoesNotContain("fictional-key", error.Message);
    }
    [Fact]
    public async Task Old_format_or_missing_version_header_is_rejected_without_fallback()
    {
        foreach (var response in new[] { Response(Catalog, false), new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"packages\":[]}") } })
        {
            var handler = new Handler(_ => response); using var http = new HttpClient(handler);
            var error = await Assert.ThrowsAsync<CatalogException>(() => new EquipmentCatalogClient(http, new("https://panel.invalid", "fictional-key")).Load(CancellationToken.None));
            Assert.Equal(CatalogFailure.Incompatible, error.Failure); Assert.Single(handler.Paths);
        }
    }
    [Fact]
    public async Task Empty_catalogue_is_valid_but_cannot_produce_an_installable_pack_request()
    {
        var handler = new Handler(_ => Response(new(3, []))); using var http = new HttpClient(handler);
        Assert.Empty((await new EquipmentCatalogClient(http, new("https://panel.invalid", "fictional-key")).Load(CancellationToken.None)).Applications);
    }
    [Fact]
    public async Task Unconfigured_assistant_never_calls_server()
    {
        var handler = new Handler(_ => throw new Exception()); using var http = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<CatalogException>(() => new EquipmentCatalogClient(http, new("", "")).Load(CancellationToken.None));
        Assert.Equal(CatalogFailure.Unconfigured, error.Failure); Assert.Empty(handler.Paths);
    }
    private sealed class TimeoutHandler : HttpMessageHandler
    {
        public int Attempts;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { Attempts++; throw new TaskCanceledException(); }
    }
    [Fact]
    public async Task Timeout_is_bounded_to_three_attempts_and_user_cancel_is_not_retried()
    {
        var handler = new TimeoutHandler(); using var http = new HttpClient(handler);
        var client = new EquipmentCatalogClient(http, new("https://panel.invalid", "fictional-key"), (_, _) => Task.CompletedTask);
        Assert.Equal(CatalogFailure.Timeout, (await Assert.ThrowsAsync<CatalogException>(() => client.Load(CancellationToken.None))).Failure); Assert.Equal(3, handler.Attempts);
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.Load(cancel.Token)); Assert.Equal(3, handler.Attempts);
    }
}
