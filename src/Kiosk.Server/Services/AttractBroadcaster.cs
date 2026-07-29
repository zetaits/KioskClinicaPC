using Kiosk.Server.Hubs;
using KioskClinicaPC.Core.Sync;
using Microsoft.AspNetCore.SignalR;

namespace Kiosk.Server.Services
{
    /// <summary>
    /// Latido periódico: reenvía el estado del reloj maestro a todos los clientes conectados. No cambia
    /// el slide (eso lo calcula cada cliente por su cuenta), solo sirve de red de seguridad — corrige el
    /// desfase de reloj de clientes de larga duración y garantiza que, tras un reinicio del servidor,
    /// todos adopten el nuevo origen aunque no hayan reconectado.
    ///
    /// De paso, en cada tick barre la flota (<see cref="FleetRegistry.SweepExpired"/>) para marcar caídos
    /// los kioscos cuyo heartbeat caducó sin desconexión limpia.
    /// </summary>
    public sealed class AttractBroadcaster : BackgroundService
    {
        private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

        private readonly IHubContext<SyncHub> _hub;
        private readonly AttractClock _clock;
        private readonly FleetRegistry _fleet;

        public AttractBroadcaster(IHubContext<SyncHub> hub, AttractClock clock, FleetRegistry fleet)
        {
            _hub = hub;
            _clock = clock;
            _fleet = fleet;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(Interval);
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await _hub.Clients.All.SendAsync("SyncState", _clock.CurrentState(), stoppingToken);
                _fleet.SweepExpired();
            }
        }
    }
}
