using Bunit;
using Kiosk.Server.Components.Pages;
using Kiosk.Server.Components.Shared.Installer;
using Kiosk.Server.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kiosk.Server.Tests;

public sealed class InstallerComponentTests
{
    [Fact]
    public async Task History_is_requested_once_on_first_click_and_repeated_clicks_share_the_pending_result()
    {
        using var context = new BunitContext();
        var pending = new TaskCompletionSource<InstallerSectionState<InstallerHistoryData>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var query = new QueryStub { History = ct => pending.Task.WaitAsync(ct) };
        context.Services.AddSingleton<IInstallerPageQuery>(query);
        var history = context.Render<InstallerHistory>(p => p.Add(x => x.DisplayedVersion, "1.5.9"));
        Assert.Equal(0, query.HistoryCalls);
        Task click = history.Find("summary").ClickAsync(new MouseEventArgs());
        Assert.Equal("true", history.Find("[aria-busy]").GetAttribute("aria-busy"));
        await history.Find("summary").ClickAsync(new MouseEventArgs());
        Assert.Equal(1, query.HistoryCalls);
        pending.SetResult(ReadyHistory());
        await click;
        history.WaitForAssertion(() => Assert.Contains("Versión 1.5.8", history.Markup));
        await history.Find("summary").ClickAsync(new MouseEventArgs());
        Assert.Equal(1, query.HistoryCalls);
        Assert.Contains("1 disponibles", history.Find("summary").TextContent);
        Assert.Equal("false", history.Find("[aria-busy]").GetAttribute("aria-busy"));
        Assert.Equal("1.5.9", query.DisplayedVersion);
    }

    [Fact]
    public async Task History_retry_recovers_from_error_and_empty_is_shown_only_after_completion()
    {
        using var context = new BunitContext();
        int attempt = 0;
        var query = new QueryStub { History = _ => Task.FromResult(++attempt == 1
            ? InstallerSectionState<InstallerHistoryData>.Error("Fallo de prueba")
            : InstallerSectionState<InstallerHistoryData>.Empty()) };
        context.Services.AddSingleton<IInstallerPageQuery>(query);
        var history = context.Render<InstallerHistory>(p => p.Add(x => x.DisplayedVersion, "1.5.9"));
        Assert.DoesNotContain("No hay otras versiones", history.Markup);
        await history.Find("summary").ClickAsync(new MouseEventArgs());
        history.WaitForAssertion(() => Assert.Contains("Fallo de prueba", history.Markup));
        await history.Find("button").ClickAsync(new MouseEventArgs());
        history.WaitForAssertion(() => Assert.Contains("No hay otras versiones disponibles", history.Markup));
        Assert.Equal(2, query.HistoryCalls);
    }

