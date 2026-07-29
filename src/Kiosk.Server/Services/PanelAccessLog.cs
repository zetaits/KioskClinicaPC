using Newtonsoft.Json;

namespace Kiosk.Server.Services;

/// <summary>Resultado de un intento de acceso al panel.</summary>
public enum AccessOutcome { Success, Fail, Locked }

/// <summary>Una entrada del registro de accesos al panel.</summary>
public sealed record AccessEntry(DateTime TimestampUtc, string Ip, AccessOutcome Outcome);

/// <summary>
/// Registro de accesos al panel (aciertos, fallos y bloqueos por fuerza bruta). Lo escribe el endpoint
/// <c>POST /login</c> y lo lee la página de Seguridad. Persistido en <c>access-log.json</c> con tope de
/// entradas (rota las más viejas). Thread-safe.
/// </summary>
public sealed class PanelAccessLog
{
    private const int MaxEntries = 200;

    private readonly object _gate = new();
    private readonly string _path;
    private readonly LinkedList<AccessEntry> _entries = new();

    public PanelAccessLog(string dataDir)
    {
        Directory.CreateDirectory(dataDir);
        _path = Path.Combine(dataDir, "access-log.json");
        Load();
    }

    public void Record(string ip, AccessOutcome outcome)
    {
        lock (_gate)
        {
            _entries.AddFirst(new AccessEntry(DateTime.UtcNow, string.IsNullOrWhiteSpace(ip) ? "desconocida" : ip, outcome));
            while (_entries.Count > MaxEntries) _entries.RemoveLast();
            Save();
        }
    }

    /// <summary>Últimas entradas, de la más reciente a la más antigua.</summary>
    public IReadOnlyList<AccessEntry> Recent(int count)
    {
        lock (_gate) return _entries.Take(count).ToList();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var list = JsonConvert.DeserializeObject<List<AccessEntry>>(File.ReadAllText(_path));
            if (list != null) foreach (var e in list) _entries.AddLast(e);
        }
        catch { /* corrupto: arranca vacío antes que morir */ }
    }

    private void Save()
    {
        string content = JsonConvert.SerializeObject(_entries.ToList(), Formatting.Indented);
        string tmp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(tmp, content);
            File.Move(tmp, _path, overwrite: true);
        }
        finally { try { if (File.Exists(tmp)) File.Delete(tmp); } catch { } }
    }
}
