using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace Kiosk.SetupHelper;

internal enum SetupPhase { Bootstrap, CatalogRefresh, Preflight, Install, Verification }

/// <summary>Local timings only: no exception text, HTTP data or credentials.</summary>
internal sealed class SetupTimingLog(string path)
{
    private readonly object _gate = new();
    internal Task Measure(SetupPhase phase, string? app, Func<Task> action, Func<string>? outcome = null)
        => Measure(phase, app, async () => { await action(); return true; }, outcome);

    internal async Task<T> Measure<T>(SetupPhase phase, string? app, Func<Task<T>> action, Func<string>? outcome = null)
    {
        long start = Stopwatch.GetTimestamp();
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
            try
            {
                var line = JsonSerializer.Serialize(new { atUtc = DateTime.UtcNow, phase = phase.ToString(), application = app,
                    durationMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds, result });
                lock (_gate) File.AppendAllText(path, line + Environment.NewLine);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* Diagnostics cannot change installation results. */ }
        }
    }
}
