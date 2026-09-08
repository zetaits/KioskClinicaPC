using System.Security.Cryptography;
using System.Text;
using KioskClinicaPC.Core.Sync;
using Newtonsoft.Json;

namespace Kiosk.Server.Services;

public sealed class InitialSetupSessionPackage
{
    public string PackageId { get; set; } = "";
    public string PackageName { get; set; } = "";
    public string PackageSha256 { get; set; } = "";
    public InstallationJobState State { get; set; } = InstallationJobState.Pending;
    public int? ProgressPercent { get; set; }
    public int? ExitCode { get; set; }
    public string? Message { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public bool IsTerminal => State is InstallationJobState.Succeeded or InstallationJobState.RebootRequired or InstallationJobState.Failed;
}

public sealed class InitialSetupSession
{
    public string Id { get; set; } = "";
    public string MachineName { get; set; } = "";
    public string SetupVersion { get; set; } = "";
    public string TokenHash { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public List<InitialSetupSessionPackage> Packages { get; set; } = new();
    public bool IsTerminal => Packages.Count > 0 && Packages.All(p => p.IsTerminal);
}

public sealed record CreatedInitialSetupSession(InitialSetupSession Session, string Token);

/// <summary>Auditoria y autorizacion efimera de instalaciones lanzadas desde el Setup inicial.</summary>
public sealed class InitialSetupSessionStore
{
    private const int MaxSessions = 500;
    private static readonly TimeSpan StaleAfter = TimeSpan.FromHours(2);
    private readonly object _gate = new();
    private readonly string _path;
    private readonly List<InitialSetupSession> _sessions = new();
    public event Action? Changed;

    public InitialSetupSessionStore(string dataDir)
    {
        Directory.CreateDirectory(dataDir);
        _path = Path.Combine(dataDir, "setup-installations.json");
        Load();
    }

    public IReadOnlyList<InitialSetupSession> Recent(int count = 50)
    {
        lock (_gate)
        {
            ExpireStale();
            return _sessions.OrderByDescending(s => s.CreatedAtUtc).Take(count).ToList();
        }
    }

    public bool HasActivePackage(string packageId)
    {
        lock (_gate)
        {
            ExpireStale();
            return _sessions.Any(s => !s.IsTerminal && s.Packages.Any(p => p.PackageId == packageId && !p.IsTerminal));
        }
    }

    public CreatedInitialSetupSession Create(string machineName, string setupVersion, IReadOnlyList<InstallerPackage> packages)
    {
        machineName = string.IsNullOrWhiteSpace(machineName) ? "Equipo sin nombre" : machineName.Trim();
        if (machineName.Length > 100) machineName = machineName[..100];
        setupVersion = string.IsNullOrWhiteSpace(setupVersion) ? "desconocida" : setupVersion.Trim();
        if (setupVersion.Length > 50) setupVersion = setupVersion[..50];
        if (packages.Count == 0) throw new ArgumentException("Selecciona al menos una aplicaci\u00f3n.");
        if (packages.Select(p => p.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != packages.Count)
            throw new ArgumentException("La selecci\u00f3n contiene aplicaciones duplicadas.");

        string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        DateTime now = DateTime.UtcNow;
        var session = new InitialSetupSession
        {
            Id = Guid.NewGuid().ToString("N"), MachineName = machineName, SetupVersion = setupVersion,
            TokenHash = HashToken(token), CreatedAtUtc = now, UpdatedAtUtc = now,
            Packages = packages.Select(p => new InitialSetupSessionPackage
            {
                PackageId = p.Id, PackageName = p.DisplayName, PackageSha256 = p.Sha256, UpdatedAtUtc = now
            }).ToList()
        };
        lock (_gate) { ExpireStale(); _sessions.Add(session); TrimAndSave(); }
        Changed?.Invoke();
        return new(session, token);
    }

    public InitialSetupSessionPackage? AuthorizePackage(string sessionId, string packageId, string token)
    {
        lock (_gate)
        {
            ExpireStale();
            var session = _sessions.FirstOrDefault(s => s.Id == sessionId);
            if (session == null || !TokenMatches(session.TokenHash, token)) return null;
            return session.Packages.FirstOrDefault(p => p.PackageId == packageId && !p.IsTerminal);
        }
    }

    public bool Update(string sessionId, string packageId, string token, InitialSetupStatusUpdate update)
    {
        lock (_gate)
        {
            ExpireStale();
            var session = _sessions.FirstOrDefault(s => s.Id == sessionId);
            if (session == null || !TokenMatches(session.TokenHash, token)) return false;
            var package = session.Packages.FirstOrDefault(p => p.PackageId == packageId);
            if (package == null || !CanTransition(package.State, update.State)) return false;
            package.State = update.State;
            package.ProgressPercent = update.ProgressPercent is null ? package.ProgressPercent : Math.Clamp(update.ProgressPercent.Value, 0, 100);
            package.ExitCode = update.ExitCode;
            package.Message = string.IsNullOrWhiteSpace(update.Message) ? null : update.Message[..Math.Min(update.Message.Length, 500)];
            package.UpdatedAtUtc = DateTime.UtcNow;
            session.UpdatedAtUtc = package.UpdatedAtUtc;
            TrimAndSave();
        }
        Changed?.Invoke();
        return true;
    }

    private static bool CanTransition(InstallationJobState current, InstallationJobState next)
    {
        if (current is InstallationJobState.Succeeded or InstallationJobState.RebootRequired or InstallationJobState.Failed) return false;
        return next == InstallationJobState.Failed || next >= current;
    }

    private static bool TokenMatches(string expectedHash, string token)
    {
        try
        {
            byte[] expected = Convert.FromHexString(expectedHash);
            byte[] actual = Convert.FromHexString(HashToken(token));
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch { return false; }
    }

    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    private void ExpireStale()
    {
        DateTime cutoff = DateTime.UtcNow - StaleAfter;
        bool changed = false;
        foreach (var session in _sessions.Where(s => !s.IsTerminal && s.UpdatedAtUtc < cutoff))
        {
            foreach (var package in session.Packages.Where(p => !p.IsTerminal))
            {
                package.State = InstallationJobState.Failed;
                package.Message = "La instalaci\u00f3n inicial caduc\u00f3 sin recibir un resultado.";
                package.UpdatedAtUtc = DateTime.UtcNow;
            }
            session.UpdatedAtUtc = DateTime.UtcNow;
            changed = true;
        }
        if (changed) TrimAndSave();
    }

    private void TrimAndSave()
    {
        if (_sessions.Count > MaxSessions)
        {
            int excess = _sessions.Count - MaxSessions;
            var remove = _sessions.Where(s => s.IsTerminal).OrderBy(s => s.CreatedAtUtc).Take(excess).ToList();
            foreach (var session in remove) _sessions.Remove(session);
        }
        string tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonConvert.SerializeObject(_sessions, Formatting.Indented));
        File.Move(tmp, _path, true);
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var sessions = JsonConvert.DeserializeObject<List<InitialSetupSession>>(File.ReadAllText(_path));
            if (sessions != null) _sessions.AddRange(sessions);
        }
        catch { }
    }
}
