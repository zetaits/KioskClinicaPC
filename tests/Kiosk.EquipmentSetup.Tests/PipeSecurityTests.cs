using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using Kiosk.EquipmentSetup;
using Xunit;
namespace Kiosk.EquipmentSetup.Tests;
public sealed class PipeSecurityTests
{
    [Fact]
    public void Local_pipe_allows_initiator_and_administrators_and_denies_network_accounts()
    {
        using var pipe = WorkerPipe.CreateServer("ClinicaPC.Equipment." + Guid.NewGuid().ToString("N"));
        var rules = pipe.GetAccessControl().GetAccessRules(true, false, typeof(SecurityIdentifier)).Cast<PipeAccessRule>().ToList();
        Assert.Contains(rules, r => r.IdentityReference == WindowsIdentity.GetCurrent().User && r.AccessControlType == AccessControlType.Allow);
        Assert.Contains(rules, r => r.IdentityReference == new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null) && r.AccessControlType == AccessControlType.Allow);
        Assert.Contains(rules, r => r.IdentityReference == new SecurityIdentifier(WellKnownSidType.NetworkSid, null) && r.AccessControlType == AccessControlType.Deny);
    }
}
