using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Kiosk.EquipmentSetup;
internal static class MachineState
{
    internal static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ClinicaPC", "Setup");
    internal static void RequireElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (identity.IsSystem || !Environment.UserInteractive || !new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            throw new InvalidOperationException("Ejecuta el modo silencioso elevado bajo un usuario interactivo, nunca SYSTEM.");
    }
    internal static string Prepare()
    {
        RequireElevated();
        foreach (string path in new[] { Path.GetDirectoryName(Root)!, Root, Path.Combine(Root, "work"), Path.Combine(Root, "logs"), Path.Combine(Root, "cache") })
        {
            KioskClinicaPC.Equipment.SetupComponentCache.SafePath(path);
            if (Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Carpeta de estado redirigida.");
            var security = new DirectorySecurity(); security.SetAccessRuleProtection(true, false);
            var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null); security.SetOwner(admins);
            foreach (var sid in new[] { admins, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null) })
                security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            if (path != Path.Combine(Root, "work") && path != Path.Combine(Root, "cache")) security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.ReadAndExecute,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            var directory = new DirectoryInfo(path);
            if (directory.Exists) directory.SetAccessControl(security); else directory.Create(security);
        }
        string work = Path.Combine(Root, "work", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(work); return work;
    }
}