    [Fact]
    public async Task Page_retry_changes_only_failed_section_and_keeps_downloads_in_the_other_section()
    {
        using var context = new BunitContext();
        int attempt = 0;
        var pending = new TaskCompletionSource<InstallerSectionState<SetupSectionData>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var query = new QueryStub
        {
            Setup = ct => ++attempt == 1 ? Task.FromResult(InstallerSectionState<SetupSectionData>.Error("Fallo de prueba")) : pending.Task.WaitAsync(ct),
            Deployment = _ => Task.FromResult(InstallerSectionState<DeploymentSectionData>.Ready(new("0.1.0")))
        };
        context.Services.AddSingleton<IInstallerPageQuery>(query);
        var page = context.Render<Installer>();
        Assert.Contains("version=0.1.0", page.Find("[data-download-cookie]").GetAttribute("href"));
        Task click = page.Find(".setup-hero button").ClickAsync(new MouseEventArgs());
        Assert.Equal("true", page.Find(".setup-hero").GetAttribute("aria-busy"));
        Assert.Empty(page.FindAll(".setup-hero a[data-setup-download]"));
        Assert.Single(page.FindAll("[data-download-cookie]"));
        pending.SetResult(ReadySetup());
        await click;
        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll(".setup-hero a[data-setup-download]").Count));
        Assert.Equal(2, query.SetupCalls);
        Assert.Equal(1, query.DeploymentCalls);
        Assert.Equal(0, query.HistoryCalls);
    }

    [Fact]
    public async Task Page_renders_successful_section_while_other_is_pending_and_preserves_visual_order()
    {
        using var context = new BunitContext();
        var pending = new TaskCompletionSource<InstallerSectionState<DeploymentSectionData>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var query = new QueryStub { Setup = _ => Task.FromResult(ReadySetup()), Deployment = ct => pending.Task.WaitAsync(ct) };
        context.Services.AddSingleton<IInstallerPageQuery>(query);
        var page = context.Render<Installer>();
        Assert.Equal("false", page.Find(".setup-hero").GetAttribute("aria-busy"));
        Assert.Equal("true", page.Find("section.card:not(.setup-hero)").GetAttribute("aria-busy"));
        Assert.Equal(new[] { "Preparar este equipo", "Instalar sistemas por red" }, page.FindAll("section h2").Select(e => e.TextContent));
        Assert.Contains("Descargar Online", page.Markup);
        pending.SetResult(InstallerSectionState<DeploymentSectionData>.Empty());
        page.WaitForAssertion(() => Assert.Equal("false", page.Find("section.card:not(.setup-hero)").GetAttribute("aria-busy")));
        await page.Find("summary").ClickAsync(new MouseEventArgs());
        page.WaitForAssertion(() => Assert.Contains("No hay otras versiones disponibles", page.Markup));
    }

    [Fact]
    public async Task Disposing_history_cancels_pending_query_and_does_not_surface_error()
    {
        using var context = new BunitContext();
        var pending = new TaskCompletionSource<InstallerSectionState<InstallerHistoryData>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var query = new QueryStub { History = ct => pending.Task.WaitAsync(ct) };
        context.Services.AddSingleton<IInstallerPageQuery>(query);
        var history = context.Render<InstallerHistory>(p => p.Add(x => x.DisplayedVersion, "1.5.9"));
        Task click = history.Find("summary").ClickAsync(new MouseEventArgs());
        await context.DisposeComponentsAsync();
        await click.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(query.HistoryToken.IsCancellationRequested);
    }

    private static InstallerSectionState<SetupSectionData> ReadySetup() => InstallerSectionState<SetupSectionData>.Ready(
        new(new("1.5.9", [new("online", 1234), new("complete", 5678)]), "1.5.9"));

    private static InstallerSectionState<InstallerHistoryData> ReadyHistory() => InstallerSectionState<InstallerHistoryData>.Ready(
        new([new("1.5.8", [new("online", 1234), new("complete", 5678)])]));

    private sealed class QueryStub : IInstallerPageQuery
    {
        public Func<CancellationToken, Task<InstallerSectionState<SetupSectionData>>> Setup = _ => Task.FromResult(InstallerSectionState<SetupSectionData>.Empty());
        public Func<CancellationToken, Task<InstallerSectionState<DeploymentSectionData>>> Deployment = _ => Task.FromResult(InstallerSectionState<DeploymentSectionData>.Empty());
        public Func<CancellationToken, Task<InstallerSectionState<InstallerHistoryData>>> History = _ => Task.FromResult(InstallerSectionState<InstallerHistoryData>.Empty());
        public int SetupCalls, DeploymentCalls, HistoryCalls;
        public string? DisplayedVersion;
        public CancellationToken HistoryToken;
        public Task<InstallerSectionState<SetupSectionData>> GetSetupSummaryAsync(CancellationToken ct) { SetupCalls++; return Setup(ct); }
        public Task<InstallerSectionState<DeploymentSectionData>> GetDeploymentSummaryAsync(CancellationToken ct) { DeploymentCalls++; return Deployment(ct); }
        public Task<InstallerSectionState<InstallerHistoryData>> GetHistoryAsync(string displayedVersion, CancellationToken ct)
        { HistoryCalls++; DisplayedVersion = displayedVersion; HistoryToken = ct; return History(ct); }
    }
}
