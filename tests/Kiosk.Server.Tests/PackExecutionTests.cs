using KioskClinicaPC.Core.Sync;
using Kiosk.SetupHelper;
using Xunit;
namespace Kiosk.Server.Tests;

public sealed class PackExecutionTests
{
    private static PackRun Run() => new() { Items = Enumerable.Range(0, 3).Select(i => new PackItemResult { Application = new(i.ToString(), "Vendor.App" + i, "App" + i, "1") }).ToList() };
    private sealed class Backend : IPackBackend
    {
        public List<string> Calls = [];
        public string? Reject, Fail, Cancel, Already, Unverified, Active;
        public Task Preflight(PackItemResult item, string log, CancellationToken ct)
        {
            Calls.Add("check" + item.Application.Id);
            if (item.Application.Id == Active) { item.RequiresRebootBeforeRetry = true; throw new InvalidOperationException("Instalador activo"); }
            if (item.Application.Id == Reject) throw new InvalidDataException("No compatible");
            item.State = item.Application.Id == Already ? PackItemState.AlreadyInstalled : PackItemState.Pending;
            return Task.CompletedTask;
        }
        public Task<PackNativeResult> ExecuteNative(PackItemResult item, string log, Action<PackProgress> progress, CancellationToken ct)
        {
            Calls.Add("install" + item.Application.Id);
            if (item.Application.Id == Cancel) throw new OperationCanceledException();
            return Task.FromResult(item.Application.Id == Fail
                ? new PackNativeResult(PackNativeDisposition.Failed, "Instalador fallido")
                : new PackNativeResult(PackNativeDisposition.Succeeded, "WinGet terminó"));
        }
        public Task<PackVerificationResult> Verify(PackItemResult item, string log, CancellationToken ct)
            => Task.FromResult(new PackVerificationResult(item.Application.Id != Unverified, "Verificación", "1"));
    }
    [Fact]
    public async Task Native_success_without_verification_continues_the_remaining_queue_but_stays_incomplete()
    {
        var backend = new Backend { Unverified = "1" }; var run = Run();
        Assert.False(await PackExecution.Run(run, backend, ".", null, _ => { }, false, CancellationToken.None));
        Assert.Equal(PackItemState.VerificationPending, run.Items[1].State); Assert.Contains("install2", backend.Calls);
        Assert.True(run.Items[1].NativeInstallSucceeded); Assert.True(run.Items[1].RequiresRebootBeforeRetry);
        Assert.Equal(PackItemState.Succeeded, run.Items[2].State);
    }
    [Fact]
    public async Task Whole_selection_is_checked_before_any_install()
    {
        var backend = new Backend(); var run = Run();
        Assert.True(await PackExecution.Run(run, backend, ".", null, _ => { }, false, CancellationToken.None));
        Assert.Equal(new[] { "check0", "check1", "check2", "install0", "install1", "install2" }, backend.Calls);
    }
    [Fact]
    public async Task Preflight_rejection_installs_nothing()
    {
        var backend = new Backend { Reject = "1" };
        Assert.False(await PackExecution.Run(Run(), backend, ".", null, _ => { }, false, CancellationToken.None));
        Assert.DoesNotContain(backend.Calls, x => x.StartsWith("install"));
    }
    [Fact]
    public async Task Partial_pack_installs_only_checked_apps_and_keeps_failure_in_saved_result()
    {
        var backend = new Backend { Reject = "1" }; var run = Run(); int gates = 0; PackRun? saved = null;
        Assert.False(await PackExecution.Run(run, backend, ".", null, value => saved = value, false, CancellationToken.None,
            () => { gates++; Assert.Equal(new[] { "check0", "check1", "check2" }, backend.Calls); return Task.FromResult(true); }, allowPartial: true));
        Assert.Equal(new[] { "check0", "check1", "check2", "install0", "install2" }, backend.Calls);
        Assert.Equal(1, gates); Assert.NotSame(run, saved); Assert.False(run.Complete);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(run), System.Text.Json.JsonSerializer.Serialize(saved));
        Assert.Equal(PackItemState.Failed, run.Items[1].State); Assert.Equal("No compatible", run.Items[1].Message);
        Assert.Equal(PackItemState.Succeeded, run.Items[0].State); Assert.Equal(PackItemState.Succeeded, run.Items[2].State);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Partial_pack_skips_ordinary_failure_after_reboot_but_preserves_same_boot_guard(bool rebooted)
    {
        var run = Run(); var backend = new Backend { Reject = "1" }; int gates = 0;
        run.Items[1].RequiresRebootBeforeRetry = true;
        run.Items[1].LastAttemptBootTimeUtc = run.HostBootTimeUtc.AddDays(rebooted ? -1 : 0);
        Assert.False(await PackExecution.Run(run, backend, ".", null, _ => { }, false, CancellationToken.None,
            () => { gates++; return Task.FromResult(true); }, allowPartial: true));
        Assert.Equal(!rebooted, run.Items[1].RequiresRebootBeforeRetry);
        Assert.Equal(rebooted ? 1 : 0, gates);
        Assert.Equal("No compatible", run.Items[1].Message);
        Assert.Equal(rebooted ? PackItemState.Succeeded : PackItemState.Pending, run.Items[0].State);
        Assert.Equal(rebooted ? PackItemState.Succeeded : PackItemState.Pending, run.Items[2].State);
    }
    [Fact]
    public async Task Reboot_does_not_override_a_new_active_installer_found_by_preflight()
    {
        var run = Run(); var backend = new Backend { Active = "1" };
        run.Items[1].RequiresRebootBeforeRetry = true;
        run.Items[1].LastAttemptBootTimeUtc = run.HostBootTimeUtc.AddDays(-1);
        Assert.False(await PackExecution.Run(run, backend, ".", null, _ => { }, false, CancellationToken.None,
            () => throw new Exception("Must not start Kiosk"), allowPartial: true));
        Assert.True(run.Items[1].RequiresRebootBeforeRetry);
        Assert.True(Math.Abs((run.HostBootTimeUtc - run.Items[1].LastAttemptBootTimeUtc!.Value).TotalMinutes) < 1);
        Assert.DoesNotContain(backend.Calls, x => x.StartsWith("install"));
        var nextCheck = new Backend { Reject = "1" };
        Assert.False(await PackExecution.Run(run, nextCheck, ".", null, _ => { }, false, CancellationToken.None,
            () => throw new Exception("Must retain the new same-boot guard"), allowPartial: true));
        Assert.True(run.Items[1].RequiresRebootBeforeRetry);
        Assert.DoesNotContain(nextCheck.Calls, x => x.StartsWith("install"));
    }
    [Fact]
    public async Task Partial_option_does_not_override_preflight_only_or_active_native_install()
    {
        var backend = new Backend { Reject = "1" }; var run = Run();
        Assert.False(await PackExecution.Run(run, backend, ".", null, _ => { }, true, CancellationToken.None, allowPartial: true));
        Assert.DoesNotContain(backend.Calls, x => x.StartsWith("install"));
        run.Items[0].State = PackItemState.Installing;
        Assert.False(await PackExecution.Run(run, backend, ".", null, _ => { }, false, CancellationToken.None,
            () => throw new Exception("Must not start Kiosk"), allowPartial: true));
        Assert.DoesNotContain(backend.Calls, x => x.StartsWith("install"));
    }
    [Fact]
    public async Task Partial_option_does_not_continue_if_all_apps_failed_preflight()
    {
        var backend = new Backend { Reject = "1" }; var run = Run(); run.Items = [run.Items[1]];
        Assert.False(await PackExecution.Run(run, backend, ".", null, _ => { }, false, CancellationToken.None,
            () => throw new Exception("Must not start Kiosk"), allowPartial: true));
        Assert.Equal(new[] { "check1" }, backend.Calls);
    }
    [Fact]
    public async Task Latest_policy_with_all_apps_unavailable_still_allows_independent_kiosk_component()
    {
        var backend = new Backend { Reject = "1" }; var run = Run(); run.Items = [run.Items[1]];
        run.Definition = new(1, [new("1", "Vendor.App1", "App1")]); int gates = 0;
        Assert.False(await PackExecution.Run(run, backend, ".", null, _ => { }, false, CancellationToken.None,
            () => { gates++; return Task.FromResult(true); }, allowPartial: true));
        Assert.Equal(1, gates); Assert.Equal(PackItemState.Failed, run.Items.Single().State);
        Assert.Equal(new[] { "check1" }, backend.Calls);
    }
    [Fact]
    public async Task Ordinary_failure_continues_independent_apps_without_blind_retry()
    {
        var backend = new Backend { Fail = "1" }; var run = Run();
        Assert.False(await PackExecution.Run(run, backend, ".", null, _ => { }, false, CancellationToken.None));
        Assert.Equal(PackItemState.Succeeded, run.Items[2].State); Assert.Single(backend.Calls, x => x == "install1");
    }
    [Fact]
    public async Task Native_timeout_or_cancellation_stops_queue_and_requires_verification()
    {
        var backend = new Backend { Cancel = "1" }; var run = Run();
        Assert.False(await PackExecution.Run(run, backend, ".", null, _ => { }, false, CancellationToken.None));
        Assert.Equal(PackItemState.VerificationPending, run.Items[1].State); Assert.DoesNotContain("install2", backend.Calls);
    }
    [Fact]
    public async Task Interrupted_installer_is_not_retried_on_same_boot()
    {
        var run = Run(); run.Items[0].State = PackItemState.Installing; var backend = new Backend();
        Assert.False(await PackExecution.Run(run, backend, ".", null, _ => { }, false, CancellationToken.None));
        Assert.DoesNotContain(backend.Calls, x => x.StartsWith("install"));
        Assert.False(await PackExecution.Run(run, backend, ".", null, _ => { }, false, CancellationToken.None));
        Assert.DoesNotContain(backend.Calls, x => x.StartsWith("install"));
    }
    [Fact]
    public async Task Already_installed_machine_version_is_skipped()
    {
        var backend = new Backend { Already = "1" }; var run = Run();
        Assert.True(await PackExecution.Run(run, backend, ".", null, _ => { }, false, CancellationToken.None));
        Assert.DoesNotContain("install1", backend.Calls);
    }
    private sealed class AsyncBackend(Func<PackItemResult, CancellationToken, Task> check) : IPackBackend
    {
        public List<string> Installed { get; } = [];
        public Task Preflight(PackItemResult item, string log, CancellationToken ct) => check(item, ct);
        public Task<PackNativeResult> ExecuteNative(PackItemResult item, string log, Action<PackProgress> progress, CancellationToken ct)
        {
            Installed.Add(item.Application.Id); return Task.FromResult(new PackNativeResult(PackNativeDisposition.Succeeded, "WinGet terminó"));
        }
        public Task<PackVerificationResult> Verify(PackItemResult item, string log, CancellationToken ct)
            => Task.FromResult(new PackVerificationResult(true, "Verificada", "1"));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Query_timeout_cancels_the_backend_without_creating_a_native_install_guard(bool previousGuard)
    {
        var run = Run(); run.Items[0].RequiresRebootBeforeRetry = previousGuard;
        run.Items[0].LastAttemptBootTimeUtc = previousGuard ? run.HostBootTimeUtc : null;
        bool cancelled = false;
        var queryStopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var backend = new AsyncBackend(async (item, ct) =>
        {
            if (item.Application.Id == "0")
            {
                try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
                finally { cancelled = ct.IsCancellationRequested; queryStopped.SetResult(); }
            }
            item.State = PackItemState.Pending;
        });
        Assert.False(await PackExecution.Run(run, backend, ".", null, _ => { }, false, default,
            allowPartial: true, preflightTimeout: TimeSpan.FromMilliseconds(30)));
        await queryStopped.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(cancelled);
        Assert.Equal(PackItemState.Failed, run.Items[0].State);
        Assert.Equal(previousGuard, run.Items[0].RequiresRebootBeforeRetry);
        Assert.Equal(previousGuard ? Array.Empty<string>() : new[] { "1", "2" }, backend.Installed);
    }

