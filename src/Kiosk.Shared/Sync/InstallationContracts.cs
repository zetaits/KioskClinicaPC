namespace KioskClinicaPC.Core.Sync
{
    public enum InstallerPackageKind
    {
        Msi,
        InnoSetup,
        Nsis
    }

    public enum InstallationJobState
    {
        Pending,
        Dispatched,
        Downloading,
        Verifying,
        Installing,
        Succeeded,
        RebootRequired,
        Failed
    }

    public enum MaintenanceJobState
    {
        Pending,
        Dispatched,
        Uninstalling,
        Succeeded,
        RebootRequired,
        Failed
    }

    /// <summary>Manifiesto inmutable que el agente obtiene del servidor para un trabajo autorizado.</summary>
    public sealed class InstallationManifest
    {
        public string JobId { get; set; } = "";
        public string DeviceId { get; set; } = "";
        public string PackageId { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string FileName { get; set; } = "";
        public InstallerPackageKind Kind { get; set; }
        public long SizeBytes { get; set; }
        public string Sha256 { get; set; } = "";
        public bool AllowUnsigned { get; set; }
    }

    public sealed class InstallationStatusUpdate
    {
        public string DeviceId { get; set; } = "";
        public InstallationJobState State { get; set; }
        public int? ProgressPercent { get; set; }
        public int? ExitCode { get; set; }
        public string? Message { get; set; }
    }

    /// <summary>Aplicacion que el instalador inicial puede ofrecer al preparar un equipo.</summary>
    public sealed class InitialSetupPackage
    {
        public string Id { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public InstallerPackageKind Kind { get; set; }
        public long SizeBytes { get; set; }
        public string Sha256 { get; set; } = "";
        public bool SelectedByDefault { get; set; }
        public int Order { get; set; }
    }

    public sealed class InitialSetupCatalog
    {
        public List<InitialSetupPackage> Packages { get; set; } = new();
    }

    public sealed class InitialSetupSessionRequest
    {
        public string MachineName { get; set; } = "";
        public string SetupVersion { get; set; } = "";
        public List<string> PackageIds { get; set; } = new();
    }

    public sealed class InitialSetupSessionResponse
    {
        public string SessionId { get; set; } = "";
        public string Token { get; set; } = "";
        public List<InstallationManifest> Packages { get; set; } = new();
    }

    public sealed class InitialSetupStatusUpdate
    {
        public InstallationJobState State { get; set; }
        public int? ProgressPercent { get; set; }
        public int? ExitCode { get; set; }
        public string? Message { get; set; }
    }

    public sealed class MaintenanceStatusUpdate
    {
        public string DeviceId { get; set; } = "";
        public MaintenanceJobState State { get; set; }
        public int? ExitCode { get; set; }
        public string? Message { get; set; }
    }

    /// <summary>Petición local del cliente WPF al servicio privilegiado.</summary>
    public sealed class InstallerAgentRequest
    {
        public string Operation { get; set; } = "install";
        public string? ServerUrl { get; set; }
        public string? ApiKey { get; set; }
        public string? DeviceId { get; set; }
        public string? JobId { get; set; }
        public string? Token { get; set; }
        public int? KioskProcessId { get; set; }
        public string? UserDataDirectory { get; set; }
        public KioskUpdateAssignment? UpdateAssignment { get; set; }
    }

    public sealed class InstallerAgentResponse
    {
        public bool Accepted { get; set; }
        public string? AgentVersion { get; set; }
        public bool CanUninstallKiosk { get; set; }
        public string? Error { get; set; }
    }

    /// <summary>
    /// Trabajo sellado que el servicio SYSTEM entrega al runner de mantenimiento. No contiene una
    /// línea de comandos libre: el runner construye siempre los argumentos permitidos.
    /// </summary>
    public sealed class KioskUninstallRunnerRequest
    {
        public string UninstallerPath { get; set; } = "";
        public string InstallDirectory { get; set; } = "";
        public string UserSid { get; set; } = "";
        public string UserDataDirectory { get; set; } = "";
        public int KioskProcessId { get; set; }
        public string? ServerUrl { get; set; }
        public string? ApiKey { get; set; }
        public string? DeviceId { get; set; }
        public string? JobId { get; set; }
        public string? Token { get; set; }
    }
}
