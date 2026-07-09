using Kiosk.Server.Services;
using KioskClinicaPC.Core.Sync;
using Microsoft.AspNetCore.SignalR;

namespace Kiosk.Server.Hubs
{
    /// <summary>
    /// Hub de sincronización del bucle de atracción. Cuando un kiosko conecta (o reconecta), recibe el
    /// estado del reloj maestro de inmediato para caer en el slide correcto sin esperar. El latido
    /// periódico (<see cref="AttractBroadcaster"/>) reenvía el estado por si el origen cambió (reinicio
    /// del servidor) o para corregir el desfase de reloj de clientes de larga duración.
    ///
    /// No lleva contenido sensible (solo origen + duración del cronómetro), por eso queda fuera de la
    /// guardia X-Api-Key de /api/*. El cliente que emite es <c>SyncClient</c> escuchando "SyncState".
    /// </summary>
    public sealed class SyncHub : Hub
    {
        private readonly AttractClock _clock;

        public SyncHub(AttractClock clock) => _clock = clock;

        public override async Task OnConnectedAsync()
        {
            await Clients.Caller.SendAsync("SyncState", _clock.CurrentState());
            await base.OnConnectedAsync();
        }

        /// <summary>Permite al cliente pedir el estado explícitamente (p.ej. al volver a idle).</summary>
        public AttractSyncState GetState() => _clock.CurrentState();
    }
}
