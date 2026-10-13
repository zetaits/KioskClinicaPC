namespace KioskClinicaPC.Core.Sync;

public interface IPackBackend
{
    Task Preflight(PackItemResult item, string log, CancellationToken ct);
    Task Install(PackItemResult item, string log, Action<string> progress, CancellationToken ct);
}

/// <summary>Checks the entire immutable selection before installing. Never retries an ambiguous native install.</summary>
public static class PackExecution
{
    public static async Task<bool> Run(PackRun run, IPackBackend engine, string logs,
        Action<PackRun>? changed, Action<PackRun> save, bool preflight, CancellationToken ct,
        Func<Task<bool>>? beforeInstall = null, bool allowPartial = false, TimeSpan? preflightTimeout = null)
    {
        bool blocked = false;
        foreach (var item in run.Items)
        {
            ct.ThrowIfCancellationRequested();
            bool uncertain = item.RequiresRebootBeforeRetry || item.State is PackItemState.Installing or PackItemState.VerificationPending;
            var currentBoot = DateTime.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64);
            bool sameBoot = Math.Abs((currentBoot - (item.LastAttemptBootTimeUtc ?? run.HostBootTimeUtc)).TotalMinutes) < 1;
            // A reboot ends a previous native installer. Clear its stale guard before
            // checking, even when this new check rejects an unrelated scope/version.
            // Preflight can still set a fresh guard if it finds an active installer.
            item.RequiresRebootBeforeRetry = uncertain && sameBoot;
            item.State = PackItemState.Checking; changed?.Invoke(run);
            var working = Copy(item);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(preflightTimeout ?? TimeSpan.FromMinutes(5));
            Task? check = null;
            try
            {
                check = engine.Preflight(working, Path.Combine(logs, item.Application.Id + ".log"), timeout.Token);
                await check.WaitAsync(timeout.Token);
                ct.ThrowIfCancellationRequested();
                Apply(working, item);
                if (uncertain && sameBoot && !item.Verified)
                    throw new InvalidOperationException("Instalación anterior interrumpida: puede seguir activa. Reinicia manualmente y reabre el instalador antes de reintentar.");
                item.RequiresRebootBeforeRetry = false;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                blocked = true; item.State = PackItemState.Failed;
                item.Message = "La comprobación previa no respondió dentro del tiempo permitido. Reintenta esta aplicación más tarde.";
            }
            catch (Exception ex)
            {
                // A completed check may have detected a real active installer before failing.
                // Never publish mutations from a timed-out or cancelled operation.
                if (check is null || check.IsCompleted) Apply(working, item);
                blocked = true; item.State = PackItemState.Failed; item.Message = ex.Message;
                if (item.RequiresRebootBeforeRetry) item.LastAttemptBootTimeUtc = currentBoot;
            }
            finally
            {
                if (check is { IsCompleted: false }) _ = Observe(check);
            }
            changed?.Invoke(run);
        }
        if (!preflight) save(run);
        if (preflight) return !blocked;
        // Partial runs only skip definite preflight failures. An active or uncertain native installer still blocks the entire queue.
        if (blocked && (!allowPartial || run.Items.Any(x => x.RequiresRebootBeforeRetry) ||
            (run.Definition is null && !run.Items.Any(x => x.State == PackItemState.Pending || x.Verified)))) return false;
        if (beforeInstall != null && !await beforeInstall()) return false;
        foreach (var item in run.Items.Where(x => x.State == PackItemState.Pending))
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
            if (item.RequiresRebootBeforeRetry || item.State == PackItemState.VerificationPending) break;
        }
        return run.Complete;
    }

    private static PackItemResult Copy(PackItemResult item)
    {
        var copy = new PackItemResult { Application = item.Application };
        Apply(item, copy);
        return copy;
    }
    private static void Apply(PackItemResult source, PackItemResult target)
    {
        target.Application = source.Application;
        target.ResolveLatest = source.ResolveLatest;
        target.ResolvedChannel = source.ResolvedChannel;
        target.InstalledVersion = source.InstalledVersion;
        target.State = source.State;
        target.Message = source.Message;
        target.RebootRequired = source.RebootRequired;
        target.RequiresRebootBeforeRetry = source.RequiresRebootBeforeRetry;
        target.LastAttemptBootTimeUtc = source.LastAttemptBootTimeUtc;
    }
    private static async Task Observe(Task check)
    {
        try { await check; }
        catch { /* Detached read-only operation: its result must not be published. */ }
    }
}
