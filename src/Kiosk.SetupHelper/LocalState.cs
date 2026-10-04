using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
namespace Kiosk.SetupHelper;

internal static class LocalState
{
    public static void Prepare()
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (identity.IsSystem || !new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            throw new InvalidOperationException("Ejecuta el Setup como administrador bajo un usuario interactivo, no SYSTEM.");
        // State used by an elevated installer must not be writable by ordinary users.
        string parent = Path.GetDirectoryName(Program.StateRoot)!;
        foreach (string path in new[] { parent, Program.StateRoot })
        {
            if (Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("No se admite un enlace o redirección en la carpeta de estado.");
            var directory = Directory.CreateDirectory(path);
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(true, false);
            var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            security.SetOwner(admins);
            foreach (var (sid, rights) in new[]
            {
                (WellKnownSidType.BuiltinAdministratorsSid, FileSystemRights.FullControl),
                (WellKnownSidType.LocalSystemSid, FileSystemRights.FullControl),
                (WellKnownSidType.BuiltinUsersSid, FileSystemRights.ReadAndExecute)
            }) security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid, null), rights,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            directory.SetAccessControl(security);
        }
    }
}
