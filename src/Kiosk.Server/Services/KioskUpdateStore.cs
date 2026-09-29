using System.Security.Cryptography;
using System.Text;
using KioskClinicaPC.Core.Sync;
using Newtonsoft.Json;

namespace Kiosk.Server.Services;

public sealed class KioskReleaseRecord
{
    public string Version { get; set; } = "";
    public string FileName { get; set; } = "";
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = "";
    public string KeyId { get; set; } = "";
    public DateTime PublishedAtUtc { get; set; }
    public string? ReleaseNotes { get; set; }
    public string? GitHubFallbackUrl { get; set; }
    public string ManifestBase64 { get; set; } = "";
    public string SignatureBase64 { get; set; } = "";
    public string StoredFileName { get; set; } = "";
    public KioskReleaseState State { get; set; }
}

public sealed class KioskUpdateJob
{
    public string Id { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public string FromVersion { get; set; } = "";
    public string TargetVersion { get; set; } = "";
    public KioskUpdateOperation Operation { get; set; }
    public KioskUpdateState State { get; set; }
    public int? ProgressPercent { get; set; }
    public int? ExitCode { get; set; }
    public string? Message { get; set; }
    public DateTime InstallAfterUtc { get; set; }
    public DateTime InstallBeforeUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public bool IsTerminal => State is KioskUpdateState.Succeeded or KioskUpdateState.Failed;
}

public sealed class KioskUpdateStore
{
    private const int MaxJobs = 500;
    private const int MaxReleases = 10;
    private readonly object _gate = new();
    private readonly string _metadataPath;
    private readonly string _updatesDir;
    private readonly long _maxBytes;
    private readonly TimeZoneInfo _storeTimeZone;
    private readonly byte[] _tokenKey;
    private readonly IReadOnlyDictionary<string, string> _trustedKeys;
    private readonly List<KioskReleaseRecord> _releases = new();
    private readonly List<KioskUpdateJob> _jobs = new();
    private string? _activeVersion;
    private TimeSpan _windowStart = TimeSpan.FromHours(4);
    private TimeSpan _windowEnd = TimeSpan.FromHours(5);

    public event Action? Changed;

    private sealed class PersistedState
    {
        public string? ActiveVersion { get; set; }
        public string WindowStart { get; set; } = "04:00";
        public string WindowEnd { get; set; } = "05:00";
        public List<KioskReleaseRecord> Releases { get; set; } = new();
        public List<KioskUpdateJob> Jobs { get; set; } = new();
    }

    public KioskUpdateStore(string dataDir, string updatesDir, long maxBytes, TimeZoneInfo storeTimeZone,
        string tokenSecret, IReadOnlyDictionary<string, string> trustedKeys)
    {
        Directory.CreateDirectory(dataDir);
        Directory.CreateDirectory(updatesDir);
        _metadataPath = Path.Combine(dataDir, "kiosk-updates.json");
        _updatesDir = Path.GetFullPath(updatesDir);
        _maxBytes = maxBytes;
        _storeTimeZone = storeTimeZone;
        _tokenKey = SHA256.HashData(Encoding.UTF8.GetBytes(tokenSecret));
        _trustedKeys = trustedKeys;
        Load();
    }

