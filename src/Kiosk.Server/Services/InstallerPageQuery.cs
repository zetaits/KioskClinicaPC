using System.Diagnostics;

namespace Kiosk.Server.Services;

public interface IInstallerPageQuery
{
    Task<InstallerSectionState<SetupSectionData>> GetSetupSummaryAsync(CancellationToken ct);
    Task<InstallerSectionState<DeploymentSectionData>> GetDeploymentSummaryAsync(CancellationToken ct);
    Task<InstallerSectionState<InstallerHistoryData>> GetHistoryAsync(string displayedVersion, CancellationToken ct);
}

public sealed class InstallerPageQuery(SetupReleaseStore setups, DeploymentReleaseStore deployments,
    ILogger<InstallerPageQuery> logger) : IInstallerPageQuery
{
    public Task<InstallerSectionState<SetupSectionData>> GetSetupSummaryAsync(CancellationToken ct) =>
        ReadAsync("equipos", async () =>
        {
            string? active = await setups.ReadActiveVersionMetadataAsync(ct);
            var latest = await setups.LatestMetadataAsync(ct);
            return latest is null ? null : new SetupSectionData(latest, active);
        }, ct);

    public Task<InstallerSectionState<DeploymentSectionData>> GetDeploymentSummaryAsync(CancellationToken ct) =>
        ReadAsync("instalación por red", () => deployments.LatestMetadataAsync(ct), ct);

    public Task<InstallerSectionState<InstallerHistoryData>> GetHistoryAsync(string displayedVersion, CancellationToken ct) =>
        ReadAsync("historial", async () =>
        {
            var releases = await setups.HistoryMetadataAsync(displayedVersion, ct);
            return releases.IsEmpty ? null : new InstallerHistoryData(releases);
        }, ct);

    private async Task<InstallerSectionState<T>> ReadAsync<T>(string section, Func<Task<T?>> read, CancellationToken ct)
        where T : class
    {
        long start = Stopwatch.GetTimestamp();
        try
        {
            ct.ThrowIfCancellationRequested();
            var data = await read();
            ct.ThrowIfCancellationRequested();
            return data is null ? InstallerSectionState<T>.Empty() : InstallerSectionState<T>.Ready(data);
        }
        catch (Exception ex) when (InstallerMetadataFiles.IsReadError(ex))
        {
            logger.LogWarning(ex, "No se pudo consultar la sección {Section} del instalador", section);
            return InstallerSectionState<T>.Error("No se pudo cargar esta sección. Vuelve a intentarlo.");
        }
        finally
        {
            logger.LogDebug("Consulta de metadatos del instalador {Section}: {ElapsedMs:F1} ms",
                section, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
    }
}
