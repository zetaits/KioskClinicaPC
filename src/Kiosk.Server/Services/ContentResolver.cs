using System.Security.Cryptography;
using System.Text;
using KioskClinicaPC.Core.Config;
using Newtonsoft.Json;

namespace Kiosk.Server.Services;

/// <summary>
/// Resuelve el contenido EFECTIVO que reciben los kioscos: config base compartida + overrides del evento
/// vigente (si lo hay). Único punto que combina <see cref="ServerConfigStore"/> y <see cref="EventStore"/>.
/// El tiempo se evalúa en la hora LOCAL de la tienda (<paramref name="storeTz"/>), no la del servidor, para
/// que el rango de fechas del evento cuadre con lo que el encargado programó.
/// </summary>
public sealed class ContentResolver
{
    private readonly ServerConfigStore _config;
    private readonly EventStore _events;
    private readonly TimeZoneInfo _storeTz;

    public ContentResolver(ServerConfigStore config, EventStore events, TimeZoneInfo storeTz)
    {
        _config = config;
        _events = events;
        _storeTz = storeTz;
    }

    /// <summary>Hora actual en la zona de la tienda.</summary>
    public DateTime NowLocal => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, _storeTz).DateTime;

    /// <summary>Evento vigente ahora mismo (o null).</summary>
    public KioskEvent? ActiveEvent() => EventContent.ActiveAt(_events.All(), NowLocal);

    /// <summary>Config base + evento activo aplicado.</summary>
    public AppConfig Effective()
    {
        var cfg = _config.Read();
        var ev = ActiveEvent();
        if (ev != null) EventContent.Apply(cfg, ev);
        return cfg;
    }

    /// <summary>JSON (PascalCase) del contenido efectivo, que consumen los clientes.</summary>
    public string EffectiveJson() => JsonConvert.SerializeObject(Effective(), Formatting.Indented);

    /// <summary>Hash corto del contenido efectivo. Cambia al entrar/salir un evento, así el cliente
    /// (polling futuro sobre /api/config/version) detecta la transición sin re-parsear el cuerpo.</summary>
    public string Version()
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(EffectiveJson()));
        return Convert.ToHexString(hash, 0, 8);
    }
}
