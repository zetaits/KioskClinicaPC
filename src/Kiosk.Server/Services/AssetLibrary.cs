using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using KioskClinicaPC.Core.Sync;
using SkiaSharp;

namespace Kiosk.Server.Services;

/// <summary>
/// Biblioteca persistente de imágenes compartidas. Valida y normaliza las subidas a PNG, publica un
/// manifiesto verificable y siembra una sola vez los assets incluidos con la aplicación.
/// </summary>
public sealed class AssetLibrary
{
    public static readonly IReadOnlyList<string> Categories = new[] { "Brands", "SpecImages", "ThemeAssets" };
    private static readonly HashSet<string> AllowedInputExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp" };
    private const long MaxInputBytes = 8 * 1024 * 1024;
    private const long MaxPixels = 40_000_000;
    // v2 añade ThemeAssets; un marcador nuevo vuelve a sembrar solo los ficheros ausentes.
    private const string SeedMarker = ".default-assets-v2";

    private readonly string _root;
    private readonly object _gate = new();
    private string? _cachedVersion;

    public AssetLibrary(string assetsDir, string? seedDir = null)
    {
        _root = Path.GetFullPath(assetsDir);
        Directory.CreateDirectory(_root);
        foreach (string category in Categories) Directory.CreateDirectory(Path.Combine(_root, category));
        SeedDefaultsOnce(seedDir);
    }

    public static bool IsValidCategory(string category) =>
        Categories.Contains(category, StringComparer.Ordinal);

    public IReadOnlyList<string> List(string category) =>
        ListDetails(category).Select(x => x.FileName).ToList();

    public IReadOnlyList<AssetManifestItem> ListDetails(string category)
    {
        if (!IsValidCategory(category)) return Array.Empty<AssetManifestItem>();
        string dir = Path.Combine(_root, category);
        if (!Directory.Exists(dir)) return Array.Empty<AssetManifestItem>();

        return Directory.EnumerateFiles(dir)
            .Where(IsSupportedStoredImage)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .Select(file => Describe(category, file))
            .ToList();
    }

    public AssetManifest Manifest()
    {
        var files = Categories.SelectMany(ListDetails).ToList();
        return new AssetManifest { Version = Version(files), Files = files };
    }

    public string Version()
    {
        lock (_gate)
        {
            if (_cachedVersion != null) return _cachedVersion;
        }
        return Version(Categories.SelectMany(ListDetails).ToList());
    }

    public static string PublicUrl(string category, string fileName) =>
        $"/api/assets/{Uri.EscapeDataString(category)}/{Uri.EscapeDataString(fileName)}";

    public async Task<string> SaveAsync(
        string category, string originalName, Stream content, CancellationToken ct = default)
    {
        AssetManifestItem item = await SaveAsync(
            category, Path.GetFileNameWithoutExtension(originalName), originalName, content, overwrite: true, ct);
        return item.FileName;
    }

    public async Task<AssetManifestItem> SaveAsync(
        string category,
        string logicalName,
        string originalName,
        Stream content,
        bool overwrite,
        CancellationToken ct = default)
    {
        if (!IsValidCategory(category)) throw new ArgumentException("Categoría no válida.", nameof(category));
        string ext = Path.GetExtension(originalName);
        if (!AllowedInputExtensions.Contains(ext))
            throw new ArgumentException("Formato no permitido. Usa PNG, JPG, WEBP, GIF o BMP.");

        string key = NormalizeKey(logicalName);
        if (key.Length < 2) throw new ArgumentException("Indica un nombre o identificador de al menos 2 caracteres.");
        string fileName = key + ".png";
        string destination = EnsureInside(category, fileName);
        if (File.Exists(destination) && !overwrite)
            throw new ArgumentException($"Ya existe «{fileName}». Marca reemplazar para actualizarla.");

        using var buffer = new MemoryStream();
        byte[] chunk = new byte[81920];
        int read;
        while ((read = await content.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > MaxInputBytes)
                throw new InvalidDataException("La imagen supera el máximo de 8 MB.");
            await buffer.WriteAsync(chunk.AsMemory(0, read), ct);
        }

        using var data = SKData.CreateCopy(buffer.ToArray());
        using var codec = SKCodec.Create(data)
            ?? throw new InvalidDataException("El archivo no contiene una imagen válida.");
        SKImageInfo info = codec.Info;
        if (info.Width <= 0 || info.Height <= 0 || (long)info.Width * info.Height > MaxPixels)
            throw new InvalidDataException("La imagen tiene demasiados píxeles (máximo 40 megapíxeles).");
        using SKBitmap bitmap = SKBitmap.Decode(data)
            ?? throw new InvalidDataException("No se pudo decodificar la imagen.");
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData png = image.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidDataException("No se pudo normalizar la imagen.");

        string temp = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                png.SaveTo(output);
                await output.FlushAsync(ct);
            }
            File.Move(temp, destination, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }

