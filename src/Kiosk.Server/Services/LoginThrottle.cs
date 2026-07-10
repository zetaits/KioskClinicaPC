using System.Collections.Concurrent;

namespace Kiosk.Server.Services;

/// <summary>
/// Freno simple contra fuerza bruta en el login del panel: cuenta fallos por IP y bloquea temporalmente
/// tras varios intentos seguidos. En memoria (un solo proceso, un solo encargado). Nota: tras un proxy
/// inverso hay que propagar la IP real (ForwardedHeaders); si no, todos comparten la IP del proxy y el
/// bloqueo es global — aceptable para un único operador, pero a tener en cuenta en F5.
/// </summary>
public sealed class LoginThrottle
{
    private const int MaxFails = 5;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan Lockout = TimeSpan.FromMinutes(15);

    private sealed class Entry
    {
        public int Count;
        public DateTime WindowStart;
        public DateTime? LockedUntil;
    }

    private readonly ConcurrentDictionary<string, Entry> _byIp = new();

    /// <summary>true si la IP está bloqueada ahora mismo. Limpia el registro si el bloqueo ya expiró.</summary>
    public bool IsLocked(string ip)
    {
        if (!_byIp.TryGetValue(ip, out var e)) return false;
        lock (e)
        {
            if (e.LockedUntil is { } until)
            {
                if (DateTime.UtcNow < until) return true;
                _byIp.TryRemove(ip, out _); // expiró: borrón y cuenta nueva
            }
            return false;
        }
    }

    /// <summary>Registra un intento fallido; al llegar al máximo dentro de la ventana, bloquea.</summary>
    public void RecordFailure(string ip)
    {
        var now = DateTime.UtcNow;
        var e = _byIp.GetOrAdd(ip, _ => new Entry { WindowStart = now });
        lock (e)
        {
            if (now - e.WindowStart > Window) { e.Count = 0; e.WindowStart = now; e.LockedUntil = null; }
            e.Count++;
            if (e.Count >= MaxFails) e.LockedUntil = now.Add(Lockout);
        }
    }

    /// <summary>Login correcto: olvida los fallos de esa IP.</summary>
    public void RecordSuccess(string ip) => _byIp.TryRemove(ip, out _);
}
