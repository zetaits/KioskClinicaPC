using System.Diagnostics;
using System.Threading.Channels;

namespace KioskClinicaPC.Core.Sync;

public interface IPackBackend
{
    Task Preflight(PackItemResult item, string log, CancellationToken ct);
    Task<PackNativeResult> ExecuteNative(PackItemResult item, string log, Action<PackProgress> progress, CancellationToken ct);
    Task<PackVerificationResult> Verify(PackItemResult item, string log, CancellationToken ct);
}

/// <summary>Checks the entire immutable selection before installing. Never retries an ambiguous native install.</summary>
public static class PackExecution
{
    public static async Task<bool> Run(PackRun run, IPackBackend engine, string logs,
        Action<PackRun>? changed, Action<PackRun> save, bool preflight, CancellationToken ct,
        Func<Task<bool>>? beforeInstall = null, bool allowPartial = false, TimeSpan? preflightTimeout = null,
        PackExecutionOptions? options = null, Action<TimeSpan>? executionTiming = null)
    {
        options ??= new();
        if (options.MaxInFlight is < 1 or > 5 || options.NativeTimeout <= TimeSpan.Zero || options.VerificationTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options));
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
            item.State = PackItemState.Checking; item.Phase = null; changed?.Invoke(run.Copy());
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
            changed?.Invoke(run.Copy());
        }
        if (!preflight) save(run.Copy());
        if (preflight) return !blocked;
        // Partial runs only skip definite preflight failures. An active or uncertain native installer still blocks the entire queue.
        if (blocked && (!allowPartial || run.Items.Any(x => x.RequiresRebootBeforeRetry) ||
            (run.Definition is null && !run.Items.Any(x => x.State == PackItemState.Pending || x.Verified)))) return false;
        if (beforeInstall != null && !await beforeInstall()) return false;
        long start = Stopwatch.GetTimestamp();
        try { return await Pipeline(run, engine, logs, changed, save, options, ct); }
        finally { executionTiming?.Invoke(Stopwatch.GetElapsedTime(start)); }
    }

    private sealed record Update(int Index, PackProgress? Progress = null, PackNativeResult? Native = null,
        PackVerificationResult? Verification = null, bool Stop = false);

    private static async Task<bool> Pipeline(PackRun run, IPackBackend engine, string logs,
        Action<PackRun>? changed, Action<PackRun> save, PackExecutionOptions options, CancellationToken ct)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var updates = Channel.CreateUnbounded<Update>(new() { SingleReader = true });
        using var cancellation = ct.Register(() => updates.Writer.TryWrite(new(-1, Stop: true)));
        var pending = new Queue<int>(Enumerable.Range(0, run.Items.Count).Where(i => run.Items[i].State == PackItemState.Pending)
            .OrderBy(i => run.Items[i].Application.Order).ThenBy(i => i));
        var native = new HashSet<int>();
        var verification = new Queue<int>();
        int? verifying = null;
        int inFlight = 0;
        var lastProgress = new Dictionary<int, long>();
        var tasks = new List<Task>();
        void Publish(bool persist)
        {
            // Consumers may dispatch asynchronously or retain snapshots. Never expose the mutable run.
            if (persist) save(run.Copy());
            changed?.Invoke(run.Copy());
        }
        void Fill()
        {
            while (!stop.IsCancellationRequested && pending.Count > 0 && inFlight < options.MaxInFlight)
            {
                // Process already available outcomes before admitting more native work.
                if (updates.Reader.TryPeek(out _)) break;
                int index = pending.Dequeue();
                var item = run.Items[index];
                item.State = PackItemState.Installing; item.Phase = PackItemPhase.Queued;
                item.NativeInstallSucceeded = false; item.Message = "En cola de WinGet";
                item.LastAttemptBootTimeUtc = DateTime.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64);
                Publish(true); // Crash protection precedes the COM call, including queued downloads.
                native.Add(index); inFlight++;
                tasks.Add(Execute(index, item.Copy()));
            }
        }
        void StartVerification()
        {
            if (verifying is not null || verification.Count == 0) return;
            int index = verification.Dequeue(); verifying = index;
            var item = run.Items[index]; item.Phase = PackItemPhase.Verifying;
            item.Message = "Verificando versión y ámbito…"; Publish(true);
            tasks.Add(Check(index, item.Copy()));
        }
        async Task Execute(int index, PackItemResult item)
        {
            PackNativeResult result;
            Task<PackNativeResult>? operation = null;
            using var timeout = new CancellationTokenSource(options.NativeTimeout ?? TimeSpan.FromMinutes(60));
            using var backendCancel = CancellationTokenSource.CreateLinkedTokenSource(stop.Token, timeout.Token);
            try
            {
                if (stop.IsCancellationRequested) result = new(PackNativeDisposition.NotStarted, "Cancelado antes de iniciar WinGet.");
                else
                {
                    operation = engine.ExecuteNative(item, Path.Combine(logs, item.Application.Id + ".log"),
                        progress => updates.Writer.TryWrite(new(index, Progress: progress)), backendCancel.Token);
                    // Cancellation stops admission/queued work. A native installer already running must drain.
                    result = await operation.WaitAsync(timeout.Token);
                }
            }
            catch (Exception ex)
            {
                result = new(PackNativeDisposition.Uncertain, ex is OperationCanceledException
                    ? "Cancelado o tiempo agotado. El instalador puede seguir activo; la cola se detiene."
                    : "Se perdió el resultado de WinGet; se detiene la cola antes de reintentar.");
            }
            finally { if (operation is { IsCompleted: false }) _ = Observe(operation); }
            if (result.Disposition == PackNativeDisposition.Uncertain) stop.Cancel();
            updates.Writer.TryWrite(new(index, Native: result));
        }
        async Task Check(int index, PackItemResult item)
        {
            PackVerificationResult result;
            Task<PackVerificationResult>? check = null;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(options.VerificationTimeout ?? TimeSpan.FromMinutes(2));
            try
            {
                timeout.Token.ThrowIfCancellationRequested();
                check = engine.Verify(item, Path.Combine(logs, item.Application.Id + ".log"), timeout.Token);
                result = await check.WaitAsync(timeout.Token);
                timeout.Token.ThrowIfCancellationRequested();
            }
            catch (Exception ex)
            {
                result = new(false, ex is OperationCanceledException
                    ? "La verificación se canceló o agotó su tiempo. WinGet había terminado; queda pendiente de revisión."
                    : "No se pudo comprobar la versión y el ámbito tras finalizar WinGet. Revisar antes de reintentar.");
            }
            finally { if (check is { IsCompleted: false }) _ = Observe(check); }
            updates.Writer.TryWrite(new(index, Verification: result));
        }
        try
        {
            Fill();
            while (inFlight > 0 || (!stop.IsCancellationRequested && pending.Count > 0))
            {
                var update = await updates.Reader.ReadAsync();
                if (update.Stop) { stop.Cancel(); continue; }
                var item = run.Items[update.Index];
                if (update.Progress is { } progress && native.Contains(update.Index))
                {
                    if (item.Phase is { } phase && progress.Phase < phase) continue;
                    bool transition = item.Phase != progress.Phase;
                    long now = Stopwatch.GetTimestamp();
                    if (!transition && lastProgress.TryGetValue(update.Index, out long previous) &&
                        Stopwatch.GetElapsedTime(previous, now) < TimeSpan.FromMilliseconds(250)) continue;
                    item.Phase = progress.Phase; item.Message = progress.Message; lastProgress[update.Index] = now;
                    Publish(transition);
                }
                else if (update.Native is { } result && native.Remove(update.Index))
                {
                    item.RebootRequired |= result.RebootRequired; item.Message = result.Message; item.Phase = null;
                    switch (result.Disposition)
                    {
                        case PackNativeDisposition.Succeeded:
                            item.NativeInstallSucceeded = true; item.State = PackItemState.VerificationPending;
                            item.Phase = PackItemPhase.WaitingVerification; item.Message = "WinGet terminó; esperando verificación.";
                            verification.Enqueue(update.Index); break;
                        case PackNativeDisposition.AlreadyInstalled:
                            item.State = PackItemState.AlreadyInstalled; item.InstalledVersion = result.InstalledVersion;
                            item.RequiresRebootBeforeRetry = false; inFlight--; break;
                        case PackNativeDisposition.Uncertain:
                            item.State = PackItemState.VerificationPending; item.RequiresRebootBeforeRetry = true;
                            stop.Cancel(); inFlight--; break;
                        case PackNativeDisposition.NotStarted:
                            item.State = PackItemState.Pending; item.RequiresRebootBeforeRetry = false; inFlight--; break;
                        default:
                            item.State = PackItemState.Failed; item.RequiresRebootBeforeRetry = false; inFlight--; break;
                    }
                    Publish(true);
                }
                else if (update.Verification is { } verified && verifying == update.Index)
                {
                    item.State = verified.Verified ? PackItemState.Succeeded : PackItemState.VerificationPending;
                    item.InstalledVersion = verified.InstalledVersion; item.Message = verified.Message; item.Phase = null;
                    item.RequiresRebootBeforeRetry = !verified.Verified;
                    verifying = null; inFlight--; Publish(true);
                    // A failed read after confirmed native success does not stop independent applications.
                }
                StartVerification(); Fill();
            }
            await Task.WhenAll(tasks);
            return !ct.IsCancellationRequested && run.Complete;
        }
        finally
        {
            stop.Cancel(); updates.Writer.TryComplete();
            // A persistence/progress consumer failure must still drain native work before the worker exits.
            await Task.WhenAll(tasks);
        }
    }

    private static PackItemResult Copy(PackItemResult item)
    {
        return item.Copy();
    }
    private static void Apply(PackItemResult source, PackItemResult target)
    {
        target.Application = source.Application;
        target.ResolveLatest = source.ResolveLatest;
        target.ResolvedChannel = source.ResolvedChannel;
        target.InstalledVersion = source.InstalledVersion;
        target.State = source.State;
        target.Phase = source.Phase;
        target.NativeInstallSucceeded = source.NativeInstallSucceeded;
        target.Message = source.Message;
        target.RebootRequired = source.RebootRequired;
        target.RequiresRebootBeforeRetry = source.RequiresRebootBeforeRetry;
        target.LastAttemptBootTimeUtc = source.LastAttemptBootTimeUtc;
    }
    private static async Task Observe(Task check)
    {
        try { await check; }
        catch { /* Detached operation owns a copy; its late result must not be published. */ }
    }
}
