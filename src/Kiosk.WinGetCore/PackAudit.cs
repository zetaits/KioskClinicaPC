using KioskClinicaPC.Core.Sync;
using KioskClinicaPC.Equipment;

namespace Kiosk.SetupHelper;

/// <summary>Replays the saved selection and resume guards against the real COM API.
/// Installation and persistence are deliberately unavailable in this diagnostic.</summary>
internal static class PackAudit
{
    private sealed class ReadOnlyBackend(WinGetEngine engine) : IPackBackend
    {
        public Task Preflight(PackItemResult item, string log) => engine.Preflight(item, "");
        public Task Install(PackItemResult item, string log, Action<string> progress, CancellationToken ct)
            => throw new InvalidOperationException("El diagnóstico no permite instalar aplicaciones.");
    }

    public static async Task<object> LastRun(CancellationToken ct)
    {
        var previous = PackState.ReadLastRun() ?? throw new InvalidDataException("No hay una ejecución anterior para comprobar.");
        var snapshot = new PackCatalog(previous.CatalogRevision, previous.Items.Select(i => i.Application with { }).ToList(), previous.Definition);
        var run = PackResumePolicy.Create(snapshot, previous, resume: true);
        bool ready = await PackExecution.Run(run, new ReadOnlyBackend(new WinGetEngine()), "", null,
            _ => throw new InvalidOperationException("El diagnóstico no permite guardar estados."), preflight: true, ct);
        return new { readOnly = true, preflightReady = ready, blockedByNativeState = run.Items.Any(i => i.RequiresRebootBeforeRetry), run };
    }
}