    [Fact]
    public async Task Noncooperative_query_cannot_mutate_live_or_saved_state_after_timeout()
    {
        var run = Run();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var backend = new AsyncBackend(async (item, ct) =>
        {
            if (item.Application.Id == "0")
            {
                await release.Task; // Deliberately ignores cancellation, like a late COM reply.
                item.Application = item.Application with { PinnedVersion = "99" };
                item.ResolveLatest = false; item.ResolvedChannel = "late"; item.InstalledVersion = "99";
                item.RequiresRebootBeforeRetry = true; item.RebootRequired = true;
                item.State = PackItemState.Succeeded; item.Message = "Late reply";
                finished.SetResult();
                return;
            }
            item.State = PackItemState.Pending;
        });
        string? saved = null;
        Assert.False(await PackExecution.Run(run, backend, ".", null,
            value => saved = System.Text.Json.JsonSerializer.Serialize(value), false, default,
            allowPartial: true, preflightTimeout: TimeSpan.FromMilliseconds(30)));
        Assert.Equal(new[] { "1", "2" }, backend.Installed);
        release.SetResult(); await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(saved, System.Text.Json.JsonSerializer.Serialize(run));
        Assert.Equal("1", run.Items[0].Application.PinnedVersion);
        Assert.False(run.Items[0].RequiresRebootBeforeRetry); Assert.False(run.RebootRequired);
        Assert.Equal(PackItemState.Failed, run.Items[0].State);
    }

