using Kiosk.Deployment;
using Xunit;

namespace Kiosk.Server.Tests;
public sealed class DeploymentJournalTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "deployment-journal-tests", Guid.NewGuid().ToString("N"));
    [Fact]
    public async Task Offline_events_survive_restart_and_replay_in_order_without_skipping_failed_event()
    {
        string path = Path.Combine(_root, "progress.json"); var journal = new DeploymentProgressJournal(path, 2);
        journal.Append("job", DeploymentState.PostInstall, "Extracting", 60, true, true);
        journal.Append("job", DeploymentState.Completed, "Verified", null, true, true, true);
        await journal.Drain(_ => Task.FromResult(false));
        var restored = new DeploymentProgressJournal(path, 2); var sent = new List<long>();
        await restored.Drain(p => { sent.Add(p.Sequence); return Task.FromResult(true); });
        Assert.Equal(new long[] { 3, 4 }, sent);
        await restored.Drain(_ => throw new Exception("No duplicates after acknowledgement"));
        Assert.Equal(5, restored.Append("job", DeploymentState.PostInstall, "Reviewed recovery").Sequence);
    }
    [Fact]
    public async Task Application_diagnostics_are_snapshotted_and_survive_offline_restart()
    {
        var run = new KioskClinicaPC.Core.Sync.PackRun { Items = [new() {
            Application = new(new string('a', 32), "Google.Chrome", "Chrome", "155"), State = KioskClinicaPC.Core.Sync.PackItemState.Failed, Message = "Unavailable" }] };
        string path = Path.Combine(_root, "apps.json"); var journal = new DeploymentProgressJournal(path, 0);
        journal.Append("job", DeploymentState.Attention, "Partial", applications: run);
        run.Items.Clear();
        await new DeploymentProgressJournal(path, 0).Drain(progress =>
        {
            var item = Assert.Single(progress.ApplicationResult!.Items); Assert.Equal("155", item.Application.PinnedVersion);
            Assert.Equal("Unavailable", item.Message); return Task.FromResult(true);
        });
    }
    [Fact]
    public async Task Concurrent_delivery_serializes_and_lost_acknowledgement_resends_same_sequence()
    {
        var journal = new DeploymentProgressJournal(Path.Combine(_root, "progress.json"), 0);
        journal.Append("job", DeploymentState.PostInstall, "Checking", null, true, true);
        var delivered = new List<long>();
        await journal.Drain(p => { delivered.Add(p.Sequence); return Task.FromResult(false); });
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => journal.Drain(p => { lock (delivered) delivered.Add(p.Sequence); return Task.FromResult(true); })));
        Assert.Equal(new long[] { 1, 1 }, delivered);
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
