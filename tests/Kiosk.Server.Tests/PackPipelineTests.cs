using System.Collections.Concurrent;
using System.Text.Json;
using KioskClinicaPC.Core.Sync;
using Xunit;

namespace Kiosk.Server.Tests;

public sealed class PackPipelineTests
{
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static PackRun Run(int count = 7) => new() { Items = Enumerable.Range(0, count)
        .Select(i => new PackItemResult { Application = new(i.ToString(), "Vendor.App" + i, "App" + i, "1", Order: i) }).ToList() };
    private static Task Await(TaskCompletionSource signal) => signal.Task.WaitAsync(TimeSpan.FromSeconds(10));

    // Models the documented COM queues: three downloads and one native installer.
    private sealed class QueuedBackend(int count) : IPackBackend
    {
        private readonly SemaphoreSlim _downloads = new(3), _installer = new(1);
        public readonly TaskCompletionSource[] Admitted = Enumerable.Range(0, count).Select(_ => Signal()).ToArray();
        public readonly TaskCompletionSource[] Downloaded = Enumerable.Range(0, count).Select(_ => Signal()).ToArray();
        public readonly TaskCompletionSource[] DownloadRelease = Enumerable.Range(0, count).Select(_ => Signal()).ToArray();
        public readonly TaskCompletionSource[] Installing = Enumerable.Range(0, count).Select(_ => Signal()).ToArray();
        public readonly TaskCompletionSource[] InstallRelease = Enumerable.Range(0, count).Select(_ => Signal()).ToArray();
        public readonly TaskCompletionSource[] Verifying = Enumerable.Range(0, count).Select(_ => Signal()).ToArray();
        public readonly TaskCompletionSource[] VerifyRelease = Enumerable.Range(0, count).Select(_ => Signal()).ToArray();
        public readonly ConcurrentQueue<int> InstallOrder = new();
        public int ActiveInstalls, ActiveChecks, MaxInstalls, MaxChecks;
        public Task Preflight(PackItemResult item, string log, CancellationToken ct)
        { item.State = PackItemState.Pending; return Task.CompletedTask; }
        public async Task<PackNativeResult> ExecuteNative(PackItemResult item, string log, Action<PackProgress> progress, CancellationToken ct)
        {
            int index = int.Parse(item.Application.Id); Admitted[index].TrySetResult();
            await _downloads.WaitAsync(ct);
            try
            {
                progress(new(PackItemPhase.Downloading, "Descargando")); Downloaded[index].TrySetResult();
                await DownloadRelease[index].Task.WaitAsync(ct);
            }
            finally { _downloads.Release(); }
            await _installer.WaitAsync(ct);
            try
            {
                int active = Interlocked.Increment(ref ActiveInstalls); MaxInstalls = Math.Max(MaxInstalls, active);
                InstallOrder.Enqueue(index); progress(new(PackItemPhase.Installing, "Instalando")); Installing[index].TrySetResult();
                // A real native installer already executing is allowed to drain after cancellation.
                await InstallRelease[index].Task;
                return new(PackNativeDisposition.Succeeded, "WinGet terminó");
            }
            finally { Interlocked.Decrement(ref ActiveInstalls); _installer.Release(); }
        }
        public async Task<PackVerificationResult> Verify(PackItemResult item, string log, CancellationToken ct)
        {
            int index = int.Parse(item.Application.Id);
            int active = Interlocked.Increment(ref ActiveChecks); MaxChecks = Math.Max(MaxChecks, active);
            Verifying[index].TrySetResult();
            try { await VerifyRelease[index].Task.WaitAsync(ct); return new(true, "Verificada", "1"); }
            finally { Interlocked.Decrement(ref ActiveChecks); }
        }
        public void ReleaseAll()
        {
            foreach (var gate in DownloadRelease.Concat(InstallRelease).Concat(VerifyRelease)) gate.TrySetResult();
        }
    }