    public string? ActiveVersion { get { lock (_gate) return _activeVersion; } }
    public TimeSpan WindowStart { get { lock (_gate) return _windowStart; } }
    public TimeSpan WindowEnd { get { lock (_gate) return _windowEnd; } }
    public IReadOnlyList<KioskReleaseRecord> Releases()
    { lock (_gate) return _releases.OrderByDescending(r => Parse(r.Version)).ToList(); }
    public IReadOnlyList<KioskUpdateJob> RecentJobs(int count = 100)
    { lock (_gate) { ExpireAwaitingRestart(); return _jobs.OrderByDescending(j => j.CreatedAtUtc).Take(count).ToList(); } }
    public DateTime StoreLocalTime(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(
        DateTime.SpecifyKind(utc, DateTimeKind.Utc), _storeTimeZone);

    public async Task<KioskReleaseRecord> ImportAsync(byte[] manifestBytes, string signatureBase64,
        string originalFileName, Stream setup, CancellationToken cancellationToken = default)
    {
        var unsigned = System.Text.Json.JsonSerializer.Deserialize<KioskReleaseManifest>(manifestBytes,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("Manifiesto vacío.");
        if (!_trustedKeys.TryGetValue(unsigned.KeyId, out string? publicKey))
            throw new InvalidDataException($"La clave '{unsigned.KeyId}' no está autorizada.");
        KioskReleaseManifest manifest = KioskReleaseSecurity.ParseAndVerify(manifestBytes, signatureBase64, publicKey);
        if (!string.Equals(Path.GetFileName(originalFileName), manifest.FileName, StringComparison.Ordinal))
            throw new InvalidDataException("El instalador no coincide con el nombre firmado.");
        if (manifest.SizeBytes > _maxBytes) throw new InvalidDataException("El instalador supera el tamaño máximo.");

        string versionDir = Path.Combine(_updatesDir, manifest.Version);
        Directory.CreateDirectory(versionDir);
        string destination = Path.Combine(versionDir, manifest.FileName);
        string tmp = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var output = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await setup.CopyToAsync(output, cancellationToken);
            var info = new FileInfo(tmp);
            if (info.Length != manifest.SizeBytes || !KioskReleaseSecurity.Sha256(tmp).Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("El instalador no coincide con el tamaño o SHA-256 firmados.");

            lock (_gate)
            {
                var existing = _releases.FirstOrDefault(r => r.Version.Equals(manifest.Version, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    if (!existing.Sha256.Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("La versión ya existe con un contenido diferente.");
                    return existing;
                }
                File.Move(tmp, destination, false);
                var release = new KioskReleaseRecord
                {
                    Version = manifest.Version, FileName = manifest.FileName, SizeBytes = manifest.SizeBytes,
                    Sha256 = manifest.Sha256.ToLowerInvariant(), KeyId = manifest.KeyId,
                    PublishedAtUtc = manifest.PublishedAtUtc, ReleaseNotes = manifest.ReleaseNotes,
                    GitHubFallbackUrl = manifest.GitHubFallbackUrl,
                    ManifestBase64 = Convert.ToBase64String(manifestBytes), SignatureBase64 = signatureBase64.Trim(),
                    StoredFileName = Path.GetRelativePath(_updatesDir, destination), State = KioskReleaseState.Available
                };
                _releases.Add(release);
                TrimReleases();
                Save();
                Changed?.Invoke();
                return release;
            }
        }
        finally { try { if (File.Exists(tmp)) File.Delete(tmp); } catch { } }
    }

    public void Activate(string version, IEnumerable<FleetDevice> devices)
    {
        lock (_gate)
        {
            var release = FindRelease(version) ?? throw new KeyNotFoundException("Release no encontrada.");
            if (release.State == KioskReleaseState.Withdrawn) throw new InvalidOperationException("La release está retirada.");
            foreach (var item in _releases.Where(r => r.State == KioskReleaseState.Active)) item.State = KioskReleaseState.Available;
            release.State = KioskReleaseState.Active;
            _activeVersion = release.Version;
            foreach (var old in _jobs.Where(j => j.TargetVersion != release.Version && !j.IsTerminal && j.State < KioskUpdateState.Installing))
            { old.State = KioskUpdateState.Failed; old.Message = "Sustituida por una nueva versión objetivo."; old.UpdatedAtUtc = DateTime.UtcNow; }
            foreach (FleetDevice device in devices) EnsureJob(device.Id, device.Name, device.AppVersion);
            Save();
        }
        Changed?.Invoke();
    }

    public void Withdraw(string version)
    {
        lock (_gate)
        {
            var release = FindRelease(version) ?? throw new KeyNotFoundException("Release no encontrada.");
            release.State = KioskReleaseState.Withdrawn;
            if (_activeVersion?.Equals(version, StringComparison.OrdinalIgnoreCase) == true) _activeVersion = null;
            foreach (var job in _jobs.Where(j => j.TargetVersion == version && !j.IsTerminal && j.State < KioskUpdateState.Installing))
            { job.State = KioskUpdateState.Failed; job.Message = "Release retirada antes de instalar."; job.UpdatedAtUtc = DateTime.UtcNow; }
            Save();
        }
        Changed?.Invoke();
    }

    public void SetWindow(TimeSpan start, TimeSpan end)
    {
        if (start < TimeSpan.Zero || end > TimeSpan.FromDays(1) || start >= end)
            throw new ArgumentException("La ventana debe estar dentro del día y tener una hora final posterior.");
        lock (_gate) { _windowStart = start; _windowEnd = end; Save(); }
        Changed?.Invoke();
    }

    public KioskUpdateAssignment? GetAssignment(string deviceId, string deviceName, string currentVersion)
    {
        if (deviceId.Length != 32 || !deviceId.All(Uri.IsHexDigit) ||
            !KioskReleaseSecurity.TryParseVersion(currentVersion, out _)) return null;
        deviceName = string.IsNullOrWhiteSpace(deviceName) ? deviceId : deviceName[..Math.Min(100, deviceName.Length)];
        lock (_gate)
        {
            ExpireAwaitingRestart();
            if (_activeVersion == null) return null;
            var confirmation = _jobs.LastOrDefault(j => j.DeviceId == deviceId && j.TargetVersion == currentVersion &&
                j.State is KioskUpdateState.Installing or KioskUpdateState.AwaitingRestart);
            if (confirmation != null)
            {
                confirmation.State = KioskUpdateState.Succeeded;
                confirmation.ProgressPercent = 100;
                confirmation.Message = "Versión confirmada por el heartbeat del kiosco.";
                confirmation.UpdatedAtUtc = DateTime.UtcNow;
                Save(); Changed?.Invoke();
            }
            if (VersionsEqual(currentVersion, _activeVersion)) return null;
            KioskUpdateJob job = EnsureJob(deviceId, deviceName, currentVersion);
            if (job.State is not (KioskUpdateState.Assigned or KioskUpdateState.Downloading)) return null;
            return ToAssignment(job);
        }
    }

    public (KioskUpdateJob Job, KioskReleaseRecord Release)? Authorize(string jobId, string? token, string? deviceId = null)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        lock (_gate)
        {
            var job = _jobs.FirstOrDefault(j => j.Id == jobId);
            if (job == null || job.IsTerminal || (deviceId != null && job.DeviceId != deviceId) ||
                !SecretEquals(Token(job), token)) return null;
            if (_activeVersion != job.TargetVersion && job.State < KioskUpdateState.Installing) return null;
            var release = FindRelease(job.TargetVersion);
            return release is { State: not KioskReleaseState.Withdrawn } ? (job, release) : null;
        }
    }

    public KioskUpdateAssignment? AuthorizedAssignment(string jobId, string? token)
    {
        lock (_gate)
        {
            var authorized = Authorize(jobId, token);
            if (authorized == null) return null;
            KioskUpdateJob job = authorized.Value.Job;
            if (job.InstallBeforeUtc <= DateTime.UtcNow && job.State < KioskUpdateState.Installing)
            {
                (job.InstallAfterUtc, job.InstallBeforeUtc) = NextWindow(DateTime.UtcNow, job.DeviceId, job.TargetVersion);
                job.UpdatedAtUtc = DateTime.UtcNow;
                Save();
            }
            return ToAssignment(job);
        }
    }

    public bool Update(string jobId, string? token, KioskUpdateStatusUpdate update)
    {
        if (!Enum.IsDefined(update.State)) return false;
        if (Authorize(jobId, token, update.DeviceId) is not { } authorized) return false;
        lock (_gate)
        {
            var job = _jobs.First(j => j.Id == authorized.Job.Id);
            if (!CanTransition(job.State, update.State)) return false;
            job.State = update.State;
            job.ProgressPercent = update.ProgressPercent is null ? job.ProgressPercent : Math.Clamp(update.ProgressPercent.Value, 0, 100);
            job.ExitCode = update.ExitCode;
            job.Message = string.IsNullOrWhiteSpace(update.Message) ? null : update.Message[..Math.Min(500, update.Message.Length)];
            job.UpdatedAtUtc = DateTime.UtcNow;
            Save();
        }
        Changed?.Invoke();
        return true;
    }

    public void Retry(string deviceId, string currentVersion, string deviceName)
    {
        lock (_gate)
        {
            foreach (var old in _jobs.Where(j => j.DeviceId == deviceId && !j.IsTerminal))
            { old.State = KioskUpdateState.Failed; old.Message = "Sustituido por un reintento manual."; old.UpdatedAtUtc = DateTime.UtcNow; }
            EnsureJob(deviceId, deviceName, currentVersion);
            Save();
        }
        Changed?.Invoke();
    }

    public void RunNow(string deviceId)
    {
        lock (_gate)
        {
            var job = _jobs.LastOrDefault(j => j.DeviceId == deviceId && !j.IsTerminal)
                ?? throw new KeyNotFoundException("No hay actualización pendiente para el equipo.");
            job.InstallAfterUtc = DateTime.UtcNow.AddMinutes(-1);
            job.InstallBeforeUtc = DateTime.UtcNow.AddHours(1);
            job.UpdatedAtUtc = DateTime.UtcNow;
            Save();
        }
        Changed?.Invoke();
    }

    public byte[] ManifestBytes(KioskReleaseRecord release) => Convert.FromBase64String(release.ManifestBase64);
    public string ResolveFile(KioskReleaseRecord release)
    {
        string full = Path.GetFullPath(Path.Combine(_updatesDir, release.StoredFileName));
        if (!full.StartsWith(_updatesDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Ruta de release no válida.");
        return full;
    }

    private KioskUpdateJob EnsureJob(string deviceId, string deviceName, string currentVersion)
    {
        var existing = _jobs.LastOrDefault(j => j.DeviceId == deviceId && j.TargetVersion == _activeVersion && !j.IsTerminal);
        if (existing != null) return existing;
        Version current = Parse(currentVersion), target = Parse(_activeVersion!);
        (DateTime after, DateTime before) = NextWindow(DateTime.UtcNow, deviceId, _activeVersion!);
        var job = new KioskUpdateJob
        {
            Id = Guid.NewGuid().ToString("N"), DeviceId = deviceId, DeviceName = deviceName,
            FromVersion = currentVersion, TargetVersion = _activeVersion!,
            Operation = target < current ? KioskUpdateOperation.Rollback : KioskUpdateOperation.Upgrade,
            State = KioskUpdateState.Assigned, InstallAfterUtc = after, InstallBeforeUtc = before,
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
        };
        _jobs.Add(job);
        TrimJobs();
        return job;
    }

    private (DateTime afterUtc, DateTime beforeUtc) NextWindow(DateTime nowUtc, string deviceId, string version)
    {
        DateTime localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc), _storeTimeZone);
        DateTime start = localNow.Date + _windowStart;
        DateTime end = localNow.Date + _windowEnd;
        if (localNow >= end) { start = start.AddDays(1); end = end.AddDays(1); }
        else if (localNow > start) start = localNow;
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(deviceId + "|" + version));
        int jitterSeconds = BitConverter.ToUInt16(hash, 0) % 601;
        DateTime after = start.AddSeconds(jitterSeconds);
        if (after >= end) after = start;
        return (TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(after, DateTimeKind.Unspecified), _storeTimeZone),
                TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(end, DateTimeKind.Unspecified), _storeTimeZone));
    }

