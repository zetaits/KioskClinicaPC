using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using Kiosk.SetupHelper;
using KioskClinicaPC.Core.Sync;
using Xunit;

namespace Kiosk.Server.Tests;

public sealed class CatalogExportReaderTests
{
    private static WingetIndexEntry Entry(int index, bool eligible = true) =>
        new($"Test.Package{index}", $"Package {index}", "Publisher", "1.0", eligible);

    [Fact]
    public async Task DefaultVersionHttp400IsNotRetriedAndDoesNotStopFollowingPackages()
    {
        int failedCalls = 0;
        var log = new List<string>();
        var entries = await CatalogExportReader.ReadAsync(2000, index =>
        {
            if (index == 232) { failedCalls++; throw new COMException("", unchecked((int)0x80190190)); }
            return Entry(index);
        }, log.Add, _ => throw new InvalidOperationException("HTTP 400 must not be retried"));
        Assert.Equal(1, failedCalls);
        Assert.Equal(1999, entries.Count);
        Assert.Contains(entries, x => x.Id == "Test.Package1999");
        Assert.DoesNotContain(entries, x => x.Id == "Test.Package232");
        Assert.Contains(log, x => x.Contains("Excluded package 233/2000") && x.Contains("0x80190190"));
    }

    [Theory]
    [InlineData(0x801901F7)] // HTTP 503
    [InlineData(0x80190198)] // HTTP 408
    [InlineData(0x801901AD)] // HTTP 429
    [InlineData(0x80072EE2)] // WinINet timeout
    public async Task TransientFailureRetriesThenKeepsRecoveredPackage(uint hresult)
    {
        int calls = 0;
        var delays = new List<TimeSpan>();
        var entries = await CatalogExportReader.ReadAsync(2000, index =>
        {
            if (index == 10 && ++calls < 3) throw new COMException("", unchecked((int)hresult));
            return Entry(index);
        }, delay: duration => { delays.Add(duration); return Task.CompletedTask; });
        Assert.Equal(3, calls);
        Assert.Equal(new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2) }, delays);
        Assert.Equal(2000, entries.Count);
    }

    [Fact]
    public async Task ExhaustedTransientFailureIsExcludedAfterThreeAttempts()
    {
        int calls = 0;
        var entries = await CatalogExportReader.ReadAsync(2000, index =>
        {
            if (index == 1) { calls++; throw new HttpRequestException("Unavailable", null, HttpStatusCode.ServiceUnavailable); }
            return Entry(index);
        }, delay: _ => Task.CompletedTask);
        Assert.Equal(3, calls);
        Assert.Equal(1999, entries.Count);
    }

    [Fact]
    public async Task PublisherMetadataFailureIsAlsoIsolated()
    {
        var entries = await CatalogExportReader.ReadAsync(2000, index =>
        {
            var entry = Entry(index);
            if (index == 20) throw new HttpRequestException("Publisher lookup failed", null, HttpStatusCode.NotFound);
            return entry;
        });
        Assert.Equal(1999, entries.Count);
        Assert.Contains(entries, x => x.Id == "Test.Package21");
    }

    [Fact]
    public async Task GeneralizedFailuresAbortBeforeReturningAnIndex()
    {
        int calls = 0;
        var failure = await Assert.ThrowsAsync<InvalidDataException>(() => CatalogExportReader.ReadAsync(2000, index =>
        {
            calls++;
            throw new COMException("", unchecked((int)0x80190190));
        }));
        Assert.Equal(21, calls); // 1% of 2000 = 20 allowed exclusions
        Assert.Contains("degraded", failure.Message);
        Assert.Contains("Keeping the previous index", failure.Message);
    }

    [Theory]
    [InlineData(0x80190191)] // HTTP 401
    [InlineData(0x80190193)] // HTTP 403
    [InlineData(0x80004002)] // COM interface mismatch
    public async Task GlobalFailuresAbortImmediately(uint hresult)
    {
        int calls = 0;
        var failure = new COMException("Global failure", unchecked((int)hresult));
        var actual = await Assert.ThrowsAsync<COMException>(() => CatalogExportReader.ReadAsync(2000, index =>
        {
            calls++;
            throw failure;
        }));
        Assert.Same(failure, actual);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task CancellationIsNeverSwallowed()
    {
        await Assert.ThrowsAsync<OperationCanceledException>(() => CatalogExportReader.ReadAsync(2000,
            _ => throw new OperationCanceledException()));
    }

    [Fact]
    public async Task LegitimatelyIneligibleApplicationsDoNotConsumeFailureBudget()
    {
        var entries = await CatalogExportReader.ReadAsync(2000, index => Entry(index, index < 100));
        Assert.Equal(2000, entries.Count);
        Assert.Equal(100, entries.Count(x => x.Eligible));
    }

    [Fact]
    public async Task InsufficientEligiblePackagesAndDuplicateIdsStillFailCompletenessChecks()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => CatalogExportReader.ReadAsync(2000, index => Entry(index, index < 99)));
        await Assert.ThrowsAsync<InvalidDataException>(() => CatalogExportReader.ReadAsync(2000, _ => Entry(1)));
    }

    [Fact]
    public void FailureBudgetHasAnAbsoluteCap()
    {
        Assert.Equal(20, CatalogExportReader.FailureLimit(2000));
        Assert.Equal(100, CatalogExportReader.FailureLimit(15314));
        Assert.Equal(100, CatalogExportReader.FailureLimit(100000));
    }
}
