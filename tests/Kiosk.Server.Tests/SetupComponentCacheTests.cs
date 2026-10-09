using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using KioskClinicaPC.Equipment;
using Xunit;

namespace Kiosk.Server.Tests;
public sealed class SetupComponentCacheTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "setup-cache-" + Guid.NewGuid().ToString("N"));
    private readonly byte[] _bytes = [1, 2, 3, 4, 5, 6];
    private SetupComponent Component => new("kiosk", "1.2.0", _bytes.Length, Convert.ToHexString(SHA256.HashData(_bytes)).ToLowerInvariant());
    private string Cached => Path.Combine(_root, Component.Sha256);
    private string Target => Path.Combine(_root, "work", "kiosk.exe");
    public SetupComponentCacheTests() => Directory.CreateDirectory(Path.Combine(_root, "work"));
    private sealed class Handler(Func<HttpRequestMessage, int, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Attempts;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(respond(request, ++Attempts));
    }
    private HttpResponseMessage Good(bool range = false, int offset = 0)
    {
        var response = new HttpResponseMessage(range ? HttpStatusCode.PartialContent : HttpStatusCode.OK) { Content = new ByteArrayContent(_bytes[offset..]) };
        response.Headers.ETag = new EntityTagHeaderValue("\"" + Component.Sha256 + "\"");
        if (range) response.Content.Headers.ContentRange = new ContentRangeHeaderValue(offset, _bytes.Length - 1, _bytes.Length);
        return response;
    }
    private Task Copy(Handler handler, CancellationToken ct = default, EquipmentConfiguration? configuration = null) =>
        new SetupComponentCache(_root, new HttpClient(handler), configuration ?? new("https://panel.invalid/base", "test-key"), (_, _) => Task.CompletedTask)
            .CopyTo(Component, Target, _ => { }, ct);
    [Fact]
    public async Task Valid_cache_is_reverified_and_used_without_network()
    {
        File.WriteAllBytes(Cached, _bytes);
        var handler = new Handler((_, _) => throw new Exception("Network forbidden"));
        await Copy(handler); Assert.Equal(0, handler.Attempts); Assert.Equal(_bytes, File.ReadAllBytes(Target));
    }
    [Fact]
    public async Task Corrupt_cache_is_replaced_with_pinned_authenticated_component()
    {
        File.WriteAllBytes(Cached, [6, 5, 4, 3, 2, 1]);
        var handler = new Handler((request, _) =>
        {
            Assert.Equal($"/base/api/setup/v3/components/kiosk/{Component.Sha256}", request.RequestUri!.AbsolutePath);
            Assert.Equal("test-key", request.Headers.GetValues("X-Setup-Key").Single()); return Good();
        });
        await Copy(handler); Assert.Equal(_bytes, File.ReadAllBytes(Cached)); Assert.Equal(_bytes, File.ReadAllBytes(Target));
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Range_resumes_only_with_correct_etag_and_200_restarts(bool partialResponse)
    {
        File.WriteAllBytes(Cached + ".part", _bytes[..2]); File.WriteAllText(Cached + ".etag", "\"" + Component.Sha256 + "\"");
        var handler = new Handler((request, _) =>
        {
            Assert.Equal(2, request.Headers.Range!.Ranges.Single().From);
            Assert.Equal("\"" + Component.Sha256 + "\"", request.Headers.IfRange!.EntityTag!.ToString());
            return partialResponse ? Good(true, 2) : Good();
        });
        await Copy(handler); Assert.Equal(_bytes, File.ReadAllBytes(Target)); Assert.False(File.Exists(Cached + ".part"));
    }
    [Fact]
    public async Task Stale_etag_discards_partial_before_request()
    {
        File.WriteAllBytes(Cached + ".part", [9, 9]); File.WriteAllText(Cached + ".etag", "\"stale\"");
        await Copy(new Handler((request, _) => { Assert.Null(request.Headers.Range); return Good(); }));
        Assert.Equal(_bytes, File.ReadAllBytes(Target));
    }
    [Theory]
    [InlineData("hash")]
    [InlineData("size")]
    [InlineData("etag")]
    [InlineData("range")]
    public async Task Invalid_response_never_produces_executable(string kind)
    {
        if (kind == "range") { File.WriteAllBytes(Cached + ".part", _bytes[..2]); File.WriteAllText(Cached + ".etag", "\"" + Component.Sha256 + "\""); }
        var handler = new Handler((_, _) =>
        {
            var response = Good(kind == "range", kind == "range" ? 2 : 0);
            if (kind == "hash") response.Content = new ByteArrayContent([6, 5, 4, 3, 2, 1]);
            if (kind == "size") response.Content = new ByteArrayContent([1]);
            if (kind == "etag") response.Headers.ETag = new EntityTagHeaderValue("\"different\"");
            if (kind == "range") response.Content.Headers.ContentRange = new ContentRangeHeaderValue(1, 5, 6);
            return response;
        });
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => Copy(handler));
        string reason = kind switch { "hash" => "SHA-256", "size" => "Tamaño de descarga", "etag" => "ETag", _ => "Range" };
        Assert.Contains(reason, EquipmentDiagnostics.Describe(error));
        Assert.False(File.Exists(Target)); Assert.False(File.Exists(Cached)); Assert.False(File.Exists(Cached + ".part"));
    }
    [Theory]
    [InlineData(401, 1)] [InlineData(403, 1)] [InlineData(404, 1)] [InlineData(302, 1)]
    [InlineData(500, 3)] [InlineData(503, 3)] [InlineData(429, 3)]
    public async Task Definitive_errors_and_transient_retry_are_bounded(int status, int attempts)
    {
        var handler = new Handler((_, _) => new((HttpStatusCode)status));
        var error = await Assert.ThrowsAnyAsync<Exception>(() => Copy(handler));
        string log = EquipmentDiagnostics.Describe(error);
        Assert.Contains($"HTTP status {status}", log);
        Assert.DoesNotContain("test-key", log);
        if (attempts == 3) Assert.Contains("tres intentos", log);
        Assert.Equal(attempts, handler.Attempts); Assert.False(File.Exists(Target));
    }
    [Fact]
    public async Task Network_errors_retry_three_times_and_can_recover()
    {
        var handler = new Handler((_, attempt) => attempt < 3 ? throw new HttpRequestException() : Good());
        await Copy(handler); Assert.Equal(3, handler.Attempts);
    }
    [Fact]
    public async Task Cancel_does_not_retry_or_create_target()
    {
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        var handler = new Handler((_, _) => throw new Exception());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Copy(handler, cancel.Token)); Assert.Equal(0, handler.Attempts); Assert.False(File.Exists(Target));
    }
    [Theory]
    [InlineData("http://remote.invalid")]
    [InlineData("https://user:password@panel.invalid")]
    [InlineData("https://panel.invalid?key=leak")]
    [InlineData("https://panel.invalid#fragment")]
    public async Task Arbitrary_credential_destinations_are_rejected_before_network(string url)
    {
        var handler = new Handler((_, _) => throw new Exception());
        await Assert.ThrowsAsync<InvalidDataException>(() => Copy(handler, configuration: new(url, "test-key"))); Assert.Equal(0, handler.Attempts);
    }
    [Fact]
    public async Task Reparse_ancestor_is_rejected_before_a_hash_path_is_opened()
    {
        string target = Path.Combine(_root, "link-target"), link = Path.Combine(_root, "link"); Directory.CreateDirectory(target);
        if (OperatingSystem.IsWindows())
        {
            string Quote(string value) => "'" + value.Replace("'", "''") + "'";
            var start = new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"))
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-Command", $"$ErrorActionPreference='Stop'; New-Item -ItemType Junction -Path {Quote(link)} -Target {Quote(target)} | Out-Null" }) start.ArgumentList.Add(argument);
            using var process = System.Diagnostics.Process.Start(start)!;
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20)); Assert.Equal(0, process.ExitCode);
        }
        else Directory.CreateSymbolicLink(link, target);
        try { Assert.Throws<IOException>(() => SetupComponentCache.SafePath(Path.Combine(link, Component.Sha256))); }
        finally { Directory.Delete(link); }
    }
    [Fact]
    public async Task Quota_evicts_old_files_but_cannot_delete_active_files()
    {
        string huge = Path.Combine(_root, new string('c', 64));
        using (var file = File.Create(huge)) file.SetLength(SetupComponentCache.MaxBytes);
        using (var lease = File.Open(huge + ".lock", FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        {
            var handler = new Handler((_, _) => Good());
            await Assert.ThrowsAsync<IOException>(() => Copy(handler)); Assert.True(File.Exists(huge)); Assert.Equal(0, handler.Attempts);
        }
        await Copy(new Handler((_, _) => Good())); Assert.False(File.Exists(huge));
    }
    private sealed class InterruptedStream(byte[] bytes) : MemoryStream(bytes)
    {
        private bool _read;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (_read) throw new IOException("Disconnected");
            _read = true; return base.ReadAsync(buffer[..2], ct);
        }
    }
    [Fact]
    public async Task Interrupted_transfer_resumes_verified_partial_on_next_attempt()
    {
        var handler = new Handler((request, attempt) =>
        {
            if (attempt == 1)
            {
                var response = Good(); response.Content = new StreamContent(new InterruptedStream(_bytes)); return response;
            }
            Assert.Equal(2, request.Headers.Range!.Ranges.Single().From); return Good(true, 2);
        });
        await Copy(handler); Assert.Equal(2, handler.Attempts); Assert.Equal(_bytes, File.ReadAllBytes(Target));
    }
    private sealed class StalledStream : MemoryStream
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        { await Task.Delay(Timeout.Infinite, ct); return 0; }
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Idle_and_overall_time_limits_stop_stalled_components(bool overall)
    {
        var handler = new Handler((_, _) => { var response = Good(); response.Content = new StreamContent(new StalledStream()); response.Content.Headers.ContentLength = _bytes.Length; return response; });
        var cache = new SetupComponentCache(_root, new HttpClient(handler), new("http://127.0.0.1", "test-key"), (_, _) => Task.CompletedTask,
            idleTimeout: overall ? TimeSpan.FromSeconds(5) : TimeSpan.FromMilliseconds(100), componentTimeout: overall ? TimeSpan.FromMilliseconds(100) : TimeSpan.FromSeconds(5));
        if (overall) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cache.CopyTo(Component, Target, _ => { }, default));
        else await Assert.ThrowsAsync<IOException>(() => cache.CopyTo(Component, Target, _ => { }, default));
        Assert.Equal(overall ? 1 : 3, handler.Attempts); Assert.False(File.Exists(Target));
    }
    [Fact]
    public async Task Cleanup_removes_old_partial_and_keeps_files_in_use()
    {
        string old = Path.Combine(_root, new string('a', 64) + ".part"); File.WriteAllBytes(old, [1]); File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-8));
        string active = Path.Combine(_root, new string('b', 64) + ".part"); File.WriteAllBytes(active, [2]); File.SetLastWriteTimeUtc(active, DateTime.UtcNow.AddDays(-8));
        using var lease = File.Open(Path.Combine(_root, new string('b', 64) + ".lock"), FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        await Copy(new Handler((_, _) => Good())); Assert.False(File.Exists(old)); Assert.True(File.Exists(active));
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
