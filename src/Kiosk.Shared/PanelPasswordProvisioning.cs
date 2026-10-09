using KioskClinicaPC.Core;

namespace KioskClinicaPC.Equipment;

/// <summary>Private installer bootstrap. Contains a verifier, never a recoverable password.</summary>
public sealed record PanelPasswordProvisioning(int SchemaVersion, string PasswordHash, int PasswordPolicyVersion)
{
    public const int CurrentPasswordPolicyVersion = 1;
    public const string FileName = "KioskPanelPassword.json";
    public bool IsCompatible() => SchemaVersion == 1 && PasswordPolicyVersion == CurrentPasswordPolicyVersion &&
        PasswordService.IsValidHash(PasswordHash);
    public override string ToString() => "PanelPasswordProvisioning (private verifier)";
}
