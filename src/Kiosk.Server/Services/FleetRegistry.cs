using KioskClinicaPC.Core.Sync;
using Newtonsoft.Json;

namespace Kiosk.Server.Services;

/// <summary>Conectividad del kiosko vista desde el servidor (derivada del heartbeat, no la reporta el cliente).</summary>
public enum KioskStatus { Online, Busy, Offline }

/// <summary>Un kiosko de la tienda tal como lo pinta el panel. Se construye a partir del último
/// <see cref="KioskHeartbeat"/> recibido más los overrides del panel (nombre) y el estado de conexión.</summary>
public sealed class FleetDevice
{
    public string Id { get; init; } = "";
    /// <summary>Nombre efectivo: override del panel si existe, si no el reportado por el equipo.</summary>
    public string Name { get; set; } = "";
    /// <summary>Nombre que reporta el propio equipo (para detectar si el override ya se aplicó).</summary>
    public string ReportedName { get; set; } = "";
    public string Ip { get; set; } = "";
    public KioskScreen Screen { get; set; }
    public string Equipment { get; set; } = "";
    public string Cpu { get; set; } = "";
    public decimal Price { get; set; }
    public decimal OldPrice { get; set; }
    public string AppVersion { get; set; } = "";

    /// <summary>ConnectionId del hub mientras está conectado; null si está caído.</summary>
    public string? ConnectionId { get; set; }
    public DateTime LastSeenUtc { get; set; }
    public long StartedAtUnixMs { get; set; }

    public KioskStatus Status { get; set; }
    public bool IsOnline => Status != KioskStatus.Offline;
    public bool HasOldPrice => OldPrice > 0 && OldPrice > Price;

    /// <summary>Uptime legible calculado desde <see cref="StartedAtUnixMs"/> (o "—" si caído/desconocido).</summary>
    public string Uptime
    {
        get
        {
            if (Status == KioskStatus.Offline)
                return LastSeenUtc == default ? "—" : "hace " + Humanize(DateTime.UtcNow - LastSeenUtc);
            if (StartedAtUnixMs <= 0) return "—";
            var up = DateTime.UtcNow - DateTimeOffset.FromUnixTimeMilliseconds(StartedAtUnixMs).UtcDateTime;
            return Humanize(up);
        }
    }

    private static string Humanize(TimeSpan t)
    {
        if (t.TotalDays >= 1) return $"{(int)t.TotalDays}d {t.Hours}h";
        if (t.TotalHours >= 1) return $"{(int)t.TotalHours}h {t.Minutes}m";
        if (t.TotalMinutes >= 1) return $"{(int)t.TotalMinutes} min";
        return "ahora";
    }
}

/// <summary>Una línea del registro de actividad de la tienda.</summary>
public sealed record FleetActivity(string Time, string Message);

/// <summary>
/// Inventario REAL de kioscos de la tienda. Los clientes se registran y mandan heartbeats por
/// <see cref="Kiosk.Server.Hubs.FleetHub"/>; aquí se guarda el último estado de cada uno, se derivan
/// online/en-uso/caído y se computa el uptime. Los overrides del panel (nombre, y precio pendiente de
/// aplicar) se persisten en <c>fleet.json</c> para sobrevivir reinicios y reconexiones. La actividad
/// reciente (conexiones, caídas, órdenes, publicaciones) se guarda en <c>fleet-activity.json</c>.
///
/// Thread-safe: lo tocan hilos del hub (SignalR) y de render (Blazor). Las páginas se suscriben a
/// <see cref="Changed"/> para refrescarse en vivo.
/// </summary>
public sealed class FleetRegistry
{
    /// <summary>Sin heartbeat en este margen → se considera caído (el latido va cada ~20 s).</summary>
    public static readonly TimeSpan OfflineAfter = TimeSpan.FromSeconds(60);
    private const int MaxActivity = 60;

    private sealed class DeviceOverride
    {
        public string? Name { get; set; }
        public decimal? Price { get; set; }
        public decimal? OldPrice { get; set; }
        public bool HasName => !string.IsNullOrWhiteSpace(Name);
        public bool HasPrice => Price.HasValue;
    }

    private readonly object _gate = new();
    private readonly Dictionary<string, FleetDevice> _devices = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DeviceOverride> _overrides = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<FleetActivity> _activity = new();

    private readonly TimeZoneInfo _storeTz;
    private readonly string _overridesPath;
    private readonly string _activityPath;

    /// <summary>Se dispara en cualquier cambio de estado de la flota; las páginas hacen StateHasChanged.</summary>
    public event Action? Changed;

    public FleetRegistry(string dataDir, TimeZoneInfo storeTz)
    {
        _storeTz = storeTz;
        Directory.CreateDirectory(dataDir);
        _overridesPath = Path.Combine(dataDir, "fleet.json");
        _activityPath = Path.Combine(dataDir, "fleet-activity.json");
        LoadOverrides();
        LoadActivity();
    }

