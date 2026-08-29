namespace KioskClinicaPC.Core.Sync;

/// <summary>Inventario versionado de imágenes compartidas por el servidor.</summary>
public sealed class AssetManifest
{
    public string Version { get; set; } = "";
    public List<AssetManifestItem> Files { get; set; } = new();
}

public sealed class AssetManifestItem
{
    public string Category { get; set; } = "";
    public string FileName { get; set; } = "";
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
}
