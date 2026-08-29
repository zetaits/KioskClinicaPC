using System.Security.Cryptography;
using System.Text;
using KioskClinicaPC.Core.Sync;
using Newtonsoft.Json;

namespace Kiosk.Server.Services;

public sealed class MaintenanceJob
{
    public string Id { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public string TokenHash { get; set; } = "";
    public MaintenanceJobState State { get; set; }
    public int? ExitCode { get; set; }
    public string? Message { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public bool IsTerminal => State is MaintenanceJobState.Succeeded or MaintenanceJobState.RebootRequired or MaintenanceJobState.Failed;
}

public sealed record CreatedMaintenanceJob(MaintenanceJob Job, string Token);

/// <summary>
/// Historial y autorización de operaciones destructivas sobre el propio kiosko. Los tokens son
/// capacidades aleatorias ligadas a un único equipo y nunca se persisten en claro.
/// </summary>
public sealed class MaintenanceJobStore
{
    private const int MaxJobs = 500;
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(30);
    private readonly object _gate = new();
    private readonly string _path;
    private readonly List<MaintenanceJob> _jobs = new();
    public event Action? Changed;

    public MaintenanceJobStore(string dataDir)
    {
        Directory.CreateDirectory(dataDir);
        _path = Path.Combine(dataDir, "maintenance-jobs.json");
        Load();
    }

    public IReadOnlyList<MaintenanceJob> Recent(int count = 50)
    { lock (_gate) { ExpireStale(); return _jobs.OrderByDescending(j => j.CreatedAtUtc).Take(count).ToList(); } }

    public bool HasActiveJob(string deviceId)
    { lock (_gate) { ExpireStale(); return _jobs.Any(j => j.DeviceId == deviceId && !j.IsTerminal); } }

    public CreatedMaintenanceJob Create(FleetDevice device)
    {
        byte[] tokenBytes = RandomNumberGenerator.GetBytes(32);
        string token = Convert.ToHexString(tokenBytes).ToLowerInvariant();
        DateTime now = DateTime.UtcNow;
        var job = new MaintenanceJob
        {
            Id = Guid.NewGuid().ToString("N"), DeviceId = device.Id, DeviceName = device.Name,
            TokenHash = HashToken(token), State = MaintenanceJobState.Pending,
            CreatedAtUtc = now, UpdatedAtUtc = now
        };
        lock (_gate)
        {
            ExpireStale();
            if (_jobs.Any(j => j.DeviceId == device.Id && !j.IsTerminal))
                throw new InvalidOperationException($"{device.Name} ya tiene un mantenimiento activo.");
            _jobs.Add(job);
            TrimAndSave();
        }
        Changed?.Invoke();
        return new(job, token);
    }

    public MaintenanceJob? Authorize(string jobId, string token, string? deviceId = null)
    {
        if (!IsHex(token, 64)) return null;
        lock (_gate)
        {
            ExpireStale();
            var job = _jobs.FirstOrDefault(j => j.Id == jobId);
            if (job == null || job.IsTerminal || (deviceId != null && job.DeviceId != deviceId)) return null;
            byte[] expected = Convert.FromHexString(job.TokenHash);
            byte[] actual = Convert.FromHexString(HashToken(token));
            return CryptographicOperations.FixedTimeEquals(expected, actual) ? job : null;
        }
    }

    public void MarkDispatched(string id) => UpdateInternal(id, MaintenanceJobState.Dispatched, null, null);

    public bool Update(string id, string token, MaintenanceStatusUpdate update)
    {
        if (Authorize(id, token, update.DeviceId) == null) return false;
        return UpdateInternal(id, update.State, update.ExitCode, update.Message);
    }

    private bool UpdateInternal(string id, MaintenanceJobState next, int? exitCode, string? message)
    {
        lock (_gate)
        {
            var job = _jobs.FirstOrDefault(j => j.Id == id);
            if (job == null || !CanTransition(job.State, next)) return false;
            job.State = next;
            job.ExitCode = exitCode;
            job.Message = string.IsNullOrWhiteSpace(message) ? null : message[..Math.Min(message.Length, 500)];
            job.UpdatedAtUtc = DateTime.UtcNow;
            TrimAndSave();
        }
        Changed?.Invoke();
        return true;
    }

    private static bool CanTransition(MaintenanceJobState current, MaintenanceJobState next)
    {
        if (current is MaintenanceJobState.Succeeded or MaintenanceJobState.RebootRequired or MaintenanceJobState.Failed) return false;
        if (next == MaintenanceJobState.Failed) return true;
        return next >= current;
    }

    private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
    private static bool IsHex(string value, int length) => value.Length == length && value.All(Uri.IsHexDigit);

    private void ExpireStale()
    {
        bool changed = false;
        DateTime cutoff = DateTime.UtcNow - StaleAfter;
        foreach (var job in _jobs.Where(j => !j.IsTerminal && j.UpdatedAtUtc < cutoff))
        {
            job.State = MaintenanceJobState.Failed;
            job.Message = "La orden caducó sin recibir una confirmación final del equipo.";
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
            var jobs = JsonConvert.DeserializeObject<List<MaintenanceJob>>(File.ReadAllText(_path));
            if (jobs != null) _jobs.AddRange(jobs);
        }
        catch { }
    }
}
