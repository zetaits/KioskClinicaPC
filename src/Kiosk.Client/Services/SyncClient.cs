using System;
using System.Threading.Tasks;
using KioskClinicaPC.Core.Sync;
using Microsoft.AspNetCore.SignalR.Client;
using Serilog;

namespace KioskClinicaPC.Services
{
    /// <summary>
    /// <see cref="ISyncClient"/> sobre SignalR. Escucha el evento "SyncState" del hub y guarda el estado
    /// del reloj maestro más un desfase de reloj (server - local) para que este kiosko calcule el mismo
    /// índice de slide que el resto aunque su reloj no esté perfectamente en hora.
    ///
    /// Regla de oro del kiosko: la sincronización NUNCA puede tumbarlo. Todo va con reconexión automática
    /// y sin propagar excepciones; si el hub no está, <see cref="IsSynced"/> es false y el kiosko rota los
    /// slides localmente como siempre. Sin <c>ServerUrl</c>, el cliente queda deshabilitado (no-op).
    /// </summary>
    public sealed class SyncClient : ISyncClient
    {
        private readonly string? _hubUrl;
        private readonly object _gate = new();
        private HubConnection? _connection;
        private AttractSyncState? _state;
        private long _clockOffsetMs;   // server - local, en ms (corrige desfase de reloj)
        private bool _connected;

        public SyncClient(string? serverUrl)
        {
            _hubUrl = string.IsNullOrWhiteSpace(serverUrl) ? null : serverUrl.TrimEnd('/') + "/hub/sync";
        }

        public bool IsSynced
        {
            get { lock (_gate) return _connected && _state != null; }
        }

        public void Start()
        {
            if (_hubUrl == null) return; // modo local puro: sin sincronización

            _connection = new HubConnectionBuilder()
                .WithUrl(_hubUrl)
                .WithAutomaticReconnect()
                .Build();

            _connection.On<AttractSyncState>("SyncState", OnState);

            _connection.Reconnected += _ => { MarkConnected(true); return Task.CompletedTask; };
            _connection.Closed += _ => { MarkConnected(false); return Task.CompletedTask; };
            _connection.Reconnecting += _ => { MarkConnected(false); return Task.CompletedTask; };

            _ = ConnectLoopAsync();
        }

        /// <summary>Intenta conectar y reintenta indefinidamente si el servidor aún no está. No bloquea
        /// el arranque del kiosko ni lanza.</summary>
        private async Task ConnectLoopAsync()
        {
            while (true)
            {
                try
                {
                    await _connection!.StartAsync();
                    MarkConnected(true);
                    Log.Information("Sync: conectado al reloj maestro ({Url}).", _hubUrl);
                    return; // WithAutomaticReconnect se ocupa de las caídas posteriores
                }
                catch (Exception ex)
                {
                    MarkConnected(false);
                    Log.Warning(ex, "Sync: servidor no disponible; reintentando en 10 s.");
                    await Task.Delay(TimeSpan.FromSeconds(10));
                }
            }
        }

        private void OnState(AttractSyncState state)
        {
            long localNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            lock (_gate)
            {
                _state = state;
                _clockOffsetMs = state.ServerTimeUnixMs - localNow;
                _connected = true;
            }
        }

        private void MarkConnected(bool value)
        {
            lock (_gate) _connected = value;
        }

        public bool TryGetSlideIndex(int slideCount, out int index)
        {
            index = 0;
            lock (_gate)
            {
                if (!_connected || _state == null) return false;
                long correctedNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + _clockOffsetMs;
                index = _state.SlideIndexAt(correctedNow, slideCount);
                return true;
            }
        }
    }
}
