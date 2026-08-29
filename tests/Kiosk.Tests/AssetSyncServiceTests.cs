using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KioskClinicaPC.Core.Sync;
using KioskClinicaPC.Services;
using Xunit;

namespace KioskClinicaPC.Tests;

public sealed class AssetSyncServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "kiosk-client-asset-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Sync_downloads_verified_files_and_removes_only_remote_cache()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("imagen-verificada");
        var manifest = new AssetManifest
        {
            Version = "v1",
            Files =
            {
                new AssetManifestItem
                {
                    Category = "Brands",
                    FileName = "lenovo.png",
                    SizeBytes = bytes.Length,
                    Sha256 = Sha(bytes)
                }
            }
        };
        using var http = new HttpClient(new Handler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/manifest"))
                return Json(manifest);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        }));
        using var service = new AssetSyncService(http, "https://server.test", _root);

        Assert.True(await service.SyncAsync());
        string downloaded = Path.Combine(_root, "Brands", "lenovo.png");
        Assert.Equal(bytes, await File.ReadAllBytesAsync(downloaded));

        manifest.Files.Clear();
        manifest.Version = "v2";
        Assert.True(await service.SyncAsync());
        Assert.False(File.Exists(downloaded));
    }

    [Fact]
    public async Task Bad_hash_keeps_last_valid_file()
    {
        string category = Path.Combine(_root, "Brands");
        Directory.CreateDirectory(category);
        string destination = Path.Combine(category, "acer.png");
        byte[] previous = Encoding.UTF8.GetBytes("anterior");
        await File.WriteAllBytesAsync(destination, previous);
        byte[] corrupt = Encoding.UTF8.GetBytes("corrupta");
        var manifest = new AssetManifest
        {
            Version = "v2",
            Files =
            {
                new AssetManifestItem
                {
                    Category = "Brands",
                    FileName = "acer.png",
                    SizeBytes = corrupt.Length,
                    Sha256 = new string('0', 64)
                }
            }
        };
        using var http = new HttpClient(new Handler(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/manifest")
                ? Json(manifest)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(corrupt) }));
        using var service = new AssetSyncService(http, "https://server.test", _root);

        Assert.False(await service.SyncAsync());
        Assert.Equal(previous, await File.ReadAllBytesAsync(destination));
    }

    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json")
    };

    private static string Sha(byte[] value) =>
        Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(send(request));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }
}
