
namespace Kiosk.SetupHelper;

internal static class Bootstrap
{
    public static Task<WinGetEngine> CreateEngine(Action<string> status, CancellationToken ct, SetupTimingLog? timings = null)
        => timings is null ? CreateEngineCore(status, ct, null) : timings.Measure(SetupPhase.Bootstrap, null, () => CreateEngineCore(status, ct, timings));

    private static async Task<WinGetEngine> CreateEngineCore(Action<string> status, CancellationToken ct, SetupTimingLog? timings)
    {
        ct.ThrowIfCancellationRequested();
        try { var engine = new WinGetEngine(timings: timings); engine.RequireSupportedRuntime(); return engine; }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or TypeInitializationException or InvalidCastException)
        {
            status("Preparando WinGet oficial; esta operación puede tardar varios minutos…");
            status("Preparando WinGet. Si cancelas, se esperará a que esta preparación termine.");
            await WinGetPreparation.Run(ct);
            var repaired = new WinGetEngine(timings: timings); repaired.RequireSupportedRuntime(); return repaired;
        }
    }
}
