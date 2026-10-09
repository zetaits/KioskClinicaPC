namespace KioskClinicaPC.Core.Sync;

public enum PackInstalledDisposition { NotInstalled, Verified, UpgradeRequired, UserScope, UnknownScope, UnknownVersion }
public sealed record PackInstalledEvidence(bool Installed, string? Version, string? ApiScope, int? Comparison)
{
    public PackInstalledDisposition Disposition => PackInstalledPolicy.Evaluate(Installed, Version, ApiScope, Comparison);
}

/// <summary>Policy for installed-package evidence supplied by the WinGet COM API.
/// InstalledScope uses System/User, unlike the machine/user values in YAML manifests.
/// Version ordering must come from WinGet, never a guessed parser.</summary>
public static class PackInstalledPolicy
{
    public static PackInstalledDisposition Evaluate(bool installed, string? version, string? apiScope, int? comparison)
    {
        if (!installed) return PackInstalledDisposition.NotInstalled;
        if (string.Equals(apiScope, "User", StringComparison.OrdinalIgnoreCase)) return PackInstalledDisposition.UserScope;
        if (!string.Equals(apiScope, "System", StringComparison.OrdinalIgnoreCase)) return PackInstalledDisposition.UnknownScope;
        if (string.IsNullOrWhiteSpace(version) || comparison is null) return PackInstalledDisposition.UnknownVersion;
        return comparison >= 0 ? PackInstalledDisposition.Verified : PackInstalledDisposition.UpgradeRequired;
    }
}
