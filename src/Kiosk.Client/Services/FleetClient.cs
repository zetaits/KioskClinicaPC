using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using KioskClinicaPC.Core;
using KioskClinicaPC.Core.Config;
using KioskClinicaPC.Core.Sync;
using Microsoft.AspNetCore.SignalR.Client;
using Serilog;

namespace KioskClinicaPC.Services
{
    /// <summary>
    /// Agente de flota del kiosko sobre SignalR (<c>/hub/fleet</c>). Hace dos cosas:
    /// 1) REPORTA: se registra al conectar y manda un heartbeat periódico con el estado del equipo
    ///    (pantalla, precio, hardware, versión, uptime) para que el panel lo pinte en vivo.
    /// 2) OBEDECE: escucha órdenes del panel en el evento "Command" (reiniciar/apagar/reiniciar app,
    ///    fijar precio, renombrar) y las ejecuta.
    ///
    /// Misma regla de oro que <see cref="SyncClient"/>: la conexión NUNCA puede tumbar el kiosko. Todo va
    /// con reconexión automática y sin propagar excepciones; sin <c>ServerUrl</c> queda deshabilitado
    /// (no-op). El canal exige X-Api-Key en el handshake (el hub la valida).
    /// </summary>
    public sealed class FleetClient : IDisposable
    {
        private static readonly TimeSpan HeartbeatPeriod = TimeSpan.FromSeconds(20);

        private readonly string? _hubUrl;
        private readonly string? _apiKey;
        private readonly string _deviceId;
        private readonly string _settingsPath;
        private readonly object _gate = new();
        private readonly long _startedAtUnixMs;

        private string _deviceName;
        private Func<KioskHeartbeat>? _snapshot;
        private HubConnection? _connection;
        private Timer? _heartbeatTimer;
        private volatile bool _disposed;

        /// <summary>El panel fijó un precio para este equipo. Aplícalo y persístelo (hilo de UI).</summary>
        public event Action<decimal, decimal>? PriceOverrideReceived;

        /// <summary>El panel pidió reiniciar la app kiosko (hilo de UI: relanzar + cerrar).</summary>
        public event Action? RestartAppRequested;

        public FleetClient(string? serverUrl, string? apiKey, string deviceId, string deviceName, string settingsPath)
        {
            _apiKey = apiKey;
            _deviceId = deviceId;
            _deviceName = deviceName;
            _settingsPath = settingsPath;
            _startedAtUnixMs = new DateTimeOffset(Process.GetCurrentProcess().StartTime.ToUniversalTime()).ToUnixTimeMilliseconds();
            _hubUrl = string.IsNullOrWhiteSpace(serverUrl) ? null : serverUrl.TrimEnd('/') + "/hub/fleet";
        }

        /// <summary>Fija la fuente del estado dinámico del kiosko (pantalla/precio/hardware). La rellena el
        /// ViewModel; la identidad (Id/nombre/arranque) la pone este cliente.</summary>
        public void SetSnapshotProvider(Func<KioskHeartbeat> snapshot) => _snapshot = snapshot;

        public void Start()
        {
            if (_hubUrl == null) return; // modo local puro: sin flota

            _connection = new HubConnectionBuilder()
                .WithUrl(_hubUrl, options =>
                {
                    if (!string.IsNullOrWhiteSpace(_apiKey)) options.Headers.Add("X-Api-Key", _apiKey!);
                })
                .WithAutomaticReconnect()
                .Build();

            _connection.On<FleetCommand>("Command", HandleCommand);
            // Al reconectar, re-registrar de inmediato (el servidor pudo reiniciar y perder el estado).
            _connection.Reconnected += async _ => await SafeSend("Register");
            _connection.Closed += async _ =>
            {
                if (_disposed) return;
                await Task.Delay(TimeSpan.FromSeconds(5));
                if (!_disposed) await ConnectLoopAsync();
            };

            _ = ConnectLoopAsync();
            _heartbeatTimer = new Timer(_ => _ = SafeSend("Heartbeat"), null, HeartbeatPeriod, HeartbeatPeriod);
        }

        private async Task ConnectLoopAsync()
        {
            while (!_disposed)
            {
                try
                {
                    await _connection!.StartAsync();
                    await SafeSend("Register");
                    Log.Information("Flota: registrado en el servidor ({Url}).", _hubUrl);
                    return; // WithAutomaticReconnect gestiona las caídas posteriores
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Flota: servidor no disponible; reintentando en 10 s.");
                    await Task.Delay(TimeSpan.FromSeconds(10));
                }
            }
        }

        /// <summary>Invoca un método del hub (Register/Heartbeat) con el heartbeat actual, sin lanzar.</summary>
        private async Task SafeSend(string method)
        {
            var conn = _connection;
            if (conn == null || conn.State != HubConnectionState.Connected) return;
            try
            {
                await conn.InvokeAsync(method, BuildHeartbeat());
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Flota: no se pudo enviar {Method}.", method);
            }
        }

        private KioskHeartbeat BuildHeartbeat()
        {
            var hb = _snapshot?.Invoke() ?? new KioskHeartbeat();
            lock (_gate)
            {
                hb.DeviceId = _deviceId;
                hb.Name = _deviceName;
            }
            hb.StartedAtUnixMs = _startedAtUnixMs;
            return hb;
        }

        private void HandleCommand(FleetCommand cmd)
        {
            try
            {
                switch (cmd.Kind)
                {
                    case FleetCommandKind.Reboot:
                        Log.Information("Flota: orden de reinicio recibida.");
                        SystemPower.Reboot();
                        break;
                    case FleetCommandKind.Shutdown:
                        Log.Information("Flota: orden de apagado recibida.");
                        SystemPower.Shutdown();
                        break;
                    case FleetCommandKind.RestartApp:
                        Log.Information("Flota: orden de reinicio de la app recibida.");
                        RestartAppRequested?.Invoke();
                        break;
                    case FleetCommandKind.SetPrice:
                        Log.Information("Flota: precio remoto recibido ({Price}/{Old}).", cmd.Price, cmd.OldPrice);
                        PriceOverrideReceived?.Invoke(cmd.Price ?? 0, cmd.OldPrice ?? 0);
                        break;
                    case FleetCommandKind.SetName:
                        ApplyName(cmd.Name);
                        break;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Flota: error al ejecutar la orden {Kind}.", cmd.Kind);
            }
        }

        // Renombrado remoto: persiste en KioskSettings (releyendo del disco para no pisar otros ajustes)
        // y actualiza el nombre que reporta el heartbeat.
        private void ApplyName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            name = name.Trim();
            lock (_gate) _deviceName = name;
            try
            {
                var s = KioskSettings.Load(_settingsPath);
                s.DeviceName = name;
                s.Save(_settingsPath);
                Log.Information("Flota: equipo renombrado a «{Name}» por el panel.", name);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Flota: no se pudo persistir el nuevo nombre.");
            }
            _ = SafeSend("Heartbeat"); // reporta el nombre nuevo sin esperar al siguiente latido
        }

        public void Dispose()
        {
            _disposed = true;
            _heartbeatTimer?.Dispose();
            _ = _connection?.DisposeAsync();
        }
    }
}
