using System.Text.Json;
using Kiosk.SetupHelper;
using Xunit;

namespace Kiosk.Server.Tests;

public sealed class SetupTimingLogTests
{
    [Fact]
    public async Task Timings_record_phase_application_duration_and_outcome_without_exception_data()
    {
        string path = Path.Combine(Path.GetTempPath(), "setup-timing-" + Guid.NewGuid().ToString("N") + ".jsonl");
        try
        {
            var timings = new SetupTimingLog(path);
            await timings.Measure(SetupPhase.Preflight, "app-id", () => Task.CompletedTask, () => "Pending");
            await Assert.ThrowsAsync<IOException>(() => timings.Measure(SetupPhase.Install, "app-id", () => throw new IOException("PRIVATE DATA")));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => timings.Measure(SetupPhase.Verification, "app-id", () => throw new OperationCanceledException("PRIVATE DATA")));
            var lines = File.ReadAllLines(path); Assert.Equal(3, lines.Length);
            var outcomes = new[] { "Pending", "Failed", "Cancelled" };
            for (int i = 0; i < lines.Length; i++)
            {
                Assert.DoesNotContain("PRIVATE DATA", lines[i]);
                using var json = JsonDocument.Parse(lines[i]);
                Assert.Equal(5, json.RootElement.EnumerateObject().Count());
                Assert.Equal("app-id", json.RootElement.GetProperty("application").GetString());
                Assert.True(json.RootElement.GetProperty("durationMs").GetDouble() >= 0);
                Assert.Equal(outcomes[i], json.RootElement.GetProperty("result").GetString());
            }
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public async Task Diagnostic_write_failure_does_not_change_the_operation_result()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing", "timings.jsonl");
        Assert.Equal(42, await new SetupTimingLog(path).Measure(SetupPhase.Bootstrap, null, () => Task.FromResult(42)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new SetupTimingLog(path).Measure(SetupPhase.Bootstrap, null, () => throw new InvalidOperationException()));
    }
}
