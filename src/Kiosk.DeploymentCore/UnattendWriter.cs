using System.Xml.Linq;

namespace Kiosk.Deployment;

public static class UnattendWriter
{
    private static readonly XNamespace Ns = "urn:schemas-microsoft-com:unattend";
    private static readonly XNamespace Wcm = "http://schemas.microsoft.com/WMIConfig/2002/State";
    private static XElement E(string name, params object?[] values) => new(Ns + name, values);
    private static XElement Component(string name, params XElement[] children) => new(Ns + "component",
        new XAttribute("name", name), new XAttribute("processorArchitecture", "amd64"),
        new XAttribute("publicKeyToken", "31bf3856ad364e35"), new XAttribute("language", "neutral"),
        new XAttribute("versionScope", "nonSxS"), children);
    private static XElement Action(string name, params XElement[] children) => new(Ns + name, new XAttribute(Wcm + "action", "add"), children);
    public static string Create(DeploymentJob job, string password)
    {
        DeploymentPolicy.Username(job.Username);
        DeploymentPolicy.Require(DeploymentPolicy.Candidate(job.Disk) && job.Profile.EditionIndex > 0 &&
            password.Length is >= 8 and <= 128 && !password.Any(char.IsControl), "Disco, edición o contraseña no válidos.");
        int disk = job.Disk.Number;
        var language = Component("Microsoft-Windows-International-Core-WinPE", E("SetupUILanguage", E("UILanguage", "es-ES")),
            E("InputLocale", "040a:0000040a"), E("SystemLocale", "es-ES"), E("UILanguage", "es-ES"), E("UserLocale", "es-ES"));
        var partitions = E("CreatePartitions", new[] { (1, "EFI", 260), (2, "MSR", 16), (3, "Primary", 0) }.Select(p =>
            Action("CreatePartition", E("Order", p.Item1), E("Type", p.Item2), p.Item3 > 0 ? E("Size", p.Item3) : E("Extend", true))));
        var modifications = E("ModifyPartitions", Action("ModifyPartition", E("Order", 1), E("PartitionID", 1), E("Format", "FAT32"), E("Label", "System")),
            Action("ModifyPartition", E("Order", 2), E("PartitionID", 3), E("Format", "NTFS"), E("Label", "Windows"), E("Letter", "W")));
        var setup = Component("Microsoft-Windows-Setup", E("DiskConfiguration", Action("Disk", E("DiskID", disk), E("WillWipeDisk", true), partitions, modifications)),
            E("ImageInstall", E("OSImage", E("InstallFrom", E("MetaData", new XAttribute(Wcm + "action", "add"), E("Key", "/IMAGE/INDEX"), E("Value", job.Profile.EditionIndex))),
                E("InstallTo", E("DiskID", disk), E("PartitionID", 3)), E("WillShowUI", "OnError"))),
            E("UserData", E("AcceptEula", true)));
        var secret = E("Password", E("Value", password), E("PlainText", true));
        var shell = Component("Microsoft-Windows-Shell-Setup", E("TimeZone", "Romance Standard Time"),
            E("UserAccounts", E("LocalAccounts", Action("LocalAccount", E("Name", job.Username), E("DisplayName", job.Username), E("Group", "Administrators"), new XElement(secret)))),
            E("AutoLogon", E("Username", job.Username), E("Enabled", true), E("LogonCount", 1), new XElement(secret)),
            E("OOBE", E("HideEULAPage", true), E("HideWirelessSetupInOOBE", true), E("HideOnlineAccountScreens", true), E("ProtectYourPC", 3)),
            E("FirstLogonCommands", Action("SynchronousCommand", E("Order", 1),
                E("CommandLine", "C:\\ProgramData\\ClinicaPC\\DeploymentJob\\Kiosk.DeploymentPostInstall.exe"), E("Description", "Preparación ClínicaPC"))));
        var xml = new XDocument(new XDeclaration("1.0", "utf-8", null), new XElement(Ns + "unattend", new XAttribute(XNamespace.Xmlns + "wcm", Wcm),
            new XElement(Ns + "settings", new XAttribute("pass", "windowsPE"), language, setup),
            new XElement(Ns + "settings", new XAttribute("pass", "oobeSystem"), shell,
                Component("Microsoft-Windows-International-Core", E("InputLocale", "040a:0000040a"), E("SystemLocale", "es-ES"), E("UILanguage", "es-ES"), E("UserLocale", "es-ES")))));
        return xml.Declaration + Environment.NewLine + xml;
    }
}
