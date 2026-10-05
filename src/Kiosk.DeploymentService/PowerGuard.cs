using System.Runtime.InteropServices;
using Kiosk.Deployment;

namespace Kiosk.DeploymentService;

public sealed class PowerGuard(StationState state) : IHostedService
{
    [DllImport("kernel32.dll")] private static extern uint SetThreadExecutionState(uint flags);
    private readonly ManualResetEventSlim _stop = new(false);
    private Thread? _thread;
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _thread = new Thread(() =>
        {
            try { while (!_stop.Wait(TimeSpan.FromSeconds(10))) SetThreadExecutionState(state.Queue.Snapshot().Jobs.Any(j => DeploymentPolicy.Active(j.State) || j.State == DeploymentState.Attention && j.DestructiveStarted) ? 0x80000001 : 0x80000000); }
            finally { SetThreadExecutionState(0x80000000); }
        }) { IsBackground = true, Name = "Deployment power guard" }; _thread.Start(); return Task.CompletedTask;
    }
    public Task StopAsync(CancellationToken cancellationToken) { _stop.Set(); _thread?.Join(TimeSpan.FromSeconds(5)); return Task.CompletedTask; }
}
