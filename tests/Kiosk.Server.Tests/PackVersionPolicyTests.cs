using KioskClinicaPC.Core.Sync;
using Xunit;

namespace Kiosk.Server.Tests;

public sealed class PackVersionPolicyTests
{
    private static int? Compare(PackVersionCandidate a, PackVersionCandidate b) =>
        Version.Parse(a.Version).CompareTo(Version.Parse(b.Version));

    [Fact]
    public async Task Highest_compatible_version_in_default_channel_wins_using_package_comparison()
    {
        var visited = new List<string>();
        var selected = await PackVersionPolicy.Select([new("9.0", ""), new("10.0", ""), new("11.0", ""), new("99.0", "beta")], "", Compare,
            candidate => { visited.Add(candidate.Version); return Task.FromResult(candidate.Version != "11.0"); }, CancellationToken.None);
        Assert.Equal("10.0", selected.Version); Assert.Equal(new[] { "11.0", "10.0" }, visited);
    }
    [Fact]
    public async Task Network_failure_never_downgrades_silently_to_an_older_candidate()
    {
        int checkedCount = 0;
        await Assert.ThrowsAsync<IOException>(() => PackVersionPolicy.Select([new("9.0", ""), new("10.0", "")], "", Compare,
            _ => { checkedCount++; throw new IOException("network"); }, CancellationToken.None));
        Assert.Equal(1, checkedCount);
    }
    [Fact]
    public void Duplicate_and_unknown_versions_fail_closed()
    {
        Assert.Throws<InvalidDataException>(() => PackVersionPolicy.Order([new("1.0", ""), new("1.0", "")], "", Compare));
        Assert.ThrowsAny<Exception>(() => PackVersionPolicy.Order([new("unknown", ""), new("1.0", "")], "", (_, _) => null));
    }
    [Fact]
    public async Task No_compatible_candidate_and_cancelled_resolution_never_select_a_version()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => PackVersionPolicy.Select([new("1.0", "")], "", Compare, _ => Task.FromResult(false), CancellationToken.None));
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PackVersionPolicy.Select([new("1.0", "")], "", Compare,
            _ => throw new Exception("Must not evaluate after cancellation"), cancel.Token));
    }
    [Fact]
    public async Task Catalog_drift_has_one_extra_refresh_shared_by_all_applications()
    {
        int refreshes = 0, checks = 0;
        var session = new PackCatalogSession(_ => { refreshes++; return Task.CompletedTask; });
        await session.Prepare(CancellationToken.None); await session.Prepare(CancellationToken.None);
        await session.Check(() => { if (++checks == 1) throw new CatalogDriftException("removed"); return Task.CompletedTask; }, CancellationToken.None);
        await Assert.ThrowsAsync<CatalogDriftException>(() => session.Check(() => throw new CatalogDriftException("still absent"), CancellationToken.None));
        Assert.Equal(2, refreshes); Assert.Equal(2, checks);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Failed_initial_or_extra_refresh_blocks_stale_resolution_of_other_apps(bool extra)
    {
        int refreshes = 0;
        var session = new PackCatalogSession(_ => { if (++refreshes == (extra ? 2 : 1)) throw new IOException("offline"); return Task.CompletedTask; });
        await session.Prepare(CancellationToken.None);
        await Assert.ThrowsAsync<IOException>(() => session.Check(() => throw new CatalogDriftException("drift"), CancellationToken.None));
        await Assert.ThrowsAsync<IOException>(() => session.Check(() => throw new Exception("Must not use stale catalog"), CancellationToken.None));
        Assert.Equal(extra ? 2 : 1, refreshes);
    }
    [Fact]
    public async Task Ordinary_incompatibility_does_not_refresh_and_cancellation_is_propagated()
    {
        int refreshes = 0;
        var session = new PackCatalogSession(_ => { refreshes++; return Task.CompletedTask; });
        await session.Prepare(CancellationToken.None);
        await Assert.ThrowsAsync<InvalidDataException>(() => session.Check(() => throw new InvalidDataException("user scope"), CancellationToken.None));
        Assert.Equal(1, refreshes);
        var cancelled = new PackCatalogSession(_ => throw new OperationCanceledException());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled.Prepare(CancellationToken.None));
    }
}
