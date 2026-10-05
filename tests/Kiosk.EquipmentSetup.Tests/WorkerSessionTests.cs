using System.Diagnostics;
using System.IO;
using System.Text;
using Kiosk.EquipmentSetup;
using KioskClinicaPC.Core.Sync;
using KioskClinicaPC.Equipment;
using Xunit;

namespace Kiosk.EquipmentSetup.Tests;
public sealed class WorkerSessionTests
{
    private static PackCatalog Catalog => new(1, [new(new string('a', 32), "Vendor.App", "App", "1")]);
    private static Task<ProcessStartInfo> Fake(string script)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"));
        foreach (string arg in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) }) start.ArgumentList.Add(arg);
        return Task.FromResult(start);
    }
    [Fact]
    public async Task Duplex_worker_retains_preflight_until_coordinator_continues_and_reports_reboot()
    {
        const string script = "[Console]::ReadLine() | Out-Null; [Console]::WriteLine('{\"kind\":\"preflight-ready\",\"message\":\"Ready\"}'); " +
            "$command=[Console]::ReadLine(); if($command -ne 'install'){exit 2}; [Console]::WriteLine('{\"kind\":\"result\",\"message\":\"Done\",\"exitCode\":0,\"rebootRequired\":true}'); exit 0";
        var events = new List<EquipmentEvent>(); var logs = new List<string>();
        await using var worker = new PackSession("unused", logs.Add, _ => Fake(script));
        Assert.True(await worker.Preflight(Catalog, true, events.Add, CancellationToken.None));
        Assert.True(await worker.Install(events.Add, CancellationToken.None)); Assert.True(worker.RebootRequired);
        Assert.Contains(events, e => e.Kind == "preflight-ready"); Assert.Empty(logs);
    }
    [Fact]
    public async Task Unexpected_worker_exit_is_detected_instead_of_waiting_forever()
    {
        await using var worker = new PackSession("unused", _ => { }, _ => Fake("[Console]::ReadLine() | Out-Null; exit 1"));
        await Assert.ThrowsAsync<IOException>(() => worker.Preflight(Catalog, false, _ => { }, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(20)));
    }
    [Fact]
    public async Task Cancellation_is_sent_over_stdin_and_worker_is_allowed_to_finish()
    {
        const string script = "[Console]::ReadLine() | Out-Null; [Console]::WriteLine('{\"kind\":\"preflight-ready\",\"message\":\"Ready\"}'); " +
            "[Console]::ReadLine() | Out-Null; $command=[Console]::ReadLine(); if($command -ne 'cancel'){exit 1}; [Console]::WriteLine('{\"kind\":\"result\",\"message\":\"Cancelled\",\"exitCode\":2}'); exit 2";
        using var cancel = new CancellationTokenSource();
        await using var worker = new PackSession("unused", _ => { }, _ => Fake(script));
        Assert.True(await worker.Preflight(Catalog, true, _ => { }, CancellationToken.None));
        Task<bool> installing = worker.Install(_ => { }, cancel.Token); cancel.Cancel();
        Assert.False(await installing.WaitAsync(TimeSpan.FromSeconds(20)));
    }
}
