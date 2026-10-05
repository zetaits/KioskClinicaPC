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
        public string? Reject, Fail, Cancel, Already, Unverified;
        public Task Preflight(PackItemResult item, string log)
        {
            Calls.Add("check" + item.Application.Id);
            if (item.Application.Id == Reject) throw new InvalidDataException("No compatible");
            item.State = item.Application.Id == Already ? PackItemState.AlreadyInstalled : PackItemState.Pending;
            return Task.CompletedTask;
        }
        public Task Install(PackItemResult item, string log, Action<string> progress, CancellationToken ct)
        {
            Calls.Add("install" + item.Application.Id);
            if (item.Application.Id == Cancel) throw new OperationCanceledException();
            if (item.Application.Id == Fail) throw new InvalidOperationException("Instalador fallido");
            if (item.Application.Id == Unverified) { item.State = PackItemState.VerificationPending; item.RequiresRebootBeforeRetry = true; return Task.CompletedTask; }
            item.State = PackItemState.Succeeded; return Task.CompletedTask;
        }
    }
    [Fact]
    public async Task Native_success_without_verification_stops_the_remaining_queue()
    {
        var backend = new Backend { Unverified = "1" }; var run = Run();
        Assert.False(await PackExecution.Run(run, backend, ".", null, _ => { }, false, CancellationToken.None));
        Assert.Equal(PackItemState.VerificationPending, run.Items[1].State); Assert.DoesNotContain("install2", backend.Calls);
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
