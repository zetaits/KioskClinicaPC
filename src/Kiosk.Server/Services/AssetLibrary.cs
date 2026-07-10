namespace Kiosk.Server.Services;

/// <summary>
/// Biblioteca de imágenes que sirve el servidor (marcas y componentes). El panel sube/borra aquí; los
/// clientes las descargan por <c>/api/assets/{categoria}/{archivo}</c>. Categorías fijas y nombres de
/// archivo saneados: nada de subir fuera del directorio ni con rutas relativas.
/// </summary>
public sealed class AssetLibrary
{
    /// <summary>Carpetas que el cliente ya conoce (override de imágenes empaquetadas).</summary>
    public static readonly IReadOnlyList<string> Categories = new[] { "Brands", "SpecImages" };

    // SVG excluido a propósito: puede llevar <script> embebido (XSS almacenado al previsualizarlo inline en
    // el panel) y el cliente WPF ni lo renderiza. Solo mapas de bits.
    private static readonly HashSet<string> AllowedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".webp", ".gif" };

    private readonly string _root;

    public AssetLibrary(string assetsDir)
    {
        _root = Path.GetFullPath(assetsDir);
        Directory.CreateDirectory(_root);
        foreach (var c in Categories) Directory.CreateDirectory(Path.Combine(_root, c));
    }

    public static bool IsValidCategory(string category) => Categories.Contains(category);

    /// <summary>Nombres de archivo de una categoría (ordenados). Vacío si la categoría no es válida.</summary>
    public IReadOnlyList<string> List(string category)
    {
        if (!IsValidCategory(category)) return Array.Empty<string>();
        var dir = Path.Combine(_root, category);
        if (!Directory.Exists(dir)) return Array.Empty<string>();
        return Directory.GetFiles(dir).Select(Path.GetFileName).Where(n => n != null).Select(n => n!)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>URL pública relativa por la que el cliente descarga la imagen.</summary>
    public static string PublicUrl(string category, string fileName) => $"/api/assets/{category}/{fileName}";

    /// <summary>Guarda un archivo saneando el nombre y validando la extensión. Devuelve el nombre final.
    /// Lanza <see cref="ArgumentException"/> si la categoría/extensión no son válidas.</summary>
    public async Task<string> SaveAsync(string category, string originalName, Stream content, CancellationToken ct = default)
    {
        if (!IsValidCategory(category)) throw new ArgumentException("Categoría no válida.", nameof(category));

        string ext = Path.GetExtension(originalName);
        if (!AllowedExtensions.Contains(ext)) throw new ArgumentException($"Extensión no permitida: {ext}");

        // Solo el nombre (descarta cualquier ruta) y sin caracteres inválidos.
        string safe = Path.GetFileName(originalName);
        foreach (char c in Path.GetInvalidFileNameChars()) safe = safe.Replace(c, '_');
        if (string.IsNullOrWhiteSpace(safe)) safe = "imagen" + ext;

        string dest = EnsureInside(category, safe);
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        await using var fs = File.Create(dest);
        await content.CopyToAsync(fs, ct);
        return safe;
    }

    /// <summary>Resuelve la ruta física de un archivo si existe y es válido (para servirlo con auth de
    /// panel). Anti-traversal incluido.</summary>
    public bool TryGetFile(string category, string fileName, out string fullPath)
    {
        fullPath = "";
        if (!IsValidCategory(category)) return false;
        try { fullPath = EnsureInside(category, Path.GetFileName(fileName)); }
        catch (ArgumentException) { return false; }
        return File.Exists(fullPath);
    }

    /// <summary>Borra un archivo de una categoría. No-op si no existe.</summary>
    public void Delete(string category, string fileName)
    {
        if (!IsValidCategory(category)) return;
        string dest = EnsureInside(category, Path.GetFileName(fileName));
        if (File.Exists(dest)) File.Delete(dest);
    }

    /// <summary>Resuelve la ruta y verifica que cae DENTRO de la categoría (anti-traversal).</summary>
    private string EnsureInside(string category, string fileName)
    {
        string dir = Path.GetFullPath(Path.Combine(_root, category));
        string full = Path.GetFullPath(Path.Combine(dir, fileName));
        if (!full.StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Ruta no permitida.");
        return full;
    }
}