    [Fact]
    public async Task Ready_application_installs_first_and_next_install_overlaps_one_verification_with_bounded_admission()
    {
        var run = Run(); var backend = new QueuedBackend(run.Items.Count);
        var snapshots = new ConcurrentQueue<PackRun>();
        Task<bool> execution = PackExecution.Run(run, backend, ".", null, snapshots.Enqueue, false, default);
        try
        {
            await Task.WhenAll(backend.Admitted.Take(5).Select(Await));
            await Task.WhenAll(backend.Downloaded.Take(3).Select(Await));
            Assert.False(backend.Admitted[5].Task.IsCompleted);
            backend.DownloadRelease[1].SetResult(); await Await(backend.Installing[1]);
            Assert.False(backend.Installing[0].Task.IsCompleted); Assert.Equal(1, backend.InstallOrder.First());
            backend.InstallRelease[1].SetResult(); await Await(backend.Verifying[1]);
            backend.DownloadRelease[2].SetResult(); await Await(backend.Installing[2]);
            Assert.False(backend.VerifyRelease[1].Task.IsCompleted);
            backend.InstallRelease[2].SetResult();
            Assert.False(backend.Verifying[2].Task.IsCompleted);
            Assert.False(backend.Admitted[5].Task.IsCompleted);
            Assert.False(execution.IsCompleted);
            backend.VerifyRelease[1].SetResult(); await Await(backend.Admitted[5]);
        }
        finally { backend.ReleaseAll(); }
        Assert.True(await execution.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(1, backend.MaxInstalls); Assert.Equal(1, backend.MaxChecks);
        Assert.True(run.Complete);
        // Saving a snapshot must not turn it into a future live view of the same run.
        Assert.All(snapshots.First().Items, item => Assert.Equal(PackItemState.Pending, item.State));
        Assert.Contains(snapshots, snapshot => snapshot.Items[1].Phase == PackItemPhase.Verifying &&
            snapshot.Items[2].Phase == PackItemPhase.Installing);
    }

    private sealed class Backend(
        Func<PackItemResult, Action<PackProgress>, CancellationToken, Task<PackNativeResult>> native,
        Func<PackItemResult, CancellationToken, Task<PackVerificationResult>>? verify = null) : IPackBackend
    {
        public ConcurrentQueue<string> Calls = new();
        public Task Preflight(PackItemResult item, string log, CancellationToken ct)
        { item.State = PackItemState.Pending; return Task.CompletedTask; }
        public Task<PackNativeResult> ExecuteNative(PackItemResult item, string log, Action<PackProgress> progress, CancellationToken ct)
        { Calls.Enqueue(item.Application.Id); return native(item, progress, ct); }
        public Task<PackVerificationResult> Verify(PackItemResult item, string log, CancellationToken ct)
            => verify?.Invoke(item, ct) ?? Task.FromResult(new PackVerificationResult(true, "Verificada", "1"));
    }

    private sealed class DeferredBackend : IPackBackend, IPackDeferredBackend
    {
        public bool OtherInstalledBeforeFallback { get; private set; }
        public int FallbackCalls { get; private set; }
        public PackNativeDisposition FallbackDisposition { get; set; } = PackNativeDisposition.Succeeded;
        public Task Preflight(PackItemResult item, string log, CancellationToken ct)
        { item.State = PackItemState.Pending; return Task.CompletedTask; }
        public Task<PackNativeResult> ExecuteNative(PackItemResult item, string log, Action<PackProgress> progress, CancellationToken ct)
            => Task.FromResult(item.Application.Id == "0"
                ? new PackNativeResult(PackNativeDisposition.Deferred, "Hash cambiado")
                : new PackNativeResult(PackNativeDisposition.Succeeded, "Instalada"));
        public Task<PackNativeResult> ExecuteDeferred(PackItemResult item, string log, Action<PackProgress> progress, CancellationToken ct)
        {
            FallbackCalls++;
            OtherInstalledBeforeFallback = _verifiedOther;
            return Task.FromResult(new PackNativeResult(FallbackDisposition, "Resultado del instalador oficial"));
        }
        private bool _verifiedOther;
        public Task<PackVerificationResult> Verify(PackItemResult item, string log, CancellationToken ct)
        {
            if (item.Application.Id == "1") _verifiedOther = true;
            return Task.FromResult(new PackVerificationResult(true, "Verificada", "1"));
        }
    }

    [Fact]
    public async Task Hash_mismatch_fallback_waits_for_other_native_work_and_is_verified()
    {
        var run = Run(2); var backend = new DeferredBackend();
        Assert.True(await PackExecution.Run(run, backend, ".", null, _ => { }, false, default));
        Assert.True(backend.OtherInstalledBeforeFallback);
        Assert.Equal(1, backend.FallbackCalls);
        Assert.All(run.Items, item => Assert.True(item.Verified));
        Assert.False(run.Items[0].RequiresRebootBeforeRetry);
    }

    [Fact]
    public async Task Uncertain_deferred_installer_requires_review_before_retry()
    {
        var run = Run(2); var backend = new DeferredBackend { FallbackDisposition = PackNativeDisposition.Uncertain };
        Assert.False(await PackExecution.Run(run, backend, ".", null, _ => { }, false, default));
        Assert.True(run.Items[0].RequiresRebootBeforeRetry);
        Assert.Equal(PackItemState.VerificationPending, run.Items[0].State);
        Assert.True(run.Items[1].Verified);
    }

    [Fact]
    public async Task Reboot_notice_survives_verification_persistence_and_does_not_stop_other_apps()
    {
        var run = Run(3); var snapshots = new List<PackRun>();
        var backend = new Backend((item, _, _) => Task.FromResult(new PackNativeResult(
            PackNativeDisposition.Succeeded, "Terminó", RebootRequired: item.Application.Id == "0")));
        Assert.True(await PackExecution.Run(run, backend, ".", null, snapshots.Add, false, default));
        Assert.True(run.Complete); Assert.True(run.RebootRequired);
        Assert.All(run.Items, item => Assert.True(item.Verified));
        Assert.Contains("Windows tiene cambios pendientes de reinicio", run.Summary);
        Assert.Equal(3, backend.Calls.Count);
        var saved = JsonSerializer.Deserialize<PackRun>(JsonSerializer.Serialize(snapshots.Last()))!;
        Assert.True(saved.Complete); Assert.True(saved.RebootRequired);
        Assert.True(saved.Items[0].RebootRequired);
        Assert.False(saved.Items[0].RequiresRebootBeforeRetry);
        Assert.All(saved.Items.Skip(1), item => Assert.False(item.RebootRequired));
    }

    [Fact]
    public async Task Verification_exception_continues_other_apps_and_preserves_retry_guard()
    {
        var run = Run(3);
        var backend = new Backend((_, _, _) => Task.FromResult(new PackNativeResult(PackNativeDisposition.Succeeded, "Terminó")),
            (item, _) => item.Application.Id == "0" ? throw new IOException("Read failure")
                : Task.FromResult(new PackVerificationResult(true, "Verificada", "1")));
        Assert.False(await PackExecution.Run(run, backend, ".", null, _ => { }, false, default));
        Assert.Equal(new[] { "0", "1", "2" }, backend.Calls);
        Assert.True(run.Items[0].NativeInstallSucceeded); Assert.True(run.Items[0].RequiresRebootBeforeRetry);
        Assert.Equal(PackItemState.VerificationPending, run.Items[0].State);
        Assert.All(run.Items.Skip(1), item => Assert.True(item.Verified));
    }

    [Fact]
    public async Task Native_uncertainty_cancels_admitted_work_and_never_admits_the_rest()
    {
        var entered = Signal(); var uncertain = Signal(); int admissions = 0; var run = Run();
        var backend = new Backend(async (item, _, ct) =>
        {
            if (Interlocked.Increment(ref admissions) == 5) entered.TrySetResult();
            if (item.Application.Id == "0") { await uncertain.Task; return new(PackNativeDisposition.Uncertain, "Resultado perdido"); }
            await Task.Delay(Timeout.InfiniteTimeSpan, ct); throw new Exception("Unreachable");
        });
        Task<bool> execution = PackExecution.Run(run, backend, ".", null, _ => { }, false, default);
        await Await(entered); uncertain.SetResult();
        Assert.False(await execution.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(5, backend.Calls.Count);
        Assert.All(run.Items.Take(5), item => Assert.True(item.RequiresRebootBeforeRetry));
        Assert.All(run.Items.Skip(5), item => Assert.Equal(PackItemState.Pending, item.State));
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task Late_native_or_verification_response_cannot_overwrite_saved_state(bool nativeTimeout)
    {
        var release = Signal(); var finished = Signal(); var run = Run(2);
        var backend = new Backend(async (item, progress, _) =>
        {
            if (nativeTimeout && item.Application.Id == "0")
            {
                await release.Task; item.State = PackItemState.Succeeded; item.RebootRequired = true;
                progress(new(PackItemPhase.Installing, "Late progress")); finished.SetResult();
            }
            return new(PackNativeDisposition.Succeeded, "Terminó");
        }, async (item, _) =>
        {
            if (!nativeTimeout && item.Application.Id == "0")
            { await release.Task; item.InstalledVersion = "99"; finished.SetResult(); }
            return new(true, "Verificada", "1");
        });
        string? saved = null;
        Assert.False(await PackExecution.Run(run, backend, ".", null, snapshot => saved = JsonSerializer.Serialize(snapshot),
            false, default, options: new(MaxInFlight: 1, NativeTimeout: TimeSpan.FromMilliseconds(50),
                VerificationTimeout: TimeSpan.FromMilliseconds(50))));
        Assert.Equal(nativeTimeout ? 1 : 2, backend.Calls.Count);
        Assert.Equal(PackItemState.VerificationPending, run.Items[0].State);
        release.SetResult(); await Await(finished);
        Assert.Equal(saved, JsonSerializer.Serialize(run));
        Assert.False(run.RebootRequired);
    }

    [Fact]
    public async Task Cancellation_waits_for_known_active_installer_without_admitting_another_app()
    {
        var entered = Signal(); var release = Signal(); using var cancel = new CancellationTokenSource();
        var run = Run(2);
        var backend = new Backend(async (_, progress, _) =>
        {
            progress(new(PackItemPhase.Installing, "Instalando")); entered.SetResult();
            await release.Task; return new(PackNativeDisposition.Succeeded, "Terminó");
        });
        Task<bool> execution = PackExecution.Run(run, backend, ".", null, _ => { }, false, cancel.Token, options: new(MaxInFlight: 1));
        await Await(entered); cancel.Cancel(); Assert.False(execution.IsCompleted);
        release.SetResult(); Assert.False(await execution.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Single(backend.Calls); Assert.True(run.Items[0].NativeInstallSucceeded);
        Assert.Equal(PackItemState.VerificationPending, run.Items[0].State);
        Assert.Equal(PackItemState.Pending, run.Items[1].State);
    }

    [Fact]
    public async Task Frequent_download_updates_are_coalesced_but_phase_transitions_are_immediate()
    {
        var run = Run(1); var snapshots = new List<PackRun>();
        var backend = new Backend((_, progress, _) =>
        {
            for (int i = 0; i < 200; i++) progress(new(PackItemPhase.Downloading, "tick" + i));
            progress(new(PackItemPhase.Installing, "Instalando"));
            progress(new(PackItemPhase.Downloading, "Stale download callback"));
            return Task.FromResult(new PackNativeResult(PackNativeDisposition.Succeeded, "Terminó"));
        });
        Assert.True(await PackExecution.Run(run, backend, ".", snapshots.Add, _ => { }, false, default));
        Assert.Single(snapshots, snapshot => snapshot.Items[0].Message.StartsWith("tick", StringComparison.Ordinal));
        Assert.Contains(snapshots, snapshot => snapshot.Items[0].Phase == PackItemPhase.Installing);
        Assert.DoesNotContain(snapshots, snapshot => snapshot.Items[0].Message == "Stale download callback");
    }

    [Fact]
    public async Task Persistence_failure_still_drains_an_already_running_native_operation()
    {
        var entered = Signal(); var release = Signal(); var run = Run(1); int writes = 0;
        var backend = new Backend(async (_, progress, _) =>
        {
            progress(new(PackItemPhase.Installing, "Instalando")); entered.SetResult();
            await release.Task; return new(PackNativeDisposition.Succeeded, "Terminó");
        });
        Task<bool> execution = PackExecution.Run(run, backend, ".", null, _ =>
        {
            // Initial preflight snapshot, admission guard, then native progress transition.
            if (++writes == 3) throw new IOException("State unavailable");
        }, false, default);
        await Await(entered); Assert.False(execution.IsCompleted); release.SetResult();
        await Assert.ThrowsAsync<IOException>(() => execution.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Single(backend.Calls);
    }

    [Fact]
    public async Task Native_guard_is_saved_before_backend_call_and_progress_callbacks_cannot_mutate_retained_snapshots()
    {
        var run = Run(1); PackRun? saved = null;
        var backend = new Backend((item, progress, _) =>
        {
            Assert.NotNull(saved); Assert.Equal(PackItemState.Installing, saved.Items[0].State);
            Assert.NotNull(saved.Items[0].LastAttemptBootTimeUtc);
            progress(new(PackItemPhase.Downloading, "Descargando"));
            item.Application = item.Application with { PinnedVersion = "99" }; // Backend owns only a copy.
            return Task.FromResult(new PackNativeResult(PackNativeDisposition.Succeeded, "Terminó"));
        });
        Assert.True(await PackExecution.Run(run, backend, ".", null, snapshot => saved = snapshot, false, default));
        Assert.Equal("1", run.Items[0].Application.PinnedVersion);
    }
}