    // ── Lecturas para el panel ────────────────────────────────────────────────────────────────

    public IReadOnlyList<FleetDevice> Devices
    {
        get { lock (_gate) return _devices.Values.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToList(); }
    }

    public int OnlineCount { get { lock (_gate) return _devices.Values.Count(d => d.IsOnline); } }
    public int OfflineCount { get { lock (_gate) return _devices.Values.Count(d => !d.IsOnline); } }

    /// <summary>Media del precio expuesto, contando solo los equipos que muestran alguno.</summary>
    public decimal AveragePrice
    {
        get
        {
            lock (_gate)
            {
                var priced = _devices.Values.Where(d => d.Price > 0).ToList();
                return priced.Count == 0 ? 0 : Math.Round(priced.Average(d => d.Price));
            }
        }
    }

    public FleetDevice? Find(string id)
    {
        lock (_gate) return _devices.TryGetValue(id, out var d) ? d : null;
    }

    /// <summary>ConnectionIds de todos los equipos conectados (para las órdenes "a todos").</summary>
    public IReadOnlyList<string> OnlineConnectionIds()
    {
        lock (_gate) return _devices.Values.Where(d => d.ConnectionId != null).Select(d => d.ConnectionId!).ToList();
    }

    public IReadOnlyList<FleetActivity> RecentActivity()
    {
        lock (_gate) return _activity.ToList();
    }

    // ── Escrituras desde el hub ───────────────────────────────────────────────────────────────

    /// <summary>Registra o actualiza un equipo desde su heartbeat. Devuelve las órdenes pendientes que hay
    /// que reenviarle (overrides que aún no reflejó), para que el hub las empuje al llamante.</summary>
    public IReadOnlyList<FleetCommand> Upsert(string connectionId, string ip, KioskHeartbeat hb)
    {
        if (string.IsNullOrWhiteSpace(hb.DeviceId)) return Array.Empty<FleetCommand>();

        List<FleetCommand> pending = new();
        bool cameOnline;
        string name;
        lock (_gate)
        {
            if (!_devices.TryGetValue(hb.DeviceId, out var d))
            {
                d = new FleetDevice { Id = hb.DeviceId };
                _devices[hb.DeviceId] = d;
                cameOnline = true;
            }
            else cameOnline = !d.IsOnline;

            d.ReportedName = hb.Name ?? "";
            d.ConnectionId = connectionId;
            d.Ip = ip;
            d.Screen = hb.Screen;
            d.Equipment = hb.Equipment ?? "";
            d.Cpu = hb.Cpu ?? "";
            d.Price = hb.Price;
            d.OldPrice = hb.OldPrice;
            d.AppVersion = hb.AppVersion ?? "";
            d.StartedAtUnixMs = hb.StartedAtUnixMs;
            d.LastSeenUtc = DateTime.UtcNow;

            var ov = _overrides.TryGetValue(hb.DeviceId, out var o) ? o : null;
            d.Name = ov?.HasName == true ? ov.Name! : d.ReportedName;
            d.Status = DeriveStatus(d);
            name = d.Name;

            // Overrides no reflejados aún por el equipo → reenviar.
            if (ov?.HasName == true && !string.Equals(d.ReportedName, ov.Name, StringComparison.Ordinal))
                pending.Add(FleetCommand.SetName(ov.Name!));
            if (ov?.HasPrice == true && (d.Price != ov.Price || d.OldPrice != (ov.OldPrice ?? 0)))
                pending.Add(FleetCommand.SetPrice(ov.Price!.Value, ov.OldPrice ?? 0));
        }

        if (cameOnline) Log($"{name} conectado");
        Changed?.Invoke();
        return pending;
    }

    /// <summary>Marca caído el equipo con este ConnectionId (desconexión limpia del hub).</summary>
    public void MarkOffline(string connectionId)
    {
        string? name = null;
        lock (_gate)
        {
            var d = _devices.Values.FirstOrDefault(x => x.ConnectionId == connectionId);
            if (d == null) return;
            d.ConnectionId = null;
            d.Status = KioskStatus.Offline;
            d.Screen = KioskScreen.Off;
            name = d.Name;
        }
        if (name != null) Log($"{name} perdió la conexión");
        Changed?.Invoke();
    }

    /// <summary>Barre equipos cuyo último heartbeat caducó (caída no limpia) y los marca offline.</summary>
    public void SweepExpired()
    {
        var now = DateTime.UtcNow;
        List<string> dropped = new();
        lock (_gate)
        {
            foreach (var d in _devices.Values)
            {
                if (d.Status != KioskStatus.Offline && now - d.LastSeenUtc > OfflineAfter)
                {
                    d.ConnectionId = null;
                    d.Status = KioskStatus.Offline;
                    d.Screen = KioskScreen.Off;
                    dropped.Add(d.Name);
                }
            }
        }
        if (dropped.Count == 0) return;
        foreach (var n in dropped) Log($"{n} sin respuesta (marcado caído)");
        Changed?.Invoke();
    }