    [Fact]
    public async Task Caller_cancellation_reaches_the_query_and_discards_its_late_result()
    {
        var run = Run(); using var cancel = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken received = default;
        var backend = new AsyncBackend(async (item, ct) =>
        {
            received = ct; started.SetResult(); await release.Task;
            item.State = PackItemState.Succeeded; item.RequiresRebootBeforeRetry = true; finished.SetResult();
        });
        var execution = PackExecution.Run(run, backend, ".", null, _ => throw new Exception("Cancelled check must not save"), false, cancel.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5)); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(received.IsCancellationRequested); Assert.Empty(backend.Installed);
        release.SetResult(); await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(PackItemState.Checking, run.Items[0].State); Assert.False(run.Items[0].RequiresRebootBeforeRetry);
    }
    [Theory]
    [InlineData("msi", "machine", null, true)]
    [InlineData("msi", "user", null, false)]
    [InlineData("exe", "machine", null, false)]
    [InlineData("exe", "machine", "silent", true)]
    [InlineData("inno", "machine", "interactive", false)]
    [InlineData("msix", "machine", "silent", false)]
    public void Manifest_policy_uses_metadata_not_application_specific_flags(string type, string scope, string? mode, bool expected)
    {
        string yaml = $"InstallerType: {type}\nScope: {scope}\n" + (mode == null ? "" : $"InstallModes: [{mode}]\n") + "Installers:\n- Architecture: x64\n";
        Assert.Equal(expected, ManifestPolicy.SupportsSilentMachine(yaml));
    }
    [Fact]
    public void Ambiguous_interactive_candidate_is_rejected_even_if_another_candidate_supports_silent()
    {
        Assert.False(ManifestPolicy.SupportsSilentMachine("Scope: machine\nInstallerType: exe\nInstallers:\n- Architecture: x64\n  InstallModes: [silent]\n- Architecture: x64\n  InstallModes: [interactive]\n", "x64", "exe"));
    }
}
