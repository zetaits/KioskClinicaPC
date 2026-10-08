namespace KioskClinicaPC.Core.Sync;

public sealed record PackApplication(string Id, string WingetId, string DisplayName, string PinnedVersion,
    bool SelectedByDefault = true, int Order = 0);
// v2 remains a concrete version snapshot. Definition is carried only by new internal workers/profiles.
public sealed record PackCatalog(long Revision, List<PackApplication> Applications, PackDefinition? Definition = null);
public sealed record PackEntry(string Id, string WingetId, string DisplayName, bool SelectedByDefault = true, int Order = 0);
public sealed record PackDefinition(long Revision, List<PackEntry> Applications)
{
    public static PackDefinition FromCatalog(PackCatalog catalog) => new(catalog.Revision,
        catalog.Applications.Select(a => new PackEntry(a.Id, a.WingetId, a.DisplayName, a.SelectedByDefault, a.Order)).ToList());
    public PackCatalog ForExecution() => new(Revision,
        Applications.Select(a => new PackApplication(a.Id, a.WingetId, a.DisplayName, "", a.SelectedByDefault, a.Order)).ToList(),
        this with { Applications = [.. Applications] });
}
public sealed record WingetIndexEntry(string Id, string Name, string Publisher, string Version,
    bool Eligible, string? Reason = null);
public sealed record WingetIndex(DateTime GeneratedAtUtc, List<WingetIndexEntry> Applications);

public enum PackItemState { Pending, Checking, Installing, Succeeded, AlreadyInstalled, Failed, VerificationPending }
public sealed class PackItemResult
{
    public required PackApplication Application { get; set; }
    public bool ResolveLatest { get; set; }
    public string? ResolvedChannel { get; set; }
    public string? InstalledVersion { get; set; }
    public PackItemState State { get; set; }
    public string Message { get; set; } = "Pendiente";
    public bool RebootRequired { get; set; }
    public bool RequiresRebootBeforeRetry { get; set; }
    public DateTime? LastAttemptBootTimeUtc { get; set; }
    public bool Verified => State is PackItemState.Succeeded or PackItemState.AlreadyInstalled;
}
public sealed class PackRun
{
    public long CatalogRevision { get; set; }
    public PackDefinition? Definition { get; set; }
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime HostBootTimeUtc { get; set; } = DateTime.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64);
    public List<PackItemResult> Items { get; set; } = [];
    public bool Complete => Items.Count > 0 && Items.All(x => x.Verified);
    public bool RebootRequired => Items.Any(x => x.RebootRequired);
    public string Summary => Complete
        ? (RebootRequired ? "Todas las aplicaciones verificadas. Solo queda reiniciar manualmente." : "Todas las aplicaciones instaladas y verificadas.")
        : $"Quedan {Items.Count(x => !x.Verified)} aplicaciones pendientes o con errores. No se da el pack por completado.";
}
