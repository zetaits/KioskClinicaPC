using KioskClinicaPC.Core.Sync;

namespace Kiosk.Server.Services
{
    /// <summary>
    /// Reloj maestro del bucle de atracción. Fija un origen (<see cref="_epochUnixMs"/>) al arrancar el
    /// servidor y una duración de slide; a partir de ahí todos los clientes calculan el mismo índice con
    /// el mismo origen, sin que el servidor tenga que empujar cada cambio de slide. Si el servidor
    /// reinicia, el origen cambia y los clientes se realinean al recibir el nuevo estado.
    /// </summary>
    public sealed class AttractClock
    {
        private readonly long _epochUnixMs;
        private readonly int _slideDurationMs;

        public AttractClock(int slideDurationMs)
        {
            _epochUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _slideDurationMs = slideDurationMs > 0 ? slideDurationMs : 5200; // igual que el default del cliente
        }

        /// <summary>Estado a emitir a los clientes (origen, duración y hora actual del servidor).</summary>
        public AttractSyncState CurrentState() => new AttractSyncState
        {
            EpochUnixMs = _epochUnixMs,
            SlideDurationMs = _slideDurationMs,
            ServerTimeUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
    }
}
