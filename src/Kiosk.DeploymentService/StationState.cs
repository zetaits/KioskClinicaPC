using System.Net;
using System.Net.NetworkInformation;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text;
using Kiosk.Deployment;

namespace Kiosk.DeploymentService;

public sealed record StationSettings(int SchemaVersion = 1, string ServerUrl = "https://panel.clinicapc.es", string? StationId = null,
    string? AdapterId = null, string? Address = null, string? Mask = null, string? Storage = null,
    string? OperatorSid = null, bool Enabled = false, int Capacity = 3);
public sealed record StationSecrets(string Credential = "", string DefaultPassword = "", string SharePassword = "", string BootstrapToken = "");
public sealed record WorkerSession(string Id, string Token, bool CommandDelivered = false);
public sealed record WorkerAuthorization(DeploymentJob Job, string Unattend, string Share, string ShareUser,
    string SharePassword, string CallbackToken, string CertificateSha256, string PostInstallSha256, string? WorkerSha256, string? KioskSha256, string? KioskVersion, string? DriverSha256);

public sealed class StationState
{
    public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ClinicaPC", "Deployment");
    private readonly object _gate = new();
    public StationSettings Settings { get; private set; }
    public StationSecrets Secrets { get; private set; }
    public DeploymentQueue Queue { get; }
    public X509Certificate2 Certificate { get; }
    public string CertificateSha256 => Convert.ToHexString(SHA256.HashData(Certificate.RawData));
    public StationState()
    {
        SecureDirectory(Root);
        Settings = AtomicState.Read(Path.Combine(Root, "settings.json"), () => new StationSettings());
        DeploymentPolicy.Require(Settings.SchemaVersion == 1, "Configuración de estación incompatible.");
        Secrets = ReadSecret<StationSecrets>("secrets.bin") ?? new(BootstrapToken: Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
        SaveSecrets(Secrets);
        Queue = new(Path.Combine(Root, "queue-v1.json"));
        byte[]? cert = ReadProtected("certificate.bin");
        if (cert is null)
        {
            using var rsa = RSA.Create(3072);
            var request = new CertificateRequest("CN=ClinicaPC Deployment Station", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));
            using var created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(2));
            cert = created.Export(X509ContentType.Pfx); WriteProtected("certificate.bin", cert);
        }
        Certificate = X509CertificateLoader.LoadPkcs12(cert, null, X509KeyStorageFlags.EphemeralKeySet);
    }
    public static void SecureDirectory(string path)
    {
        string full = Path.GetFullPath(path);
        for (var parent = new DirectoryInfo(full); parent is not null; parent = parent.Parent)
            if (parent.Exists && (parent.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Carpeta de despliegue redirigida.");
        var security = new DirectorySecurity(); security.SetAccessRuleProtection(true, false);
        foreach (var sid in new[] { WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinAdministratorsSid })
            security.AddAccessRule(new(new SecurityIdentifier(sid, null), FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        var directory = new DirectoryInfo(full); if (directory.Exists) directory.SetAccessControl(security); else directory.Create(security);
    }
    private byte[]? ReadProtected(string name) => File.Exists(Path.Combine(Root, name))
        ? ProtectedData.Unprotect(File.ReadAllBytes(Path.Combine(Root, name)), null, DataProtectionScope.LocalMachine) : null;
    private T? ReadSecret<T>(string name) => ReadProtected(name) is { } bytes ? System.Text.Json.JsonSerializer.Deserialize<T>(bytes, AtomicState.Json) : default;
    private void WriteProtected(string name, byte[] bytes)
    {
        string path = Path.Combine(Root, name), tmp = path + ".tmp";
        File.WriteAllBytes(tmp, ProtectedData.Protect(bytes, null, DataProtectionScope.LocalMachine)); File.Move(tmp, path, true);
    }
    public void SaveSecrets(StationSecrets secrets)
    { lock (_gate) { WriteProtected("secrets.bin", System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(secrets, AtomicState.Json)); Secrets = secrets; } }
    public void Configure(StationSettings settings)
    {
        DeploymentPolicy.Require(!settings.Enabled, "Activa la red mediante la comprobación previa.");
        lock (_gate)
        {
            DeploymentPolicy.Require(!Queue.Snapshot().Jobs.Any(j => DeploymentPolicy.Active(j.State)), "Espera a que terminen los trabajos activos antes de cambiar la estación.");
            AtomicState.Write(Path.Combine(Root, "settings.json"), settings); Settings = settings; Queue.Capacity(settings.Capacity);
        }
    }
    public void Enable(bool enabled)
    { lock (_gate) { AtomicState.Write(Path.Combine(Root, "settings.json"), Settings with { Enabled = enabled }); Settings = Settings with { Enabled = enabled }; } }
    public string? JobPassword(string id) { lock (_gate) return ReadSecret<string>("password-" + id + ".bin"); }
    public void JobPassword(string id, string password)
    { DeploymentPolicy.Require(DeploymentPolicy.IsId(id), "Trabajo no válido."); lock (_gate) WriteProtected("password-" + id + ".bin", System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(password)); }
    public void ClearJobPassword(string id) { lock (_gate) { string path = Path.Combine(Root, "password-" + id + ".bin"); if (File.Exists(path)) File.Delete(path); } }
    public WorkerSession NewWorker()
    {
        var worker = new WorkerSession(Guid.NewGuid().ToString("N"), Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
        lock (_gate) WriteProtected("session-" + worker.Id + ".bin", System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(worker, AtomicState.Json)); return worker;
    }
    public WorkerSession? Worker(string id, string? token)
    {
        if (!DeploymentPolicy.IsId(id) || token?.Length != 64) return null;
        lock (_gate)
        {
            var worker = ReadSecret<WorkerSession>("session-" + id + ".bin");
            return worker is not null && SecretEqual(worker.Token, token) ? worker : null;
        }
    }
    public static bool SecretEqual(string expected, string? actual) => !string.IsNullOrEmpty(actual) && CryptographicOperations.FixedTimeEquals(
        SHA256.HashData(Encoding.UTF8.GetBytes(expected)), SHA256.HashData(Encoding.UTF8.GetBytes(actual)));
    public void Delivered(WorkerSession worker) { lock (_gate) WriteProtected("session-" + worker.Id + ".bin", System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(worker with { CommandDelivered = true }, AtomicState.Json)); }
    public (IPAddress Address, IPAddress Mask) Network()
    {
        var adapter = NetworkInterface.GetAllNetworkInterfaces().SingleOrDefault(a => a.Id == Settings.AdapterId &&
            a.NetworkInterfaceType == NetworkInterfaceType.Ethernet && a.OperationalStatus == OperationalStatus.Up)
            ?? throw new InvalidDataException("Selecciona una interfaz Ethernet conectada.");
        var address = adapter.GetIPProperties().UnicastAddresses.SingleOrDefault(a => a.Address.ToString() == Settings.Address && a.IPv4Mask.ToString() == Settings.Mask)
            ?? throw new InvalidDataException("La dirección de la interfaz cambió. Revisa la configuración de red.");
        return (address.Address, address.IPv4Mask);
    }
    public bool InSubnet(IPAddress? remote)
    {
        if (remote is null || Settings.Address is null || Settings.Mask is null) return false;
        var r = remote.MapToIPv4().GetAddressBytes(); var a = IPAddress.Parse(Settings.Address).GetAddressBytes(); var m = IPAddress.Parse(Settings.Mask).GetAddressBytes();
        return r.Length == 4 && Enumerable.Range(0, 4).All(i => (r[i] & m[i]) == (a[i] & m[i]));
    }
}
