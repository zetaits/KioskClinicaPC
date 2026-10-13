using System.Diagnostics;
using System.IO;
using System.Text.Json;
using KioskClinicaPC.Core.Sync;

namespace Kiosk.SetupHelper;

internal enum SetupPhase { Bootstrap, CatalogRefresh, Preflight, Install, Verification, Queued, Download, NativeInstall, PostInstall, PackExecution, PackTotal }

/// <summary>Local timings only: no exception text, HTTP data or credentials.</summary>
internal sealed class SetupTimingLog(string path)
{
    private readonly object _gate = new();
    internal Task Measure(SetupPhase phase, string? app, Func<Task> action, Func<string>? outcome = null)
        => Measure(phase, app, async () => { await action(); return true; }, outcome);

    internal async Task<T> Measure<T>(SetupPhase phase, string? app, Func<Task<T>> action, Func<string>? outcome = null)
    {
        long start = Stopwatch.GetTimestamp();
        DateTime startedAtUtc = DateTime.UtcNow;
        string result = "Failed";
        try
        {
            var value = await action();
            result = outcome?.Invoke() ?? "Succeeded";
            return value;
        }
        catch (OperationCanceledException) { result = "Cancelled"; throw; }
        finally
        {
            Record(phase, app, startedAtUtc, Stopwatch.GetElapsedTime(start), result);
        }
    }
    internal void Record(SetupPhase phase, string? app, DateTime startedAtUtc, TimeSpan duration, string result)
    {
        try
        {
            var line = JsonSerializer.Serialize(new { atUtc = DateTime.UtcNow, startedAtUtc, phase = phase.ToString(), application = app,
                durationMs = duration.TotalMilliseconds, result });
            lock (_gate) File.AppendAllText(path, line + Environment.NewLine);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* Diagnostics cannot change installation results. */ }
    }
    internal ActivityTiming Track(string app) => new(this, app);
    internal sealed class ActivityTiming(SetupTimingLog log, string app) : IDisposable
    {
        private readonly object _gate = new();
        private PackItemPhase _phase = PackItemPhase.Queued;
        private long _start = Stopwatch.GetTimestamp();
        private DateTime _startedAtUtc = DateTime.UtcNow;
        private bool _completed;
        private void Record(string result) => log.Record(_phase switch
        {
            PackItemPhase.Downloading => SetupPhase.Download, PackItemPhase.Installing => SetupPhase.NativeInstall,
            PackItemPhase.PostInstall => SetupPhase.PostInstall, _ => SetupPhase.Queued
        }, app, _startedAtUtc, Stopwatch.GetElapsedTime(_start), result);
        internal void Move(PackItemPhase phase)
        {
            lock (_gate)
            {
                if (_completed || phase <= _phase) return;
                Record("PhaseChanged"); _phase = phase; _start = Stopwatch.GetTimestamp(); _startedAtUtc = DateTime.UtcNow;
            }
        }
        internal void Complete(string result)
        {
            lock (_gate) { if (_completed) return; _completed = true; Record(result); }
        }
        public void Dispose() => Complete("Uncertain");
    }
}
