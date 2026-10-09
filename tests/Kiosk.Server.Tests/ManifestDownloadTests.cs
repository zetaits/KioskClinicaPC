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
        await ManifestPolicy.ValidateOnline("Vendor.App", "1", "x64", "msi", http,
            (wait, _) => { waits.Add(wait); return Task.CompletedTask; });
        Assert.Equal(3, handler.Calls);
        Assert.Equal(new[] { TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(24) }, waits);
    }
    [Fact]
    public async Task Exhausted_rate_limit_has_a_clear_reason_and_no_unbounded_retries()
    {
        using var handler = new Handler(_ => Limited()); using var http = new HttpClient(handler);
        var waits = new List<TimeSpan>();
        var error = await Assert.ThrowsAsync<IOException>(() => ManifestPolicy.ValidateOnline("Vendor.App", "1", "x64", "msi", http,
            (wait, _) => { waits.Add(wait); return Task.CompletedTask; }));
        Assert.Contains("HTTP 429", error.Message); Assert.Contains("más tarde", error.Message);
        Assert.Equal(3, handler.Calls);
        Assert.Equal(new[] { TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10) }, waits);
    }
    [Fact]
    public async Task Long_rate_limit_skips_the_app_without_retrying_before_the_server_allows()
    {
        using var handler = new Handler(_ => Limited(TimeSpan.FromMinutes(5))); using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<IOException>(() => ManifestPolicy.ValidateOnline("Vendor.App", "1", "x64", "msi", http,
            (_, _) => throw new Exception("Must not stall the pack")));
        Assert.Equal(1, handler.Calls);
    }
    [Fact]
    public async Task Cancellation_during_retry_is_propagated_without_more_requests()
    {
        using var handler = new Handler(_ => Limited()); using var http = new HttpClient(handler);
        using var cancel = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ManifestPolicy.ValidateOnline("Vendor.App", "1", "x64", "msi", http,
            (_, _) => { cancel.Cancel(); throw new OperationCanceledException(cancel.Token); }, cancel.Token));
        Assert.Equal(1, handler.Calls);
    }
}
