using KioskClinicaPC.Core.Sync;
using Xunit;

namespace Kiosk.Server.Tests;

public sealed class PackInstalledPolicyTests
{
    [Theory]
    [InlineData("4.6", "System", 0, PackInstalledDisposition.Verified)] // Observed IZArc COM evidence
    [InlineData("7.6.2.1", "System", -1, PackInstalledDisposition.UpgradeRequired)] // Observed LibreOffice evidence
    [InlineData("5.0", "System", 1, PackInstalledDisposition.Verified)]
    [InlineData("4.6", "system", 0, PackInstalledDisposition.Verified)]
    [InlineData("4.6", "User", 0, PackInstalledDisposition.UserScope)]
    [InlineData("4.6", "User", 1, PackInstalledDisposition.UserScope)]
    [InlineData("4.6", "", 0, PackInstalledDisposition.UnknownScope)]
    [InlineData("4.6", null, 0, PackInstalledDisposition.UnknownScope)]
    [InlineData("4.6", "Machine", 0, PackInstalledDisposition.UnknownScope)] // YAML isn't the COM contract
    [InlineData("4.6", "System", null, PackInstalledDisposition.UnknownVersion)]
    [InlineData("", "System", 0, PackInstalledDisposition.UnknownVersion)]
    [InlineData(null, "System", 0, PackInstalledDisposition.UnknownVersion)]
    public void Native_evidence_controls_verification_and_upgrade_without_accepting_unknown_scope_or_version(
        string? version, string? scope, int? comparison, PackInstalledDisposition expected)
        => Assert.Equal(expected, PackInstalledPolicy.Evaluate(true, version, scope, comparison));

    [Fact]
    public void Missing_installed_package_is_not_a_verified_application()
        => Assert.Equal(PackInstalledDisposition.NotInstalled, PackInstalledPolicy.Evaluate(false, null, null, null));
}
