using System.Collections.Immutable;

namespace Kiosk.Server.Services;

// Display metadata deliberately cannot be passed to the verified download APIs.
public sealed record InstallerEditionSummary(string Edition, long SizeBytes);
public sealed record InstallerReleaseSummary(string Version, ImmutableArray<InstallerEditionSummary> Editions);
public sealed record SetupSectionData(InstallerReleaseSummary Latest, string? ActiveVersion);
public sealed record DeploymentSectionData(string Version);
public sealed record InstallerHistoryData(ImmutableArray<InstallerReleaseSummary> Releases);

public enum InstallerSectionStatus { Loading, Ready, Empty, Error }

public sealed record InstallerSectionState<T>(InstallerSectionStatus Status, T? Data = default, string? Message = null)
    where T : class
{
    public static InstallerSectionState<T> Loading() => new(InstallerSectionStatus.Loading);
    public static InstallerSectionState<T> Ready(T data) => new(InstallerSectionStatus.Ready, data);
    public static InstallerSectionState<T> Empty() => new(InstallerSectionStatus.Empty);
    public static InstallerSectionState<T> Error(string message) => new(InstallerSectionStatus.Error, Message: message);
}
