using System.Security.Cryptography;
using System.Text;
using Kiosk.Deployment;
using KioskClinicaPC.Core.Sync;

namespace Kiosk.Server.Services;

public sealed record DeploymentEnrollment(string Hash, DateTimeOffset ExpiresAtUtc, bool Used);
public sealed record DeploymentStationRecord(DeploymentStation Station, string CredentialHash,
    List<DeploymentBatch> Batches, List<DeploymentJob> Jobs);
public sealed record DeploymentServerState(int SchemaVersion, List<DeploymentEnrollment> Enrollments,
    List<DeploymentStationRecord> Stations, List<DeploymentProfile> Profiles);

public sealed class DeploymentStore
{
    private readonly object _gate = new();
    private readonly string _path;
    private readonly TimeProvider _time;
    private DeploymentServerState _state;
    public DeploymentStore(string root, TimeProvider? time = null)
    {
        _time = time ?? TimeProvider.System; _path = Path.Combine(root, "deployment-v1.json");
        _state = AtomicState.Read(_path, () => new DeploymentServerState(1, [], [],
            [new("00000000000000000000000000000001", 1, "Solo Windows", null, null, "es-ES", "Usuario", new PackCatalog(0, []), false)]));
        DeploymentPolicy.Require(_state.SchemaVersion == 1, "Estado del despliegue incompatible.");
    }
    private void Commit(DeploymentServerState next) { AtomicState.Write(_path, next); _state = next; }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static bool EqualHash(string expected, string value) => CryptographicOperations.FixedTimeEquals(
        Convert.FromHexString(expected), Convert.FromHexString(Hash(value)));
    public EnrollmentCode CreateCode()
    {
        lock (_gate)
        {
            var code = new EnrollmentCode(Convert.ToHexString(RandomNumberGenerator.GetBytes(16)), _time.GetUtcNow().AddMinutes(10));
            Commit(_state with { Enrollments = [.. _state.Enrollments.Where(e => e.ExpiresAtUtc > _time.GetUtcNow() && !e.Used), new(Hash(code.Code), code.ExpiresAtUtc, false)] });
            return code;
        }
    }
    public EnrollmentResponse Enroll(EnrollmentRequest request)
    {
        DeploymentPolicy.Require(request.ProtocolVersion == 1 && request.Code?.Length == 32 && !string.IsNullOrWhiteSpace(request.Name) && request.Name.Trim().Length <= 100,
            "Código o versión de la estación no válido.");
        lock (_gate)
        {
            var enrollment = _state.Enrollments.Find(e => EqualHash(e.Hash, request.Code) && !e.Used && e.ExpiresAtUtc > _time.GetUtcNow())
                ?? throw new InvalidDataException("Código caducado o ya utilizado. Genera otro código en el panel.");
            var response = new EnrollmentResponse(1, Guid.NewGuid().ToString("N"), Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
            Commit(_state with { Enrollments = _state.Enrollments.Select(e => e == enrollment ? e with { Used = true } : e).ToList(),
                Stations = [.. _state.Stations, new(new(response.StationId, request.Name.Trim(), null, false, [], []), Hash(response.Credential), [], [])] });
            return response;
        }
    }
    public string? Authenticate(string? credential)
    {
        if (credential?.Length != 64) return null;
        lock (_gate) return _state.Stations.Find(s => !s.Station.Revoked && EqualHash(s.CredentialHash, credential))?.Station.Id;
    }
    public void Revoke(string id)
    {
        lock (_gate)
        {
            DeploymentPolicy.Require(_state.Stations.Any(s => s.Station.Id == id), "Estación desconocida.");
            Commit(_state with { Stations = _state.Stations.Select(s => s.Station.Id == id ? s with { Station = s.Station with { Revoked = true } } : s).ToList() });
        }
    }
    public DeploymentPanelSnapshot Snapshot()
    {
        lock (_gate) return DeploymentPolicy.Copy(new DeploymentPanelSnapshot(_state.Stations.Select(s => s.Station).ToList(),
            _state.Profiles.GroupBy(p => p.Id).Select(g => g.MaxBy(p => p.Revision)!).ToList(),
            _state.Stations.ToDictionary(s => s.Station.Id, s => s.Batches), _state.Stations.ToDictionary(s => s.Station.Id, s => s.Jobs)));
    }
    public DeploymentConfiguration Configuration(string id, PackCatalog catalog)
    {
        lock (_gate)
        {
            var station = _state.Stations.Single(s => s.Station.Id == id && !s.Station.Revoked);
            return DeploymentPolicy.Copy(new DeploymentConfiguration(1,
                _state.Profiles.GroupBy(p => p.Id).Select(g => g.MaxBy(p => p.Revision)!).ToList(), station.Station.Sessions, catalog));
        }
    }
    public DeploymentProfile SaveProfile(ProfileRequest request, PackCatalog catalog)
    {
        DeploymentPolicy.Username(request.Username);
        DeploymentPolicy.Require(request.Language == "es-ES" && !string.IsNullOrWhiteSpace(request.Name) && request.Name.Trim().Length <= 100 && request.ApplicationIds is not null &&
            request.ApplicationIds.Count <= 1000 && request.ApplicationIds.Distinct().Count() == request.ApplicationIds.Count &&
            request.ApplicationIds.All(id => catalog.Applications.Any(a => a.Id == id)), "Perfil no válido. Revisa idioma y aplicaciones.");
        lock (_gate)
        {
            var previous = request.Id is null ? null : _state.Profiles.Where(p => p.Id == request.Id).MaxBy(p => p.Revision);
            DeploymentPolicy.Require(request.ExpectedRevision == (previous?.Revision ?? 0) && (request.Id is null || previous is not null),
                "El perfil ha cambiado. Recarga antes de guardar.");
            DeploymentPolicy.Require((request.ImageId is null && request.EditionIndex is null) || _state.Stations.Any(s =>
                !s.Station.Revoked && s.Station.Images.Any(i => i.Id == request.ImageId && i.Verified && i.Editions.Any(e => e.Index == request.EditionIndex))),
                "Selecciona una imagen y edición disponibles en una estación vinculada.");
            var profile = new DeploymentProfile(previous?.Id ?? Guid.NewGuid().ToString("N"), (previous?.Revision ?? 0) + 1,
                request.Name.Trim(), request.ImageId, request.EditionIndex, request.Language, request.Username,
                DeploymentPolicy.Copy(new PackCatalog(catalog.Revision, catalog.Applications.Where(a => request.ApplicationIds.Contains(a.Id)).ToList())), request.Kiosk);
            Commit(_state with { Profiles = [.. _state.Profiles, profile] }); return DeploymentPolicy.Copy(profile);
        }
    }
    public void PendingUsername(string stationId, string sessionId, PendingUsernameRequest request)
    {
        DeploymentPolicy.Username(request.Username);
        lock (_gate)
        {
            var station = _state.Stations.Single(s => s.Station.Id == stationId && !s.Station.Revoked);
            var session = station.Station.Sessions.Single(s => s.Id == sessionId);
            DeploymentPolicy.Require(session.State == DeploymentState.Ready && !station.Jobs.Any(j => j.SessionId == sessionId) &&
                request.ExpectedRevision == session.OptionsRevision, "El destino ya está confirmado o sus opciones han cambiado.");
            var next = station with { Station = station.Station with { Sessions = station.Station.Sessions.Select(s => s.Id == sessionId
                ? s with { PendingUsername = request.Username, OptionsRevision = s.OptionsRevision + 1 } : s).ToList() } };
            Commit(_state with { Stations = _state.Stations.Select(s => s.Station.Id == stationId ? next : s).ToList() });
        }
    }
    public void Sync(string id, DeploymentInventory inventory)
    {
        DeploymentPolicy.Require(inventory.ProtocolVersion == 1 && inventory.Capacity is >= 1 and <= 8 && inventory.Images is not null &&
            inventory.Sessions is not null && inventory.Jobs is not null && inventory.Batches is not null &&
            inventory.Images.Count <= 100 && inventory.Sessions.Count <= 1000 && inventory.Jobs.Count <= 10000 && inventory.Batches.Count <= 10000,
            "Inventario incompatible o demasiado grande.");
        foreach (var image in inventory.Images) DeploymentPolicy.Image(image);
        DeploymentPolicy.Require(inventory.Images.Select(i => i.Id).Distinct().Count() == inventory.Images.Count &&
            inventory.Sessions.All(s => DeploymentPolicy.IsId(s.Id) && s.Hardware.Disks.Count <= 64) &&
            inventory.Sessions.Select(s => s.Id).Distinct().Count() == inventory.Sessions.Count &&
            inventory.Jobs.All(j => DeploymentPolicy.IsId(j.Id) && inventory.Sessions.Any(s => s.Id == j.SessionId) &&
                (j.State != DeploymentState.Completed || j.WindowsVerified && j.AccountVerified && j.ComponentsVerified)) &&
            inventory.Jobs.Select(j => j.Id).Distinct().Count() == inventory.Jobs.Count, "Inventario de seguimiento no válido.");
        lock (_gate)
        {
            var previous = _state.Stations.Single(s => s.Station.Id == id && !s.Station.Revoked);
            foreach (var job in inventory.Jobs.Where(j => !previous.Jobs.Any(p => p.Id == j.Id)))
            {
                var session = previous.Station.Sessions.Find(s => s.Id == job.SessionId);
                var profile = _state.Profiles.Where(p => p.Id == job.Profile.Id).MaxBy(p => p.Revision);
                DeploymentPolicy.Require(job.State == DeploymentState.Queued && !job.DestructiveStarted && session is not null &&
                    session.State == DeploymentState.Ready && session.OptionsRevision == job.OptionsRevision &&
                    (session.PendingUsername is null || session.PendingUsername == job.Username) && profile is not null &&
                    System.Text.Json.JsonSerializer.Serialize(profile) == System.Text.Json.JsonSerializer.Serialize(job.Profile),
                    "Las opciones o el perfil cambiaron antes de confirmar. Revisa el lote en la estación.");
            }
            foreach (var job in inventory.Jobs.Where(j => previous.Jobs.Any(p => p.Id == j.Id)))
            {
                var old = previous.Jobs.Single(j => j.Id == job.Id);
                DeploymentPolicy.Require(job.Username == old.Username && job.SessionId == old.SessionId && job.Disk == old.Disk &&
                    job.HardwareFingerprint == old.HardwareFingerprint && job.WindowsEditionId == old.WindowsEditionId &&
                    job.WindowsBuild == old.WindowsBuild && job.DriverSha256 == old.DriverSha256 && job.OptionsRevision == old.OptionsRevision &&
                    System.Text.Json.JsonSerializer.Serialize(job.Profile) == System.Text.Json.JsonSerializer.Serialize(old.Profile),
                    "No se pueden modificar opciones de un trabajo confirmado.");
            }
            var sessions = inventory.Sessions.Select(s =>
            {
                var old = previous.Station.Sessions.Find(p => p.Id == s.Id);
                return !inventory.Jobs.Any(j => j.SessionId == s.Id) && old is not null && old.OptionsRevision > s.OptionsRevision
                    ? s with { PendingUsername = old.PendingUsername, OptionsRevision = old.OptionsRevision } : s;
            }).ToList();
            DeploymentPolicy.Require(previous.Jobs.All(j => inventory.Jobs.Any(n => n.Id == j.Id && n.LastSequence >= j.LastSequence)),
                "La estación ha perdido trabajos o está enviando un estado anterior.");
            var next = new DeploymentStationRecord(previous.Station with { LastSeenUtc = _time.GetUtcNow(), Images = DeploymentPolicy.Copy(inventory.Images),
                Sessions = DeploymentPolicy.Copy(sessions), Capacity = inventory.Capacity }, previous.CredentialHash,
                DeploymentPolicy.Copy(inventory.Batches), DeploymentPolicy.Copy(inventory.Jobs));
            Commit(_state with { Stations = _state.Stations.Select(s => s.Station.Id == id ? next : s).ToList() });
        }
    }
}