    private KioskUpdateAssignment ToAssignment(KioskUpdateJob job) => new()
    {
        JobId = job.Id, DeviceId = job.DeviceId, Version = job.TargetVersion, Operation = job.Operation,
        InstallAfterUtc = job.InstallAfterUtc, InstallBeforeUtc = job.InstallBeforeUtc, Token = Token(job)
    };

    private string Token(KioskUpdateJob job)
    {
        using var hmac = new HMACSHA256(_tokenKey);
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{job.Id}|{job.DeviceId}|{job.TargetVersion}"))).ToLowerInvariant();
    }

    private static bool SecretEquals(string left, string right)
    {
        byte[] a = SHA256.HashData(Encoding.UTF8.GetBytes(left));
        byte[] b = SHA256.HashData(Encoding.UTF8.GetBytes(right));
        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    private static bool CanTransition(KioskUpdateState current, KioskUpdateState next)
    {
        if (current is KioskUpdateState.Succeeded or KioskUpdateState.Failed) return false;
        return next == KioskUpdateState.Failed || next >= current;
    }

    private void ExpireAwaitingRestart()
    {
        DateTime cutoff = DateTime.UtcNow.AddHours(-2);
        bool changed = false;
        foreach (var job in _jobs.Where(j => j.State == KioskUpdateState.AwaitingRestart && j.UpdatedAtUtc < cutoff))
        { job.State = KioskUpdateState.Failed; job.Message = "El equipo no confirmó la versión tras reiniciar."; job.UpdatedAtUtc = DateTime.UtcNow; changed = true; }
        if (changed) Save();
    }

    private KioskReleaseRecord? FindRelease(string version) =>
        _releases.FirstOrDefault(r => r.Version.Equals(version, StringComparison.OrdinalIgnoreCase));
    private static Version Parse(string value) => KioskReleaseSecurity.TryParseVersion(value, out var v) ? v : new Version();
    private static bool VersionsEqual(string a, string b) => Parse(a) == Parse(b);
    private void TrimJobs()
    {
        if (_jobs.Count <= MaxJobs) return;
        var remove = _jobs.Where(j => j.IsTerminal).OrderByDescending(j => j.CreatedAtUtc).Skip(MaxJobs).ToHashSet();
        _jobs.RemoveAll(remove.Contains);
    }

    private void TrimReleases()
    {
        if (_releases.Count <= MaxReleases) return;
        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (_activeVersion != null) keep.Add(_activeVersion);
        foreach (var job in _jobs.Where(j => !j.IsTerminal)) keep.Add(job.TargetVersion);
        foreach (var release in _releases.OrderByDescending(r => Parse(r.Version)))
        {
            if (keep.Count >= MaxReleases) break;
            keep.Add(release.Version);
        }

        foreach (var release in _releases.Where(r => !keep.Contains(r.Version)).ToList())
        {
            try
            {
                string file = ResolveFile(release);
                if (File.Exists(file)) File.Delete(file);
                _releases.Remove(release);
                string? directory = Path.GetDirectoryName(file);
                if (directory != null && Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
                    Directory.Delete(directory);
            }
            catch { /* Si el fichero está en uso, conserva también sus metadatos para reintentarlo en otra importación. */ }
        }
    }

    private void Save()
    {
        var state = new PersistedState
        {
            ActiveVersion = _activeVersion, WindowStart = _windowStart.ToString(@"hh\:mm"),
            WindowEnd = _windowEnd.ToString(@"hh\:mm"), Releases = _releases, Jobs = _jobs
        };
        string tmp = _metadataPath + ".tmp";
        File.WriteAllText(tmp, JsonConvert.SerializeObject(state, Formatting.Indented));
        File.Move(tmp, _metadataPath, true);
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_metadataPath)) return;
            var state = JsonConvert.DeserializeObject<PersistedState>(File.ReadAllText(_metadataPath));
            if (state == null) return;
            _activeVersion = state.ActiveVersion;
            if (TimeSpan.TryParse(state.WindowStart, out var start)) _windowStart = start;
            if (TimeSpan.TryParse(state.WindowEnd, out var end)) _windowEnd = end;
            _releases.AddRange(state.Releases);
            _jobs.AddRange(state.Jobs);
        }
        catch { }
    }
}
