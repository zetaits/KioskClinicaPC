namespace Kiosk.Deployment;

public sealed record DeploymentProgressJournalState(int SchemaVersion, long Sequence, List<DeploymentProgress> Pending);
public sealed class DeploymentProgressJournal
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _drain = new(1, 1);
    private readonly string _path;
    private DeploymentProgressJournalState _state;
    public DeploymentProgressJournal(string path, long initialSequence)
    {
        _path = path; _state = AtomicState.Read(path, () => new DeploymentProgressJournalState(1, initialSequence, []));
        DeploymentPolicy.Require(_state.SchemaVersion == 1 && _state.Sequence >= initialSequence, "Registro de progreso incompatible.");
    }
    public DeploymentProgress Append(string job, DeploymentState state, string phase, int? percent = null,
        bool windows = false, bool account = false, bool components = false, bool reboot = false,
        KioskClinicaPC.Core.Sync.PackRun? applications = null)
    {
        lock (_gate)
        {
            var progress = new DeploymentProgress(job, _state.Sequence + 1, state, phase[..Math.Min(phase.Length, 200)], percent,
                windows, account, components, reboot, applications is null ? null : DeploymentPolicy.Copy(applications));
            var next = new DeploymentProgressJournalState(1, progress.Sequence, [.. _state.Pending, progress]);
            AtomicState.Write(_path, next); _state = next; return progress;
        }
    }
    public async Task Drain(Func<DeploymentProgress, Task<bool>> send)
    {
        await _drain.WaitAsync();
        try
        {
            while (true)
            {
                DeploymentProgress? progress;
                lock (_gate) progress = _state.Pending.FirstOrDefault();
                if (progress is null || !await send(progress)) return;
                lock (_gate)
                {
                    var next = _state with { Pending = _state.Pending.Where(p => p.Sequence != progress.Sequence).ToList() };
                    AtomicState.Write(_path, next); _state = next;
                }
            }
        }
        finally { _drain.Release(); }
    }
}
