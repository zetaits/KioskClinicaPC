namespace KioskClinicaPC.Core.Sync
{
    /// <summary>
    /// Pantalla que muestra un kiosko ahora mismo. Viaja en el <see cref="KioskHeartbeat"/> para que el
    /// panel sepa qué está haciendo cada equipo. El cliente WPF mapea su índice interno de pantalla
    /// (0 attract, 1 scan, 2 main, 3 detalle) a estos valores; <see cref="Off"/> lo pone el servidor
    /// cuando el equipo pierde la conexión (no lo reporta el cliente).
    /// </summary>
    public enum KioskScreen
    {
        Attract,
        Scan,
        Main,
        Detail,
        Off
    }
}
