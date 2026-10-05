using System.IO.Pipes;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Kiosk.Deployment;

namespace Kiosk.DeploymentService;

public sealed class LocalControl(StationState state, PanelConnection panel, ImageLibrary images, NetworkBoot boot, DriverLibrary drivers) : BackgroundService
{
    private static readonly JsonSerializerOptions Json = new(AtomicState.Json) { WriteIndented = false };
    private NamedPipeServerStream Pipe()
    {
        var acl = new PipeSecurity(); acl.SetAccessRuleProtection(true, false);
        acl.AddAccessRule(new(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
        foreach (var sid in new[] { new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null) })
            acl.AddAccessRule(new(sid, PipeAccessRights.FullControl, AccessControlType.Allow));
        if (state.Settings.OperatorSid is { } authorized)
            acl.AddAccessRule(new(new SecurityIdentifier(authorized), PipeAccessRights.ReadWrite, AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(DeploymentPipe.Name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous, 65536, 65536, acl);
    }
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // A single local mutator prevents concurrent configuration and double-click confirmation races.
        while (!ct.IsCancellationRequested)
        {
            using var pipe = Pipe();
            try
            {
                await pipe.WaitForConnectionAsync(ct);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromHours(2));
                using var reader = new StreamReader(pipe, Encoding.UTF8, false, 65536, true);
                using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 65536, true) { AutoFlush = true };
                var buffer = new StringBuilder(); var character = new char[1];
                while (await reader.ReadAsync(character.AsMemory(), timeout.Token) > 0 && character[0] != '\n')
                { if (buffer.Length >= 64 * 1024) throw new InvalidDataException("Petición local demasiado grande."); buffer.Append(character[0]); }
                LocalDeploymentResponse result;
                try
                {
                    var request = JsonSerializer.Deserialize<LocalDeploymentRequest>(buffer.ToString(), Json) ?? throw new InvalidDataException("Petición vacía.");
                    object data = await Handle(request, timeout.Token);
                    result = new(true, JsonSerializer.SerializeToElement(data, Json));
                }
                catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException or InvalidOperationException or JsonException or HttpRequestException or SocketException)
                { result = new(false, null, ex is InvalidDataException ? ex.Message : "La operación falló. Comprueba conexión, permisos, disco y servicio; vuelve a intentarlo."); }
                await writer.WriteLineAsync(JsonSerializer.Serialize(result, Json));
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or InvalidDataException) { }
        }
    }
    private static T Parse<T>(JsonElement payload) => payload.Deserialize<T>(Json) ?? throw new InvalidDataException("Petición incompatible.");
    private async Task<object> Handle(LocalDeploymentRequest request, CancellationToken ct)
    {
        switch (request.Operation)
        {
            case "snapshot":
                var q = state.Queue.Snapshot();
                return new LocalStationView(state.Settings.StationId is not null, panel.Connected, state.Settings.Enabled,
                    state.Settings.AdapterId, state.Settings.Storage, state.Secrets.DefaultPassword.Length >= 8,
                    new(q.Capacity, q.Sessions, q.Images, q.Batches, q.Jobs), panel.Configuration, Adapters(), boot.Devices);
            case "enroll":
                DeploymentPolicy.Require(state.Settings.StationId is null, "Esta estación ya está vinculada.");
                var enrollment = Parse<LocalEnrollment>(request.Payload);
                await panel.Enroll(enrollment.Server, enrollment.Code, enrollment.Name, ct); break;
            case "network":
                DeploymentPolicy.Require(!state.Settings.Enabled, "Desactiva PXE antes de cambiar la red.");
                var network = Parse<LocalNetwork>(request.Payload);
                DeploymentPolicy.Require(Adapters().Any(a => a.Id == network.AdapterId && a.Address == network.Address && a.Mask == network.Mask) &&
                    network.Capacity is >= 1 and <= 8 && Path.IsPathFullyQualified(network.Storage) && !network.Storage.StartsWith("\\\\"), "Selecciona una interfaz Ethernet y una carpeta local válidas.");
                string storageRoot = Path.GetFullPath(network.Storage);
                DeploymentPolicy.Require(storageRoot.TrimEnd('\\') != Path.GetPathRoot(storageRoot)?.TrimEnd('\\') &&
                    (!Directory.Exists(storageRoot) || !Directory.EnumerateFileSystemEntries(storageRoot).Any() ||
                     File.Exists(Path.Combine(storageRoot, ".clinicapc-deployment-library"))), "Selecciona una carpeta vacía dedicada a las imágenes de despliegue.");
                StationState.SecureDirectory(network.Storage);
                File.WriteAllText(Path.Combine(storageRoot, ".clinicapc-deployment-library"), "ClínicaPC Deployment v1");
                state.Configure(state.Settings with { AdapterId = network.AdapterId, Address = network.Address, Mask = network.Mask,
                    Storage = Path.GetFullPath(network.Storage), Capacity = network.Capacity, Enabled = false }); break;
            case "password":
                var password = Parse<string>(request.Payload);
                DeploymentPolicy.Require(password.Length is >= 8 and <= 128 && !password.Any(char.IsControl), "La contraseña debe tener entre 8 y 128 caracteres.");
                state.SaveSecrets(state.Secrets with { DefaultPassword = password }); break;
            case "import":
                DeploymentPolicy.Require(!state.Settings.Enabled && !state.Queue.Snapshot().Jobs.Any(j => DeploymentPolicy.Active(j.State)), "Desactiva PXE y espera a los trabajos antes de importar imágenes.");
                await images.Import(Parse<string>(request.Payload), ct); break;
            case "drivers": await drivers.Import(Parse<string>(request.Payload), ct); break;
            case "scan": return await boot.Scan(ct);
            case "activate":
                try { await panel.Synchronize(ct); }
                catch (Exception ex) when (state.Settings.StationId is not null && ex is HttpRequestException or InvalidDataException or OperationCanceledException)
                {
                    DeploymentPolicy.Require(state.Queue.Snapshot().Jobs.Any(j => j.PanelAccepted), "Conecta al panel para activar la estación por primera vez.");
                }
                var retainedQueue = state.Queue.Snapshot();
                foreach (var retainedImage in retainedQueue.Images.Where(i => retainedQueue.Jobs.Any(j => j.State == DeploymentState.Queued && j.Profile.ImageId == i.Id)))
                    DeploymentPolicy.Require(await Task.Run(() => images.Verify(retainedImage), ct), "Una imagen de la cola ha cambiado. Revisa los trabajos antes de reactivar PXE.");
                await boot.Activate(ct); break;
            case "deactivate": boot.Deactivate(); break;
            case "maintenance":
                DeploymentPolicy.Require(!state.Queue.Snapshot().Jobs.Any(j => DeploymentPolicy.Active(j.State) || j.State == DeploymentState.Attention && j.DestructiveStarted),
                    "Hay trabajos activos o resultados inciertos. Revísalos antes de actualizar o desinstalar.");
                boot.Deactivate(); break;
            case "sync": await panel.Synchronize(ct); break;
            case "capacity": state.Queue.Capacity(Parse<int>(request.Payload)); break;
            case "confirm":
                var confirmation = Parse<LocalConfirmation>(request.Payload);
                DeploymentPolicy.Require(state.Settings.Enabled, "Activa el servicio PXE antes de confirmar.");
                await panel.Synchronize(ct);
                var profile = panel.Configuration!.Profiles.SingleOrDefault(p => p.Id == confirmation.Confirmation.ProfileId)
                    ?? throw new InvalidDataException("El perfil ya no está disponible.");
                var selectedImage = state.Queue.Snapshot().Images.SingleOrDefault(i => i.Id == profile.ImageId)
                    ?? throw new InvalidDataException("Importa la imagen del perfil en esta estación.");
                DeploymentPolicy.Require(await Task.Run(() => images.Verify(selectedImage), ct), "La imagen cambió o está incompleta. Revisa su integridad antes de confirmar.");
                // Full image hashing may take minutes on a HDD; re-fetch pending options afterward.
                await panel.Synchronize(ct);
                DeploymentPolicy.Require(panel.Configuration!.Profiles.Any(p => p.Id == profile.Id && p.Revision == profile.Revision), "El perfil cambió durante la verificación. Revisa el lote.");
                var passwords = confirmation.Confirmation.Targets.ToDictionary(t => t.SessionId,
                    t => confirmation.Passwords.GetValueOrDefault(t.SessionId, state.Secrets.DefaultPassword));
                DeploymentPolicy.Require(passwords.Values.All(p => p.Length is >= 8 and <= 128 && !p.Any(char.IsControl)), "Configura una contraseña válida para cada instalación.");
                var driverBoot = AtomicState.Read<DriverBoot?>(Path.Combine(StationState.Root, "driver-boot.json"), () => null);
                var batch = state.Queue.Confirm(confirmation.Confirmation, profile, panel.Connected, DateTimeOffset.UtcNow, driverBoot?.PackageHash);
                foreach (var job in state.Queue.Snapshot().Jobs.Where(j => j.BatchId == batch.Id)) state.JobPassword(job.Id, passwords[job.SessionId]);
                // Disk erasure stays blocked until the panel acknowledges this exact frozen revision.
                await panel.Synchronize(ct); return batch;
            case "cancel": state.Queue.Cancel(Parse<string>(request.Payload)); break;
            case "resume-components":
                var recovery = Parse<LocalRecovery>(request.Payload);
                DeploymentPolicy.Require(recovery.PhysicallyReviewed, "Confirma la revisión física del destino y de los instaladores activos.");
                state.Queue.ResumeComponents(recovery.JobId, recovery.PhysicallyReviewed); break;
            case "close-failure":
                var closed = Parse<LocalRecovery>(request.Payload);
                DeploymentPolicy.Require(closed.PhysicallyReviewed, "Confirma que el destino no tiene instaladores activos antes de cerrar la incidencia.");
                state.Queue.CloseReviewedFailure(closed.JobId); break;
            case "diagnostics":
                var diagnostic = state.Queue.Snapshot();
                // Export only safe typed fields; never copy DPAPI blobs, answer files, settings or headers.
                return new { Version = 1, Jobs = diagnostic.Jobs.Select(j => new { j.Id, j.State, j.Phase, j.LastSequence,
                    j.WindowsVerified, j.AccountVerified, j.ComponentsVerified, j.RebootRequired }), Images = diagnostic.Images.Select(i => new { i.Id, i.Build, i.Verified }) };
            default: throw new InvalidDataException("Operación local desconocida.");
        }
        return new { ok = true };
    }
    private static List<EthernetAdapter> Adapters() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(a => a.NetworkInterfaceType == NetworkInterfaceType.Ethernet && a.OperationalStatus == OperationalStatus.Up)
        .SelectMany(a => a.GetIPProperties().UnicastAddresses.Where(ip => ip.Address.AddressFamily == AddressFamily.InterNetwork &&
            !ip.Address.ToString().StartsWith("169.254.")).Select(ip => new EthernetAdapter(a.Id, a.Name, ip.Address.ToString(), ip.IPv4Mask.ToString()))).ToList();
}