        Invalidate();
        return Describe(category, destination);
    }

    public bool TryGetFile(string category, string fileName, out string fullPath)
    {
        fullPath = "";
        if (!IsValidCategory(category)) return false;
        try { fullPath = EnsureInside(category, Path.GetFileName(fileName)); }
        catch (ArgumentException) { return false; }
        return File.Exists(fullPath) && IsSupportedStoredImage(fullPath);
    }

    public void Delete(string category, string fileName)
    {
        if (!IsValidCategory(category)) return;
        string destination = EnsureInside(category, Path.GetFileName(fileName));
        if (!File.Exists(destination)) return;
        File.Delete(destination);
        Invalidate();
    }

    private void SeedDefaultsOnce(string? seedDir)
    {
        string marker = Path.Combine(_root, SeedMarker);
        if (File.Exists(marker) || string.IsNullOrWhiteSpace(seedDir) || !Directory.Exists(seedDir)) return;

        foreach (string category in Categories)
        {
            string source = Path.Combine(seedDir, category);
            if (!Directory.Exists(source)) continue;
            foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)
                         .Where(IsSupportedStoredImage))
            {
                string destination = EnsureInside(category, Path.GetFileName(file));
                if (!File.Exists(destination)) File.Copy(file, destination);
            }
        }
        File.WriteAllText(marker, DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        Invalidate();
    }

    private static bool IsSupportedStoredImage(string path) =>
        AllowedInputExtensions.Contains(Path.GetExtension(path));

    private static string NormalizeKey(string value)
    {
        var result = new StringBuilder();
        foreach (char c in (value ?? "").Trim().Normalize(NormalizationForm.FormD))
        {
            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(c)) result.Append(char.ToLowerInvariant(c));
        }
        return result.ToString();
    }

    private AssetManifestItem Describe(string category, string file)
    {
        var info = new FileInfo(file);
        int width = 0;
        int height = 0;
        try
        {
            using var data = SKData.Create(file);
            using var codec = SKCodec.Create(data);
            if (codec != null)
            {
                width = codec.Info.Width;
                height = codec.Info.Height;
            }
        }
        catch { }

        using var stream = File.OpenRead(file);
        string hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        return new AssetManifestItem
        {
            Category = category,
            FileName = info.Name,
            SizeBytes = info.Length,
            Sha256 = hash,
            Width = width,
            Height = height
        };
    }

    private string Version(IReadOnlyCollection<AssetManifestItem> files)
    {
        lock (_gate)
        {
            if (_cachedVersion != null) return _cachedVersion;
            string value = string.Join("|", files.Select(x =>
                $"{x.Category}/{x.FileName}:{x.SizeBytes}:{x.Sha256}"));
            _cachedVersion = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
            return _cachedVersion;
        }
    }

    private void Invalidate()
    {
        lock (_gate) _cachedVersion = null;
    }

    private string EnsureInside(string category, string fileName)
    {
        string dir = Path.GetFullPath(Path.Combine(_root, category));
        string full = Path.GetFullPath(Path.Combine(dir, fileName));
        if (!full.StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Ruta no permitida.");
        return full;
    }
}
