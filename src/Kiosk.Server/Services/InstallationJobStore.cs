using System.Security.Cryptography;
using System.Text;
using KioskClinicaPC.Core.Sync;
using Newtonsoft.Json;

namespace Kiosk.Server.Services;

public sealed class InstallationJob
{
    public string Id { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public string PackageId { get; set; } = "";
    public string PackageName { get; set; } = "";
    public string PackageSha256 { get; set; } = "";
    public string TokenHash { get; set; } = "";
    public InstallationJobState State { get; set; }
    public int? ProgressPercent { get; set; }
    public int? ExitCode { get; set; }
    public string? Message { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public bool IsTerminal => State is InstallationJobState.Succeeded or InstallationJobState.RebootRequired or InstallationJobState.Failed;
}

public sealed record CreatedInstallationJob(InstallationJob Job, string Token);

public sealed class InstallationJobStore
{
    private const int MaxJobs = 500;
    private static readonly TimeSpan StaleAfter = TimeSpan.FromHours(2);
    private readonly object _gate = new();
    private readonly string _path;
    private readonly List<InstallationJob> _jobs = new();
    public event Action? Changed;

    public InstallationJobStore(string dataDir)
    {
        Directory.CreateDirectory(dataDir);
        _path = Path.Combine(dataDir, "install-jobs.json");
        Load();
    }

    public IReadOnlyList<InstallationJob> Recent(int count = 100)
    { lock (_gate) { ExpireStale(); return _jobs.OrderByDescending(j => j.CreatedAtUtc).Take(count).ToList(); } }

    public bool HasActiveJob(string deviceId)
    { lock (_gate) { ExpireStale(); return _jobs.Any(j => j.DeviceId == deviceId && !j.IsTerminal); } }

    public bool HasActivePackage(string packageId)
    { lock (_gate) { ExpireStale(); return _jobs.Any(j => j.PackageId == packageId && !j.IsTerminal); } }

    public CreatedInstallationJob Create(FleetDevice device, InstallerPackage package)
    {
        byte[] tokenBytes = RandomNumberGenerator.GetBytes(32);
        string token = Convert.ToHexString(tokenBytes).ToLowerInvariant();
        var now = DateTime.UtcNow;
        var job = new InstallationJob
        {
            Id = Guid.NewGuid().ToString("N"), DeviceId = device.Id, DeviceName = device.Name,
            PackageId = package.Id, PackageName = package.DisplayName, PackageSha256 = package.Sha256,
            TokenHash = HashToken(token), State = InstallationJobState.Pending,
            CreatedAtUtc = now, UpdatedAtUtc = now
        };
        lock (_gate)
        {
            ExpireStale();
            if (_jobs.Any(j => j.DeviceId == device.Id && !j.IsTerminal))
                throw new InvalidOperationException($"{device.Name} ya tiene una instalación activa.");
            _jobs.Add(job);
            TrimAndSave();
        }
        Changed?.Invoke();
        return new(job, token);
    }

    public InstallationJob? Authorize(string jobId, string token, string? deviceId = null)
    {
        lock (_gate)
        {
            var job = _jobs.FirstOrDefault(j => j.Id == jobId);
            if (job == null || job.IsTerminal || (deviceId != null && job.DeviceId != deviceId)) return null;
            byte[] expected = Convert.FromHexString(job.TokenHash);
            byte[] actual = Convert.FromHexString(HashToken(token));
            return CryptographicOperations.FixedTimeEquals(expected, actual) ? job : null;
        }
    }

    public void MarkDispatched(string id) => UpdateInternal(id, InstallationJobState.Dispatched, null, null, null);

    public bool Update(string id, string token, InstallationStatusUpdate update)
    {
        if (Authorize(id, token, update.DeviceId) == null) return false;
        return UpdateInternal(id, update.State, update.ProgressPercent, update.ExitCode, update.Message);
    }

    private bool UpdateInternal(string id, InstallationJobState next, int? progress, int? exitCode, string? message)
    {
        lock (_gate)
        {
            var job = _jobs.FirstOrDefault(j => j.Id == id);
            if (job == null || !CanTransition(job.State, next)) return false;
            job.State = next;
            job.ProgressPercent = progress is null ? job.ProgressPercent : Math.Clamp(progress.Value, 0, 100);
            job.ExitCode = exitCode;
            job.Message = string.IsNullOrWhiteSpace(message) ? null : message[..Math.Min(message.Length, 500)];
            job.UpdatedAtUtc = DateTime.UtcNow;
            TrimAndSave();
        }
        Changed?.Invoke();
        return true;
    }

    private static bool CanTransition(InstallationJobState current, InstallationJobState next)
    {
        if (current is InstallationJobState.Succeeded or InstallationJobState.RebootRequired or InstallationJobState.Failed) return false;
        if (next == InstallationJobState.Failed) return true;
        return next >= current;
    }

    private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
    private void ExpireStale()
    {
        bool changed = false;
        DateTime cutoff = DateTime.UtcNow - StaleAfter;
        foreach (var job in _jobs.Where(j => !j.IsTerminal && j.UpdatedAtUtc < cutoff))
        {
            job.State = InstallationJobState.Failed;
            job.Message = "Trabajo interrumpido o sin respuesta durante más de dos horas.";
            job.UpdatedAtUtc = DateTime.UtcNow;
            changed = true;
        }
        if (changed) TrimAndSave();
    }
    private void TrimAndSave()
    {
        if (_jobs.Count > MaxJobs)
            _jobs.RemoveAll(j => j.IsTerminal && _jobs.OrderByDescending(x => x.CreatedAtUtc).Skip(MaxJobs).Contains(j));
        string tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonConvert.SerializeObject(_jobs, Formatting.Indented));
        File.Move(tmp, _path, true);
    }
    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var jobs = JsonConvert.DeserializeObject<List<InstallationJob>>(File.ReadAllText(_path));
            if (jobs != null) _jobs.AddRange(jobs);
        }
        catch { }
    }
}
