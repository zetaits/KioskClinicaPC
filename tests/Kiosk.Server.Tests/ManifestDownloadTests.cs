using System.Net;
using System.Net.Http.Headers;
using Kiosk.SetupHelper;
using Xunit;

namespace Kiosk.Server.Tests;

public sealed class ManifestDownloadTests
{
    private const string Yaml = "InstallerType: msi\nScope: machine\nInstallers:\n- Architecture: x64\n";
    private sealed class Handler(Func<int, HttpResponseMessage> response) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(response(++Calls));
    }
    private static HttpResponseMessage Limited(TimeSpan? retryAfter = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        if (retryAfter is { } wait) response.Headers.RetryAfter = new RetryConditionHeaderValue(wait);
        return response;
    }
    [Fact]
    public async Task Rate_limit_respects_retry_after_and_then_accepts_the_official_manifest()
    {
        using var handler = new Handler(call => call < 3 ? Limited(TimeSpan.FromSeconds(call * 12))
            : new(HttpStatusCode.OK) { Content = new StringContent(Yaml) });
        using var http = new HttpClient(handler); var waits = new List<TimeSpan>();
        var client = new ManifestClient(http, (wait, _) => { waits.Add(wait); return Task.CompletedTask; });
        await client.Validate("Vendor.App", "1", "x64", "msi");
        Assert.Equal(3, handler.Calls);
        Assert.Equal(new[] { TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(24) }, waits);
    }
    [Fact]
    public async Task Exhausted_rate_limit_has_a_clear_reason_and_no_unbounded_retries()
    {
        using var handler = new Handler(_ => Limited()); using var http = new HttpClient(handler);
        var waits = new List<TimeSpan>();
        var client = new ManifestClient(http, (wait, _) => { waits.Add(wait); return Task.CompletedTask; });
        var error = await Assert.ThrowsAsync<IOException>(() => client.Validate("Vendor.App", "1", "x64", "msi"));
        Assert.Contains("HTTP 429", error.Message); Assert.Contains("más tarde", error.Message);
        Assert.Equal(3, handler.Calls);
        Assert.Equal(new[] { TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10) }, waits);
    }
    [Fact]
    public async Task Long_rate_limit_skips_the_app_without_retrying_before_the_server_allows()
    {
        using var handler = new Handler(_ => Limited(TimeSpan.FromMinutes(5))); using var http = new HttpClient(handler);
        var client = new ManifestClient(http, (_, _) => throw new Exception("Must not stall the pack"));
        await Assert.ThrowsAsync<IOException>(() => client.Validate("Vendor.App", "1", "x64", "msi"));
        Assert.Equal(1, handler.Calls);
    }
    [Fact]
    public async Task Cancellation_during_retry_is_propagated_without_more_requests()
    {
        using var handler = new Handler(_ => Limited()); using var http = new HttpClient(handler);
        using var cancel = new CancellationTokenSource();
        var client = new ManifestClient(http, (_, _) => { cancel.Cancel(); throw new OperationCanceledException(cancel.Token); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.Validate("Vendor.App", "1", "x64", "msi", cancel.Token));
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(408)] [InlineData(429)] [InlineData(503)]
    public async Task Shared_outage_exhausts_retries_only_once_for_the_whole_execution(int status)
    {
        using var handler = new Handler(_ => new((HttpStatusCode)status));
        using var http = new HttpClient(handler);
        var client = new ManifestClient(http, (_, _) => Task.CompletedTask);
        for (int app = 0; app < 10; app++)
            await Assert.ThrowsAsync<IOException>(() => client.Validate("Vendor.App" + app, "1", "x64", "msi"));
        Assert.Equal(3, handler.Calls);
        // A new execution must try again, even if it shares the HTTP connection pool.
        var next = new ManifestClient(http, (_, _) => Task.CompletedTask);
        await Assert.ThrowsAsync<IOException>(() => next.Validate("Vendor.App", "1", "x64", "msi"));
        Assert.Equal(6, handler.Calls);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Transport_failures_and_http_timeouts_are_shared_but_caller_cancellation_is_not(bool timeout)
    {
        using var handler = new Handler(_ => throw (timeout ? new TaskCanceledException() : new HttpRequestException()));
        using var http = new HttpClient(handler);
        var client = new ManifestClient(http, (_, _) => Task.CompletedTask);
        await Assert.ThrowsAsync<IOException>(() => client.Validate("Vendor.App", "1", "x64", "msi"));
        await Assert.ThrowsAsync<IOException>(() => client.Validate("Other.App", "1", "x64", "msi"));
        Assert.Equal(3, handler.Calls);
    }

    [Theory]
    [InlineData(404)] [InlineData(403)] [InlineData(200)]
    public async Task Application_specific_failure_does_not_block_other_apps(int status)
    {
        int failures = status == 404 ? 2 : 1;
        using var handler = new Handler(call => call <= failures
            ? new((HttpStatusCode)status) { Content = new StringContent("InstallerType: msix\nScope: user\n") }
            : new(HttpStatusCode.OK) { Content = new StringContent(Yaml) });
        using var http = new HttpClient(handler);
        var client = new ManifestClient(http);
        await Assert.ThrowsAnyAsync<Exception>(() => client.Validate("Vendor.App", "1", "x64", "msi"));
        await client.Validate("Other.App", "1", "x64", "msi");
        Assert.Equal(failures + 1, handler.Calls);
    }

    [Fact]
    public async Task Long_retry_after_opens_the_source_immediately()
    {
        using var handler = new Handler(_ => Limited(TimeSpan.FromMinutes(5)));
        using var http = new HttpClient(handler);
        var client = new ManifestClient(http, (_, _) => throw new Exception("Must not wait"));
        await Assert.ThrowsAsync<IOException>(() => client.Validate("Vendor.App", "1", "x64", "msi"));
        await Assert.ThrowsAsync<IOException>(() => client.Validate("Other.App", "1", "x64", "msi"));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Retry_after_date_uses_the_clock_and_external_cancellation_leaves_the_source_available()
    {
        using var cancel = new CancellationTokenSource();
        var now = new DateTimeOffset(2026, 10, 12, 10, 0, 0, TimeSpan.Zero);
        using var handler = new Handler(call =>
        {
            if (call > 1) return new(HttpStatusCode.OK) { Content = new StringContent(Yaml) };
            var response = Limited(); response.Headers.RetryAfter = new RetryConditionHeaderValue(now.AddSeconds(20)); return response;
        });
        using var http = new HttpClient(handler);
        var client = new ManifestClient(http, (wait, ct) =>
        {
            Assert.Equal(TimeSpan.FromSeconds(20), wait);
            cancel.Cancel(); ct.ThrowIfCancellationRequested(); return Task.CompletedTask;
        }, new Clock(now));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.Validate("Vendor.App", "1", "x64", "msi", cancel.Token));
        await client.Validate("Other.App", "1", "x64", "msi");
        Assert.Equal(2, handler.Calls);
    }
    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