    // ── Overrides desde el panel ──────────────────────────────────────────────────────────────

    /// <summary>Renombra un equipo (override persistente). Devuelve el ConnectionId si está online, para
    /// que la página empuje el <see cref="FleetCommandKind.SetName"/> y el kiosko actualice su ajuste local.</summary>
    public string? Rename(string id, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        name = name.Trim();
        string? connId;
        lock (_gate)
        {
            var ov = GetOrCreateOverride(id);
            ov.Name = name;
            SaveOverrides();
            if (_devices.TryGetValue(id, out var d)) { d.Name = name; connId = d.ConnectionId; }
            else connId = null;
        }
        Log($"Equipo renombrado a «{name}»");
        Changed?.Invoke();
        return connId;
    }

    /// <summary>Fija el precio/oferta que el equipo debe exponer (override persistente hasta que lo aplique
    /// y lo reporte de vuelta). Devuelve el ConnectionId si está online, para empujar la orden.</summary>
    public string? SetPriceOverride(string id, decimal price, decimal oldPrice)
    {
        string? connId;
        string name;
        lock (_gate)
        {
            var ov = GetOrCreateOverride(id);
            ov.Price = price;
            ov.OldPrice = oldPrice;
            SaveOverrides();
            if (_devices.TryGetValue(id, out var d)) { connId = d.ConnectionId; name = d.Name; }
            else { connId = null; name = id; }
        }
        Log($"Precio de {name} → {price:N0} €");
        Changed?.Invoke();
        return connId;
    }

    /// <summary>Anota una publicación de contenido en el registro de actividad (la llama el panel al guardar).</summary>
    public void LogContentPublished(string what) => Log(what);

    // ── Internos ──────────────────────────────────────────────────────────────────────────────

    private DeviceOverride GetOrCreateOverride(string id)
    {
        if (!_overrides.TryGetValue(id, out var ov)) { ov = new DeviceOverride(); _overrides[id] = ov; }
        return ov;
    }

    private static KioskStatus DeriveStatus(FleetDevice d)
    {
        if (d.ConnectionId == null || DateTime.UtcNow - d.LastSeenUtc > OfflineAfter) return KioskStatus.Offline;
        return d.Screen is KioskScreen.Scan or KioskScreen.Detail ? KioskStatus.Busy : KioskStatus.Online;
    }

    private void Log(string message)
    {
        var local = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, _storeTz).DateTime;
        lock (_gate)
        {
            _activity.AddFirst(new FleetActivity(local.ToString("HH:mm"), message));
            while (_activity.Count > MaxActivity) _activity.RemoveLast();
            SaveActivity();
        }
    }

    private void LoadOverrides()
    {
        try
        {
            if (!File.Exists(_overridesPath)) return;
            var map = JsonConvert.DeserializeObject<Dictionary<string, DeviceOverride>>(File.ReadAllText(_overridesPath));
            if (map != null) foreach (var kv in map) _overrides[kv.Key] = kv.Value;
        }
        catch { /* fichero corrupto: arrancar sin overrides antes que morir */ }
    }

    private void SaveOverrides() => WriteAtomic(_overridesPath, JsonConvert.SerializeObject(_overrides, Formatting.Indented));

    private void LoadActivity()
    {
        try
        {
            if (!File.Exists(_activityPath)) return;
            var list = JsonConvert.DeserializeObject<List<FleetActivity>>(File.ReadAllText(_activityPath));
            if (list != null) foreach (var a in list) _activity.AddLast(a);
        }
        catch { /* idem */ }
    }

    private void SaveActivity() => WriteAtomic(_activityPath, JsonConvert.SerializeObject(_activity.ToList(), Formatting.Indented));

    private static void WriteAtomic(string path, string content)
    {
        string tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(tmp, content);
            File.Move(tmp, path, overwrite: true);
        }
        finally { try { if (File.Exists(tmp)) File.Delete(tmp); } catch { } }
    }

    // ── Helpers de presentación (usados por las páginas) ──────────────────────────────────────

    public static string ScreenLabel(KioskScreen s) => s switch
    {
        KioskScreen.Attract => "attract",
        KioskScreen.Scan => "scan",
        KioskScreen.Main => "ficha",
        KioskScreen.Detail => "detalle",
        _ => "off",
    };

    public static string ScreenCss(KioskScreen s) => s switch
    {
        KioskScreen.Off => "off",
        KioskScreen.Detail or KioskScreen.Scan => "busy",
        _ => "attract",
    };

    public static string StatusCss(KioskStatus s) => s switch
    {
        KioskStatus.Online => "online",
        KioskStatus.Busy => "busy",
        _ => "offline",
    };

    public static string StatusLabel(KioskStatus s) => s switch
    {
        KioskStatus.Online => "online",
        KioskStatus.Busy => "en uso",
        _ => "sin conexión",
    };
}
