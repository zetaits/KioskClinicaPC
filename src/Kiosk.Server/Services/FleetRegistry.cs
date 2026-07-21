namespace Kiosk.Server.Services;

/// <summary>Estado de la pantalla que muestra un kiosko ahora mismo.</summary>
public enum KioskScreen { Attract, Scan, Main, Detail, Off }

/// <summary>Conectividad del kiosko vista desde el servidor.</summary>
public enum KioskStatus { Online, Busy, Offline }

/// <summary>Un kiosko de la tienda tal como lo pinta el panel.</summary>
public sealed class FleetDevice
{
    public string Id { get; init; } = "";
    public string Name { get; set; } = "";
    public string Ip { get; init; } = "";
    public KioskStatus Status { get; init; }
    public KioskScreen Screen { get; init; }
    public string Equipment { get; init; } = "";
    public string Cpu { get; init; } = "";
    public decimal Price { get; set; }
    public decimal OldPrice { get; set; }
    public string Uptime { get; init; } = "";
    public string AppVersion { get; init; } = "";

    public bool IsOnline => Status != KioskStatus.Offline;
    public bool HasOldPrice => OldPrice > 0 && OldPrice > Price;
}

/// <summary>Una línea del registro de actividad de la tienda.</summary>
public sealed record FleetActivity(string Time, string Message);

/// <summary>
/// Inventario de kioscos de la tienda. PENDIENTE DE IMPLEMENTAR: los clientes todavía no reportan su
/// estado al servidor, así que de momento devuelve datos de muestra para que el panel tenga forma. Cuando
/// exista el canal de telemetría (SignalR/heartbeat), esta clase pasa a leer el estado real y las páginas
/// no cambian: <see cref="Devices"/>, <see cref="Rename"/> y <see cref="SetPrice"/> son toda la superficie.
/// </summary>
public sealed class FleetRegistry
{
    private readonly List<FleetDevice> _devices = new()
    {
        new() { Id = "mostrador-01",  Name = "MOSTRADOR-01",  Ip = "192.168.1.21", Status = KioskStatus.Online,  Screen = KioskScreen.Main,    Equipment = "GIGABYTE B860 DS3H",  Cpu = "Intel Core Ultra 5 245K",      Price = 1000, OldPrice = 1290, Uptime = "4d 6h", AppVersion = "2.4.1" },
        new() { Id = "mostrador-02",  Name = "MOSTRADOR-02",  Ip = "192.168.1.22", Status = KioskStatus.Online,  Screen = KioskScreen.Attract, Equipment = "ASUS ROG Strix G16",  Cpu = "Ryzen 9 7945HX · RTX 4070",    Price = 1799, OldPrice = 0,    Uptime = "2d 1h", AppVersion = "2.4.1" },
        new() { Id = "escaparate-01", Name = "ESCAPARATE-01", Ip = "192.168.1.23", Status = KioskStatus.Online,  Screen = KioskScreen.Scan,    Equipment = "MSI Katana 15",       Cpu = "Intel i7-13620H · RTX 4060",   Price = 999,  OldPrice = 1199, Uptime = "6h",    AppVersion = "2.4.1" },
        new() { Id = "mostrador-03",  Name = "MOSTRADOR-03",  Ip = "192.168.1.24", Status = KioskStatus.Online,  Screen = KioskScreen.Main,    Equipment = "Acer Nitro V 15",     Cpu = "i5-13420H · RTX 4050",         Price = 899,  OldPrice = 0,    Uptime = "1d 3h", AppVersion = "2.4.1" },
        new() { Id = "vitrina-01",    Name = "VITRINA-01",    Ip = "192.168.1.25", Status = KioskStatus.Busy,    Screen = KioskScreen.Detail,  Equipment = "HP Victus 16",        Cpu = "Ryzen 7 8845HS",               Price = 1099, OldPrice = 1299, Uptime = "12h",   AppVersion = "2.4.0 ↻" },
        new() { Id = "taller-01",     Name = "TALLER-01",     Ip = "192.168.1.26", Status = KioskStatus.Offline, Screen = KioskScreen.Off,     Equipment = "Lenovo LOQ 15",       Cpu = "— sin conexión",               Price = 0,    OldPrice = 0,    Uptime = "hace 2 h", AppVersion = "2.3.9" },
    };

    public IReadOnlyList<FleetDevice> Devices => _devices;

    public int OnlineCount => _devices.Count(d => d.IsOnline);
    public int OfflineCount => _devices.Count(d => !d.IsOnline);

    /// <summary>Media del precio expuesto, contando solo los equipos que muestran alguno.</summary>
    public decimal AveragePrice
    {
        get
        {
            var priced = _devices.Where(d => d.Price > 0).ToList();
            return priced.Count == 0 ? 0 : Math.Round(priced.Average(d => d.Price));
        }
    }

    public FleetDevice? Find(string id) => _devices.FirstOrDefault(d => d.Id == id);

    public void Rename(string id, string name)
    {
        var d = Find(id);
        if (d != null && !string.IsNullOrWhiteSpace(name)) d.Name = name.Trim();
    }

    public void SetPrice(string id, decimal price, decimal oldPrice)
    {
        var d = Find(id);
        if (d == null) return;
        d.Price = price;
        d.OldPrice = oldPrice;
    }

    /// <summary>Actividad reciente de la tienda. Muestra, igual que <see cref="Devices"/>.</summary>
    public IReadOnlyList<FleetActivity> RecentActivity() => new List<FleetActivity>
    {
        new("16:41", "MOSTRADOR-01 → pantalla FICHA"),
        new("16:39", "Contenido sincronizado · v48"),
        new("16:32", "Evento «Rebajas de verano» activo"),
        new("16:20", "TALLER-01 perdió la conexión"),
        new("15:58", "Precio · MOSTRADOR-03 · 899 €"),
        new("15:44", "MSI Katana detectado en ESCAPARATE-01"),
    };

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
