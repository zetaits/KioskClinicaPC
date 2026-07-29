using Kiosk.Server.Services;
using KioskClinicaPC.Core.Sync;
using Microsoft.AspNetCore.SignalR;

namespace Kiosk.Server.Hubs
{
    /// <summary>
    /// Hub de CONTROL de la flota. Cada kiosko se conecta aquí, se registra y manda heartbeats con su
    /// estado (<see cref="FleetRegistry"/>); el panel envía órdenes dirigidas (reiniciar/apagar/reiniciar
    /// app/precio/nombre) a través de <c>IHubContext&lt;FleetHub&gt;</c>, que el cliente recibe en el evento
    /// "Command".
    ///
    /// A diferencia de <see cref="SyncHub"/> (anónimo, solo cronómetro), este canal SÍ mueve control
    /// sensible, por eso exige la misma X-Api-Key que <c>/api/*</c>: se valida en el handshake y se aborta
    /// la conexión si no coincide (cuando hay clave configurada).
    /// </summary>
    public sealed class FleetHub : Hub
    {
        private readonly FleetRegistry _registry;
        private readonly string? _apiKey;

        public FleetHub(FleetRegistry registry, IConfiguration config)
        {
            _registry = registry;
            _apiKey = config["Kiosk:ApiKey"];
        }

        public override Task OnConnectedAsync()
        {
            // Servidor abierto (sin clave) = sin guardia, igual que /api/*. Con clave, exige coincidencia.
            if (!string.IsNullOrEmpty(_apiKey))
            {
                var http = Context.GetHttpContext();
                if (http == null || http.Request.Headers["X-Api-Key"] != _apiKey)
                {
                    Context.Abort();
                    return Task.CompletedTask;
                }
            }
            return base.OnConnectedAsync();
        }

        /// <summary>Alta del kiosko al conectar. Igual que <see cref="Heartbeat"/> pero semánticamente el
        /// primer contacto.</summary>
        public Task Register(KioskHeartbeat hb) => Ingest(hb);

        /// <summary>Latido periódico con el estado actual del kiosko.</summary>
        public Task Heartbeat(KioskHeartbeat hb) => Ingest(hb);

        private async Task Ingest(KioskHeartbeat hb)
        {
            string ip = Context.GetHttpContext()?.Connection.RemoteIpAddress?.ToString() ?? "";
            var pending = _registry.Upsert(Context.ConnectionId, ip, hb);
            // Reenvía al equipo los overrides del panel que aún no reflejaba (renombrado/precio offline).
            foreach (var cmd in pending)
                await Clients.Caller.SendAsync("Command", cmd);
        }

        public override Task OnDisconnectedAsync(Exception? exception)
        {
            _registry.MarkOffline(Context.ConnectionId);
            return base.OnDisconnectedAsync(exception);
        }
    }
}
