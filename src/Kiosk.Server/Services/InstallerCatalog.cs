using System.Security.Cryptography;
using KioskClinicaPC.Core.Sync;
using Newtonsoft.Json;

namespace Kiosk.Server.Services;

public sealed class InstallerPackage
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string OriginalFileName { get; set; } = "";
    public string StoredFileName { get; set; } = "";
    public InstallerPackageKind Kind { get; set; }
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = "";
    public bool HasEmbeddedSignature { get; set; }
    public bool AllowUnsigned { get; set; }
    public DateTime UploadedAtUtc { get; set; }
    public DateTime? ArchivedAtUtc { get; set; }
}

/// <summary>Catálogo administrable de instaladores. Los binarios son inmutables y nunca son assets públicos.</summary>
public sealed class InstallerCatalog
{
    private readonly object _gate = new();
    private readonly string _root;
    private readonly string _temp;
    private readonly string _metadataPath;
    private readonly long _maxBytes;
    private readonly List<InstallerPackage> _packages = new();
    private readonly HashSet<string> _pendingNames = new(StringComparer.OrdinalIgnoreCase);

    public InstallerCatalog(string dataDir, string installersDir, long maxBytes)
    {
        _root = Path.GetFullPath(installersDir);
        _temp = Path.Combine(_root, ".uploading");
        _metadataPath = Path.Combine(dataDir, "installers.json");
        _maxBytes = maxBytes;
        Directory.CreateDirectory(dataDir);
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_temp);
        Load();
        CleanupTemporaryFiles();
    }

    public long MaxBytes => _maxBytes;
    public IReadOnlyList<InstallerPackage> List()
    {
        lock (_gate) return _packages.Where(p => p.ArchivedAtUtc == null).OrderBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public InstallerPackage? Find(string id)
    {
        lock (_gate) return _packages.FirstOrDefault(p => p.ArchivedAtUtc == null && p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<InstallerPackage> AddAsync(string displayName, string originalName, Stream content,
        bool allowUnsigned, CancellationToken ct = default)
    {
        displayName = displayName.Trim();
        if (displayName.Length is < 1 or > 100) throw new ArgumentException("El nombre debe tener entre 1 y 100 caracteres.");
        string ext = Path.GetExtension(originalName).ToLowerInvariant();
        if (ext is not ".msi" and not ".exe") throw new ArgumentException("Solo se admiten instaladores MSI o EXE.");
        lock (_gate)
        {
            if (_packages.Any(p => p.ArchivedAtUtc == null && p.DisplayName.Equals(displayName, StringComparison.OrdinalIgnoreCase)) || !_pendingNames.Add(displayName))
                throw new ArgumentException("Ya existe una aplicación con ese nombre.");
        }
        string id = Guid.NewGuid().ToString("N");
        string tmp = Path.Combine(_temp, id + ".tmp");
        long size = 0;
        string hash;
        try
        {
            using var sha = SHA256.Create();
            await using (var output = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                byte[] buffer = new byte[81920];
                int read;
                while ((read = await content.ReadAsync(buffer, ct)) > 0)
                {
                    size += read;
                    if (size > _maxBytes) throw new ArgumentException($"El archivo supera el límite de {_maxBytes / 1024 / 1024} MB.");
                    sha.TransformBlock(buffer, 0, read, null, 0);
                    await output.WriteAsync(buffer.AsMemory(0, read), ct);
                }
                sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                hash = Convert.ToHexString(sha.Hash!).ToLowerInvariant();
            }

            InstallerPackageKind kind = DetectKind(tmp, ext);
            bool hasSignature = HasPeCertificate(tmp, ext);
            if (!hasSignature && !allowUnsigned)
                throw new UnsignedInstallerException("El instalador no contiene una firma digital. Confirma expresamente que deseas admitirlo.");

            string stored = id + ext;
            File.Move(tmp, Path.Combine(_root, stored));
            var package = new InstallerPackage
            {
                Id = id, DisplayName = displayName, OriginalFileName = Path.GetFileName(originalName),
                StoredFileName = stored, Kind = kind, SizeBytes = size, Sha256 = hash,
                HasEmbeddedSignature = hasSignature, AllowUnsigned = !hasSignature && allowUnsigned,
                UploadedAtUtc = DateTime.UtcNow
            };
            lock (_gate) { _packages.Add(package); Save(); }
            return package;
        }
        finally
        {
            lock (_gate) _pendingNames.Remove(displayName);
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
        }
    }

    public void Rename(string id, string displayName)
    {
        displayName = displayName.Trim();
        if (displayName.Length is < 1 or > 100) throw new ArgumentException("Nombre no válido.");
        lock (_gate)
        {
            if (_packages.Any(p => p.ArchivedAtUtc == null && p.Id != id && p.DisplayName.Equals(displayName, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("Ya existe una aplicación con ese nombre.");
            var p = _packages.FirstOrDefault(x => x.Id == id) ?? throw new KeyNotFoundException();
            p.DisplayName = displayName;
            Save();
        }
    }

    public void Archive(string id)
    {
        InstallerPackage? package;
        string? staged = null;
        lock (_gate)
        {
            package = _packages.FirstOrDefault(p => p.Id == id && p.ArchivedAtUtc == null);
            if (package == null) return;
        }
        string path = ResolveFile(package);
        if (File.Exists(path))
        {
            staged = Path.Combine(_temp, package.Id + ".archived");
            File.Move(path, staged, overwrite: true);
        }
        try
        {
            lock (_gate)
            {
                package.ArchivedAtUtc = DateTime.UtcNow;
                Save();
            }
        }
        catch
        {
            if (staged != null && File.Exists(staged)) File.Move(staged, path, overwrite: true);
            throw;
        }
        try { if (staged != null && File.Exists(staged)) File.Delete(staged); } catch { }
    }

    public string ResolveFile(InstallerPackage package)
    {
        string full = Path.GetFullPath(Path.Combine(_root, Path.GetFileName(package.StoredFileName)));
        if (!full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Ruta de instalador no válida.");
        return full;
    }

    private static InstallerPackageKind DetectKind(string path, string ext)
    {
        byte[] head = new byte[(int)Math.Min(new FileInfo(path).Length, 4 * 1024 * 1024)];
        using (var fs = File.OpenRead(path)) fs.ReadExactly(head);
        if (ext == ".msi")
        {
            byte[] ole = { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 };
            if (!head.AsSpan().StartsWith(ole)) throw new ArgumentException("El archivo no es un MSI válido.");
            return InstallerPackageKind.Msi;
        }
        if (head.Length < 2 || head[0] != 'M' || head[1] != 'Z') throw new ArgumentException("El archivo no es un ejecutable PE válido.");
        if (ContainsAnyAscii(path, "Inno Setup Setup Data", "Inno Setup")) return InstallerPackageKind.InnoSetup;
        if (ContainsAnyAscii(path, "Nullsoft", "NSIS")) return InstallerPackageKind.Nsis;
        throw new ArgumentException("No se reconoce un modo silencioso fiable para este EXE. Usa un MSI, Inno Setup o NSIS.");
    }

    private static bool ContainsAnyAscii(string path, params string[] needles)
    {
        byte[][] patterns = needles.Select(System.Text.Encoding.ASCII.GetBytes).ToArray();
        int overlap = patterns.Max(p => p.Length) - 1;
        byte[] buffer = new byte[1024 * 1024 + overlap];
        int kept = 0;
        using var stream = File.OpenRead(path);
        while (true)
        {
            int read = stream.Read(buffer, kept, buffer.Length - kept);
            if (read == 0) return false;
            int count = kept + read;
            var span = buffer.AsSpan(0, count);
            foreach (byte[] pattern in patterns)
                if (span.IndexOf(pattern) >= 0) return true;
            kept = Math.Min(overlap, count);
            span[^kept..].CopyTo(buffer);
        }
    }

    private static bool HasPeCertificate(string path, string ext)
    {
        if (ext != ".exe") return false; // Los MSI se validarán criptográficamente en el agente Windows.
        using var fs = File.OpenRead(path);
        using var br = new BinaryReader(fs);
        if (br.ReadUInt16() != 0x5A4D) return false;
        fs.Position = 0x3c;
        int pe = br.ReadInt32();
        if (pe < 0 || pe > fs.Length - 256) return false;
        fs.Position = pe;
        if (br.ReadUInt32() != 0x4550) return false;
        fs.Position += 20;
        ushort magic = br.ReadUInt16();
        long dataDirectories = fs.Position + (magic == 0x20b ? 110 : 94);
        fs.Position = dataDirectories + 8 * 4; // IMAGE_DIRECTORY_ENTRY_SECURITY
        return br.ReadUInt32() != 0 && br.ReadUInt32() != 0;
    }

    private void CleanupTemporaryFiles()
    {
        foreach (string file in Directory.GetFiles(_temp))
            if (DateTime.UtcNow - File.GetLastWriteTimeUtc(file) > TimeSpan.FromHours(24))
                try { File.Delete(file); } catch { }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_metadataPath)) return;
            var items = JsonConvert.DeserializeObject<List<InstallerPackage>>(File.ReadAllText(_metadataPath));
            if (items != null) _packages.AddRange(items);
        }
        catch { }
    }

    private void Save()
    {
        string tmp = _metadataPath + ".tmp";
        File.WriteAllText(tmp, JsonConvert.SerializeObject(_packages, Formatting.Indented));
        File.Move(tmp, _metadataPath, true);
    }
}

public sealed class UnsignedInstallerException(string message) : ArgumentException(message);
