using Kiosk.Server.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Kiosk.Server.Services;

/// <summary>
/// Publica el cambio de contenido justo al cruzar un inicio/fin. El polling del cliente sigue siendo
/// respaldo, pero deja de ser el mecanismo normal de activación de campañas.
/// </summary>
public sealed class EventTransitionBroadcaster : BackgroundService
{
    private static readonly TimeSpan MaxClockCheck = TimeSpan.FromMinutes(1);
    private readonly EventStore _events;
    private readonly ContentResolver _content;
    private readonly IHubContext<SyncHub> _hub;
    private readonly ILogger<EventTransitionBroadcaster> _log;
    private readonly SemaphoreSlim _changed = new(0, 1);

    public EventTransitionBroadcaster(EventStore events, ContentResolver content,
        IHubContext<SyncHub> hub, ILogger<EventTransitionBroadcaster> log)
    {
        _events = events;
        _content = content;
        _hub = hub;
        _log = log;
        _events.Changed += Wake;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string version = _content.Version();
        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan delay = DelayUntilNextBoundary();
            try
            {
                using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                Task timer = Task.Delay(delay, waitCts.Token);
                Task wake = _changed.WaitAsync(waitCts.Token);
                await Task.WhenAny(timer, wake);
                await waitCts.CancelAsync();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            string next = _content.Version();
            if (next == version) continue;
            version = next;
            await _hub.Clients.All.SendAsync("ContentChanged", stoppingToken);
            _log.LogInformation("Transición de evento publicada. Evento activo: {Event}",
                _content.ActiveEvent()?.Name ?? "ninguno");
        }
    }

    private TimeSpan DelayUntilNextBoundary()
    {
        DateTime utcNow = DateTime.UtcNow;
        DateTime? next = null;
        foreach (var ev in _events.All().Where(x => x.Enabled))
        {
            Consider(ev.Start);
            Consider(ev.End);
        }
        if (next == null) return MaxClockCheck;
        TimeSpan delay = next.Value - utcNow;
        if (delay <= TimeSpan.Zero) return TimeSpan.FromMilliseconds(100);
        return delay < MaxClockCheck ? delay + TimeSpan.FromMilliseconds(100) : MaxClockCheck;

        void Consider(DateTime local)
        {
            try
            {
                local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
                if (_content.StoreTz.IsInvalidTime(local) || _content.StoreTz.IsAmbiguousTime(local)) return;
                DateTime utc = TimeZoneInfo.ConvertTimeToUtc(local, _content.StoreTz);
                if (utc > utcNow && (next == null || utc < next)) next = utc;
            }
            catch (ArgumentException) { }
        }
    }

    private void Wake()
    {
        try { _changed.Release(); }
        catch (SemaphoreFullException) { }
        catch (ObjectDisposedException) { }
    }

    public override void Dispose()
    {
        _events.Changed -= Wake;
        _changed.Dispose();
        base.Dispose();
    }
}
