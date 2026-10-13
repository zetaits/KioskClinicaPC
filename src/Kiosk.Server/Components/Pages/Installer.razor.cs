using Kiosk.Server.Services;
using Microsoft.AspNetCore.Components;

namespace Kiosk.Server.Components.Pages;

public partial class Installer : IDisposable
{
    [Inject] public IInstallerPageQuery Query { get; set; } = default!;

    [PersistentState]
    public InstallerSectionState<SetupSectionData>? Setup { get; set; }

    [PersistentState]
    public InstallerSectionState<DeploymentSectionData>? Deployment { get; set; }

    private readonly CancellationTokenSource _lifetime = new();
    private bool _disposed;

    protected override Task OnInitializedAsync()
    {
        Setup ??= InstallerSectionState<SetupSectionData>.Loading();
        Deployment ??= InstallerSectionState<DeploymentSectionData>.Loading();
        return Task.WhenAll(
            Setup.Status == InstallerSectionStatus.Loading ? LoadSetupAsync() : Task.CompletedTask,
            Deployment.Status == InstallerSectionStatus.Loading ? LoadDeploymentAsync() : Task.CompletedTask);
    }

    private Task RetrySetupAsync() => Setup?.Status == InstallerSectionStatus.Error ? LoadSetupAsync() : Task.CompletedTask;
    private Task RetryDeploymentAsync() => Deployment?.Status == InstallerSectionStatus.Error ? LoadDeploymentAsync() : Task.CompletedTask;

    private async Task LoadSetupAsync()
    {
        Setup = InstallerSectionState<SetupSectionData>.Loading();
        try
        {
            var result = await Query.GetSetupSummaryAsync(_lifetime.Token);
            if (_disposed) return;
            Setup = result;
            StateHasChanged();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
    }

    private async Task LoadDeploymentAsync()
    {
        Deployment = InstallerSectionState<DeploymentSectionData>.Loading();
        try
        {
            var result = await Query.GetDeploymentSummaryAsync(_lifetime.Token);
            if (_disposed) return;
            Deployment = result;
            StateHasChanged();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
