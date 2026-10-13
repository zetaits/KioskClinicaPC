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
// Activity is additive: persisted state values remain compatible with older panels/workers.
public enum PackItemPhase { Queued, Downloading, Installing, PostInstall, WaitingVerification, Verifying }
public sealed record PackProgress(PackItemPhase Phase, string Message, ulong? BytesDownloaded = null, ulong? BytesRequired = null);
public enum PackNativeDisposition { Succeeded, AlreadyInstalled, Failed, Uncertain, NotStarted }
public sealed record PackNativeResult(PackNativeDisposition Disposition, string Message,
    bool RebootRequired = false, string? InstalledVersion = null);
public sealed record PackVerificationResult(bool Verified, string Message, string? InstalledVersion = null);
public sealed record PackExecutionOptions(int MaxInFlight = 5, TimeSpan? NativeTimeout = null, TimeSpan? VerificationTimeout = null);
public sealed class PackItemResult
{
    public required PackApplication Application { get; set; }
    public bool ResolveLatest { get; set; }
    public string? ResolvedChannel { get; set; }
    public string? InstalledVersion { get; set; }
    public PackItemState State { get; set; }
    public PackItemPhase? Phase { get; set; }
    public bool NativeInstallSucceeded { get; set; }
    public string Message { get; set; } = "Pendiente";
    public bool RebootRequired { get; set; }
    public bool RequiresRebootBeforeRetry { get; set; }
    public DateTime? LastAttemptBootTimeUtc { get; set; }
    public bool Verified => State is PackItemState.Succeeded or PackItemState.AlreadyInstalled;
    public PackItemResult Copy() => (PackItemResult)MemberwiseClone();
    public string ActivityLabel() => Phase switch
    {
        PackItemPhase.Queued => "En cola", PackItemPhase.Downloading => "Descargando",
        PackItemPhase.Installing => "Instalando", PackItemPhase.PostInstall => "Finalizando",
        PackItemPhase.WaitingVerification => "Esperando verificación", PackItemPhase.Verifying => "Verificando",
        _ => State switch
        {
            PackItemState.Checking => "Comprobando", PackItemState.Installing => "Instalando",
            PackItemState.Succeeded => "Instalada y verificada", PackItemState.AlreadyInstalled => "Ya instalada",
            PackItemState.Failed => "Error", PackItemState.VerificationPending => "Verificación pendiente", _ => "Pendiente"
        }
    };
    public string ActivityDescription()
    {
        var label = ActivityLabel();
        var message = Message?.Trim();
        if (string.IsNullOrEmpty(message)) return label;
        return message.StartsWith(label, StringComparison.OrdinalIgnoreCase) &&
            (message.Length == label.Length || !char.IsLetter(message[label.Length]))
            ? message : $"{label}: {message}";
    }
}
public sealed class PackRun
{
    public long CatalogRevision { get; set; }
    public PackDefinition? Definition { get; set; }
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime HostBootTimeUtc { get; set; } = DateTime.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64);
    public List<PackItemResult> Items { get; set; } = [];
    public PackRun Copy() => new()
    {
        CatalogRevision = CatalogRevision, StartedAtUtc = StartedAtUtc, HostBootTimeUtc = HostBootTimeUtc,
        Definition = Definition is null ? null : Definition with { Applications = [.. Definition.Applications] },
        Items = Items.Select(item => item.Copy()).ToList()
    };
    public string ActivitySummary() => $"{Items.Count(item => item.Verified)} / {Items.Count} verificadas · " +
        $"{Items.Count(item => item.Phase == PackItemPhase.Downloading)} descargando · " +
        $"{Items.Count(item => item.Phase is PackItemPhase.Installing or PackItemPhase.PostInstall || item.State == PackItemState.Installing && item.Phase is null)} instalando · " +
        $"{Items.Count(item => item.Phase == PackItemPhase.Verifying)} verificando";
    public bool Complete => Items.Count > 0 && Items.All(x => x.Verified);
    public bool RebootRequired => Items.Any(x => x.RebootRequired);
    public string Summary => Complete
        ? (RebootRequired ? "Todas las aplicaciones verificadas. Windows tiene cambios pendientes de reinicio." : "Todas las aplicaciones instaladas y verificadas.")
        : $"Quedan {Items.Count(x => !x.Verified)} aplicaciones pendientes o con errores. No se da el pack por completado.";
}
