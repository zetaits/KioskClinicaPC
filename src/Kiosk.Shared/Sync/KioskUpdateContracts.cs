using System.Security.Cryptography;
using System.Text.Json;

namespace KioskClinicaPC.Core.Sync;

public enum KioskReleaseState { Available, Active, Withdrawn }
public enum KioskUpdateOperation { Upgrade, Rollback }
public enum KioskUpdateState
{
    Assigned,
    Downloading,
    Staged,
    WaitingWindow,
    Installing,
    AwaitingRestart,
    Succeeded,
    Failed
}

/// <summary>Documento inmutable firmado por CI. La firma se calcula sobre los bytes UTF-8 exactos.</summary>
public sealed class KioskReleaseManifest
{
    public int SchemaVersion { get; set; } = 1;
    public string Version { get; set; } = "";
    public string FileName { get; set; } = "";
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = "";
    public DateTime PublishedAtUtc { get; set; }
    public string KeyId { get; set; } = "";
    public string MinimumUpdaterVersion { get; set; } = "1.2.0";
    public string? ReleaseNotes { get; set; }
    public string? GitHubFallbackUrl { get; set; }
}

public sealed class KioskUpdateAssignment
{
    public string JobId { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public string Version { get; set; } = "";
    public KioskUpdateOperation Operation { get; set; }
    public DateTime InstallAfterUtc { get; set; }
    public DateTime InstallBeforeUtc { get; set; }
    public string Token { get; set; } = "";
}

public sealed class KioskUpdateStatusUpdate
{
    public string DeviceId { get; set; } = "";
    public KioskUpdateState State { get; set; }
    public int? ProgressPercent { get; set; }
    public int? ExitCode { get; set; }
    public string? Message { get; set; }
}

/// <summary>Trabajo local protegido que consume el runner SYSTEM.</summary>
public sealed class KioskUpdateRunnerRequest
{
    public KioskUpdateAssignment Assignment { get; set; } = new();
    public KioskReleaseManifest Manifest { get; set; } = new();
    public string ManifestPath { get; set; } = "";
    public string SignaturePath { get; set; } = "";
    public string SetupPath { get; set; } = "";
    public string PublicKeyPath { get; set; } = "";
    public string ServerUrl { get; set; } = "";
    public string? ApiKey { get; set; }
}

public static class KioskReleaseSecurity
{
    public static KioskReleaseManifest ParseAndVerify(byte[] manifestBytes, string signatureBase64, string publicKeyPem)
    {
        byte[] signature;
        try { signature = Convert.FromBase64String(signatureBase64.Trim()); }
        catch (FormatException ex) { throw new InvalidDataException("La firma del manifiesto no es Base64 válido.", ex); }

        using var key = ECDsa.Create();
        try { key.ImportFromPem(publicKeyPem); }
        catch (Exception ex) when (ex is ArgumentException or CryptographicException)
        { throw new InvalidDataException("La clave pública de actualizaciones no es válida.", ex); }

        if (!key.VerifyData(manifestBytes, signature, HashAlgorithmName.SHA256))
            throw new CryptographicException("La firma del manifiesto de actualización no es válida.");

        var manifest = JsonSerializer.Deserialize<KioskReleaseManifest>(manifestBytes,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("El manifiesto de actualización está vacío.");
        ValidateManifest(manifest);
        return manifest;
    }

    public static void ValidateManifest(KioskReleaseManifest manifest)
    {
        if (manifest.SchemaVersion != 1) throw new InvalidDataException("Versión de manifiesto no compatible.");
        if (!TryParseVersion(manifest.Version, out _)) throw new InvalidDataException("Versión de Kiosk no válida.");
        if (!TryParseVersion(manifest.MinimumUpdaterVersion, out _)) throw new InvalidDataException("Versión mínima del actualizador no válida.");
        if (manifest.SizeBytes <= 0) throw new InvalidDataException("Tamaño de instalador no válido.");
        if (manifest.Sha256.Length != 64 || !manifest.Sha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("SHA-256 no válido.");
        if (string.IsNullOrWhiteSpace(manifest.KeyId) || manifest.KeyId.Length > 40 ||
            manifest.KeyId.Any(c => !char.IsLetterOrDigit(c) && c is not '-' and not '_'))
            throw new InvalidDataException("Identificador de clave no válido.");
        if (Path.GetFileName(manifest.FileName) != manifest.FileName ||
            !manifest.FileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Nombre de instalador no válido.");
        if (manifest.GitHubFallbackUrl is { Length: > 0 } fallback &&
            (!Uri.TryCreate(fallback, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps))
            throw new InvalidDataException("URL de respaldo no válida.");
    }

    public static bool TryParseVersion(string? value, out Version version)
    {
        string text = (value ?? "").Trim().TrimStart('v', 'V');
        bool ok = Version.TryParse(text, out Version? parsed) && parsed.Major >= 0 && parsed.Minor >= 0 && parsed.Build >= 0;
        version = ok ? new Version(parsed!.Major, parsed.Minor, parsed.Build) : new Version(0, 0, 0);
        return ok;
    }

    public static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)).ToLowerInvariant();
    }
}
