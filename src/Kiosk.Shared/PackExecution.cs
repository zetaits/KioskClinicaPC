namespace KioskClinicaPC.Core.Sync;

public interface IPackBackend
{
    Task Preflight(PackItemResult item, string log);
    Task Install(PackItemResult item, string log, Action<string> progress, CancellationToken ct);
}

/// <summary>Checks the entire immutable selection before installing. Never retries an ambiguous native install.</summary>
public static class PackExecution
{
    public static async Task<bool> Run(PackRun run, IPackBackend engine, string logs,
        Action<PackRun>? changed, Action<PackRun> save, bool preflight, CancellationToken ct)
    {
        bool blocked = false;
        foreach (var item in run.Items)
        {
            ct.ThrowIfCancellationRequested();
            bool uncertain = item.RequiresRebootBeforeRetry || item.State is PackItemState.Installing or PackItemState.VerificationPending;
            if (uncertain) item.RequiresRebootBeforeRetry = true;
            item.State = PackItemState.Checking; changed?.Invoke(run);
            try
            {
                await engine.Preflight(item, Path.Combine(logs, item.Application.Id + ".log")).WaitAsync(TimeSpan.FromMinutes(5), ct);
                var currentBoot = DateTime.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64);
                if (uncertain && !item.Verified && Math.Abs((currentBoot - (item.LastAttemptBootTimeUtc ?? run.HostBootTimeUtc)).TotalMinutes) < 1)
                    throw new InvalidOperationException("Instalación anterior interrumpida: puede seguir activa. Reinicia manualmente y reabre el instalador antes de reintentar.");
                item.RequiresRebootBeforeRetry = false;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { blocked = true; item.State = PackItemState.Failed; item.Message = ex.Message; }
            changed?.Invoke(run);
        }
        if (!preflight) save(run);
        if (blocked || preflight) return !blocked;
        foreach (var item in run.Items.Where(x => !x.Verified))
        {
            if (ct.IsCancellationRequested) break;
            item.State = PackItemState.Installing; changed?.Invoke(run); save(run);
            item.LastAttemptBootTimeUtc = DateTime.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64);
            save(run);
            try { await engine.Install(item, Path.Combine(logs, item.Application.Id + ".log"), message => { item.Message = message; changed?.Invoke(run); }, ct); }
            catch (OperationCanceledException)
            {
                item.State = PackItemState.VerificationPending;
                item.RequiresRebootBeforeRetry = true;
                item.Message = "Cancelado o tiempo agotado. El instalador nativo puede seguir activo; la cola se detiene. No reintentar hasta comprobarlo.";
                save(run); changed?.Invoke(run); break;
            }
            catch (Exception ex) { item.State = PackItemState.Failed; item.Message = ex.Message; }
            save(run); changed?.Invoke(run);
        }
        return run.Complete;
    }
}
