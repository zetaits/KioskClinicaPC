using Kiosk.Server.Services;
using SkiaSharp;
using Xunit;

namespace Kiosk.Server.Tests;

public sealed class AssetLibraryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "kiosk-asset-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Seed_is_once_and_preserves_later_deletions()
    {
        string seed = Path.Combine(_root, "seed");
        string store = Path.Combine(_root, "store");
        Directory.CreateDirectory(Path.Combine(seed, "Brands"));
        WriteImage(Path.Combine(seed, "Brands", "lenovo.png"), 3, 2, SKEncodedImageFormat.Png);

        var first = new AssetLibrary(store, seed);
        Assert.Contains("lenovo.png", first.List("Brands"));
        first.Delete("Brands", "lenovo.png");

        var restarted = new AssetLibrary(store, seed);
        Assert.DoesNotContain("lenovo.png", restarted.List("Brands"));
    }

    [Fact]
    public async Task Upload_normalizes_name_and_image_to_png()
    {
        var library = new AssetLibrary(Path.Combine(_root, "store"));
        await using var input = CreateImage(8, 5, SKEncodedImageFormat.Jpeg);
        input.Position = 0;

        var saved = await library.SaveAsync("Brands", "Hewlett-Packard", "foto.jpg", input, false);

        Assert.Equal("hewlettpackard.png", saved.FileName);
        Assert.Equal(8, saved.Width);
        Assert.Equal(5, saved.Height);
        Assert.True(library.TryGetFile("Brands", saved.FileName, out string path));
        Assert.Equal(".png", Path.GetExtension(path));
    }

    [Fact]
    public async Task Manifest_version_changes_after_upload_and_delete()
    {
        var library = new AssetLibrary(Path.Combine(_root, "store"));
        string initial = library.Version();
        await using var input = CreateImage(2, 2, SKEncodedImageFormat.Png);
        input.Position = 0;
        var saved = await library.SaveAsync("Brands", "Acer", "acer.png", input, false);
        string uploaded = library.Version();
        library.Delete("Brands", saved.FileName);

        Assert.NotEqual(initial, uploaded);
        Assert.Equal(initial, library.Version());
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private static MemoryStream CreateImage(int width, int height, SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.CornflowerBlue);
        using var image = SKImage.FromBitmap(bitmap);
        using SKData encoded = image.Encode(format, 90);
        return new MemoryStream(encoded.ToArray());
    }

    private static void WriteImage(string path, int width, int height, SKEncodedImageFormat format)
    {
        using MemoryStream image = CreateImage(width, height, format);
        using FileStream file = File.Create(path);
        image.CopyTo(file);
    }
}
