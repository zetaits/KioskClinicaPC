using System;
using System.Threading;
using System.Threading.Tasks;
using KioskClinicaPC.Core.Sync;
using Microsoft.AspNetCore.SignalR.Client;
using Serilog;

namespace KioskClinicaPC.Services
{
    /// <summary>
    /// <see cref="ISyncClient"/> sobre SignalR. Hace dos cosas con el servidor:
    /// 1) Sincroniza el bucle de atracción: escucha "SyncState" del reloj maestro y calcula el mismo índice
    ///    de slide que el resto de kioscos (con corrección de desfase de reloj).
    /// 2) Avisa de cambios de contenido (<see cref="ContentChanged"/>) con doble red: PUSH inmediato por el
    ///    hub ("ContentChanged" al guardar en el panel) + POLLING lento a /api/config/version como respaldo
    ///    si el hub se cae. Así los cambios del panel llegan sin reiniciar cada kiosko.
    ///
    /// Regla de oro del kiosko: la sincronización NUNCA puede tumbarlo. Todo va con reconexión automática y
    /// sin propagar excepciones; sin <c>ServerUrl</c> queda deshabilitado (no-op).
    /// </summary>
    public sealed class SyncClient : ISyncClient, IDisposable
    {
        private static readonly TimeSpan PollPeriod = TimeSpan.FromSeconds(90); // respaldo si el push falla

        private readonly string? _hubUrl;
        private readonly string? _versionUrl;
        private readonly System.Net.Http.HttpClient? _http;
        private readonly object _gate = new();
        private HubConnection? _connection;
        private Timer? _pollTimer;
        private AttractSyncState? _state;
        private long _clockOffsetMs;   // server - local, en ms (corrige desfase de reloj)
        private bool _connected;
        private string? _lastVersion;  // última versión de contenido vista (para detectar cambios)

        /// <summary>Se dispara cuando el contenido del servidor cambió (push del hub o polling de versión).
        /// El suscriptor debe recargar la config. Puede llegar en un hilo de fondo.</summary>
        public event Action? ContentChanged;

        public SyncClient(string? serverUrl, string? apiKey)
        {
            if (string.IsNullOrWhiteSpace(serverUrl))
            {
                _hubUrl = null;
                _versionUrl = null;
                _http = null;
                return;
            }

            string baseUrl = serverUrl.TrimEnd('/');
            _hubUrl = baseUrl + "/hub/sync";
            _versionUrl = baseUrl + "/api/config/version";
            _http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            if (!string.IsNullOrWhiteSpace(apiKey))
                _http.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
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
            // PUSH: el panel guardó → recarga inmediata (force: notifica aunque el poll no lo haya visto aún).
            _connection.On("ContentChanged", () => { _ = CheckVersionAsync(force: true); });

            _connection.Reconnected += _ => { MarkConnected(true); return Task.CompletedTask; };
            _connection.Closed += _ => { MarkConnected(false); return Task.CompletedTask; };
            _connection.Reconnecting += _ => { MarkConnected(false); return Task.CompletedTask; };

            _ = ConnectLoopAsync();

            // POLLING de respaldo: fija la línea base pronto y luego comprueba la versión cada 90 s.
            _pollTimer = new Timer(_ => _ = CheckVersionAsync(force: false), null,
                TimeSpan.FromSeconds(10), PollPeriod);
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

        /// <summary>Comprueba la versión de contenido del servidor. Si cambió (o <paramref name="force"/>),
        /// dispara <see cref="ContentChanged"/>. La primera vez solo fija la línea base (sin notificar), para
        /// no recargar de más nada más arrancar.</summary>
        private async Task CheckVersionAsync(bool force)
        {
            if (_versionUrl == null || _http == null) return;
            try
            {
                string json = await _http.GetStringAsync(_versionUrl);
                string? version = null;
                using (var doc = System.Text.Json.JsonDocument.Parse(json))
                    if (doc.RootElement.TryGetProperty("version", out var el))
                        version = el.GetString();
                if (version == null) return;

                lock (_gate)
                {
                    bool baseline = _lastVersion == null;
                    bool changed = version != _lastVersion;
                    _lastVersion = version;
                    // Sin forzar: no notifiques en la primera lectura (línea base) ni si no ha cambiado.
                    if (!force && (baseline || !changed)) return;
                }

                Log.Information("Contenido del servidor actualizado (versión {Version}); recargando.", version);
                ContentChanged?.Invoke();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Sync: no se pudo comprobar la versión de contenido.");
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

        public void Dispose()
        {
            _pollTimer?.Dispose();
            _http?.Dispose();
            _ = _connection?.DisposeAsync();
        }
    }
}
