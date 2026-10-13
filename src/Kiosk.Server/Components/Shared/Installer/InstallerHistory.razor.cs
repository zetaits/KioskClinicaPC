using Kiosk.Server.Services;
using Microsoft.AspNetCore.Components;

namespace Kiosk.Server.Components.Shared.Installer;

public partial class InstallerHistory : IDisposable
{
    [Inject] public IInstallerPageQuery Query { get; set; } = default!;
    [Parameter, EditorRequired] public string DisplayedVersion { get; set; } = "";
    [Parameter] public string? ActiveVersion { get; set; }
    private InstallerSectionState<InstallerHistoryData> State { get; set; } = InstallerSectionState<InstallerHistoryData>.Loading();
    private readonly CancellationTokenSource _lifetime = new();
    private bool _requested;
    private bool _disposed;

    private Task EnsureLoadedAsync() => _requested ? Task.CompletedTask : LoadAsync();
    private Task RetryAsync() => State.Status == InstallerSectionStatus.Error ? LoadAsync() : Task.CompletedTask;

    private async Task LoadAsync()
    {
        _requested = true;
        State = InstallerSectionState<InstallerHistoryData>.Loading();
        try
        {
            var result = await Query.GetHistoryAsync(DisplayedVersion, _lifetime.Token);
            if (!_disposed) State = result;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
    }

    private static string Size(long bytes) => $"{bytes / 1024d / 1024:F1} MiB";

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
