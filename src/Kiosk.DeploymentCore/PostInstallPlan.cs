namespace Kiosk.Deployment;

// Local per-job callback capability. This is never a station credential or a panel administration key.
public sealed record PostInstallPlan(int SchemaVersion, DeploymentJob Job, string Server, string SessionId,
    string CallbackToken, string CertificateSha256, string? WorkerSha256, string? KioskSha256, string? KioskVersion);
