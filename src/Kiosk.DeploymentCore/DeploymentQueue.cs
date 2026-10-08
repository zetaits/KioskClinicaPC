namespace Kiosk.Deployment;

public sealed record DeploymentQueueState(int SchemaVersion, int Capacity, List<BootSession> Sessions,
    List<DeploymentImage> Images, List<DeploymentBatch> Batches, List<DeploymentJob> Jobs,
    Dictionary<string, DeploymentConfirmation> Confirmations);

/// <summary>The station is the execution authority. Persist a destructive claim before exposing a job to WinPE.</summary>
public sealed class DeploymentQueue
{
    private readonly object _gate = new();
    private readonly string _path;
    private DeploymentQueueState _state;
    public DeploymentQueue(string path)
    {
        _path = path;
        _state = AtomicState.Read(path, () => new DeploymentQueueState(1, 3, [], [], [], [], []));
        DeploymentPolicy.Require(_state.SchemaVersion == 1, "Estado local incompatible.");
        // We cannot infer whether Setup or an installer was still running when the station stopped.
        if (_state.Jobs.Any(j => DeploymentPolicy.Active(j.State)))
            Commit(_state with { Jobs = _state.Jobs.Select(j => DeploymentPolicy.Active(j.State)
                ? j with { State = DeploymentState.Attention, Phase = "Estación reiniciada; revisa el destino. No se repetirá el borrado." } : j).ToList(),
                Sessions = _state.Sessions.Select(s => _state.Jobs.Any(j => j.SessionId == s.Id && DeploymentPolicy.Active(j.State))
                    ? s with { State = DeploymentState.Attention } : s).ToList() });
    }
    private void Commit(DeploymentQueueState next) { AtomicState.Write(_path, next); _state = next; }
    public DeploymentQueueState Snapshot() { lock (_gate) return DeploymentPolicy.Copy(_state); }
    public void Capacity(int capacity)
    {
        DeploymentPolicy.Require(capacity is >= 1 and <= 8, "La capacidad debe estar entre 1 y 8.");
        lock (_gate) Commit(_state with { Capacity = capacity });
    }
    public void Observe(BootSession session)
    {
        session = DeploymentPolicy.Copy(session);
        DeploymentPolicy.Require(DeploymentPolicy.IsId(session.Id) && session.Hardware.Disks.Count <= 64 &&
            session.Hardware.Disks.Select(d => d.UniqueId).Distinct().Count() == session.Hardware.Disks.Count,
            "Inventario de discos no válido.");
        lock (_gate)
        {
            var previous = _state.Sessions.Find(s => s.Id == session.Id);
            var job = _state.Jobs.LastOrDefault(j => j.SessionId == session.Id);
            var observed = session with { PendingUsername = previous?.PendingUsername, OptionsRevision = previous?.OptionsRevision ?? 0,
                State = job?.State ?? DeploymentState.Ready };
            Commit(_state with { Sessions = [.. _state.Sessions.Where(s => s.Id != session.Id), observed] });
        }
    }
    public void PendingOptions(IEnumerable<BootSession> pending)
    {
        lock (_gate)
        {
            var values = pending.ToDictionary(s => s.Id);
            Commit(_state with { Sessions = _state.Sessions.Select(s => values.TryGetValue(s.Id, out var p) &&
                p.OptionsRevision > s.OptionsRevision && !_state.Jobs.Any(j => j.SessionId == s.Id)
                    ? s with { PendingUsername = p.PendingUsername, OptionsRevision = p.OptionsRevision } : s).ToList() });
        }
    }
    public void AddImage(DeploymentImage image)
    {
        image = DeploymentPolicy.Copy(image);
        DeploymentPolicy.Image(image);
        lock (_gate)
        {
            var existing = _state.Images.Find(i => i.Id == image.Id);
            DeploymentPolicy.Require(existing is null || existing == image ||
                System.Text.Json.JsonSerializer.Serialize(existing) == System.Text.Json.JsonSerializer.Serialize(image),
                "Una imagen inmutable no puede sustituirse.");
            if (existing is null) Commit(_state with { Images = [.. _state.Images, image] });
        }
    }
    public void RemoveImage(string id)
    {
        lock (_gate)
        {
            DeploymentPolicy.Require(!_state.Jobs.Any(j => j.Profile.ImageId == id), "La imagen está retenida por un trabajo.");
            Commit(_state with { Images = _state.Images.Where(i => i.Id != id).ToList() });
        }
    }
    public DeploymentBatch Confirm(DeploymentConfirmation request, DeploymentProfile profile, bool panelConnected,
        DateTimeOffset now, string? driverHash = null)
    {
        lock (_gate)
        {
            DeploymentPolicy.Require(panelConnected, "Conecta con el panel antes de confirmar un lote nuevo.");
            DeploymentPolicy.Require(DeploymentPolicy.IsId(request.Id) && request.Targets.Count is > 0 and <= 100 &&
                request.Targets.Select(t => t.SessionId).Distinct().Count() == request.Targets.Count, "Lote no válido.");
            if (_state.Confirmations.TryGetValue(request.Id, out var previous))
            {
                DeploymentPolicy.Require(System.Text.Json.JsonSerializer.Serialize(previous) == System.Text.Json.JsonSerializer.Serialize(request),
                    "La confirmación ya existe con otras opciones.");
                return DeploymentPolicy.Copy(_state.Batches.Single(b => b.Id == request.Id));
            }
            DeploymentPolicy.Require(profile.Id == request.ProfileId && profile.Revision == request.ProfileRevision,
                "El perfil ha cambiado. Vuelve a revisar el lote.");
            var image = _state.Images.Find(i => i.Id == profile.ImageId) ?? throw new InvalidDataException("Importa la imagen del perfil en esta estación.");
            DeploymentPolicy.Image(image);
            DeploymentPolicy.Require(image.Editions.Any(e => e.Index == profile.EditionIndex), "La edición no existe en la imagen.");
            var jobs = new List<DeploymentJob>();
            foreach (var target in request.Targets)
            {
                var session = _state.Sessions.Find(s => s.Id == target.SessionId) ?? throw new InvalidDataException("Sesión de arranque desconocida.");
                DeploymentPolicy.Require(session.State == DeploymentState.Ready && now - session.SeenAtUtc < TimeSpan.FromMinutes(2) &&
                    !_state.Jobs.Any(j => j.SessionId == session.Id), "El destino ya tiene un trabajo o debe comunicar de nuevo su inventario.");
                DeploymentPolicy.Require(!DeploymentPolicy.Ambiguous(session, _state.Sessions), "Identidad duplicada o ambigua. Revisa físicamente los equipos.");
                DeploymentPolicy.Require(session.Hardware.Uefi && session.Hardware.Tpm2 && session.Hardware.MemoryBytes >= 4L * 1024 * 1024 * 1024,
                    "El destino requiere UEFI, TPM 2.0 y al menos 4 GB de memoria. Setup comprobará el resto de requisitos.");
                DeploymentPolicy.Username(target.Username);
                DeploymentPolicy.Require(target.OptionsRevision == session.OptionsRevision &&
                    (session.PendingUsername is null || target.Username == session.PendingUsername), "El panel cambió las opciones. Vuelve a revisar el lote.");
                var disk = session.Hardware.Disks.SingleOrDefault(d => d.UniqueId == target.DiskId && DeploymentPolicy.Candidate(d))
                    ?? throw new InvalidDataException("Selecciona expresamente un disco interno identificable.");
                jobs.Add(new(Guid.NewGuid().ToString("N"), request.Id, session.Id, DeploymentPolicy.Fingerprint(session.Hardware),
                    disk, DeploymentPolicy.Copy(profile), target.Username, target.OptionsRevision, DeploymentState.Queued, now,
                    WindowsEditionId: image.Editions.Single(e => e.Index == profile.EditionIndex).EditionId, WindowsBuild: image.Build, DriverSha256: driverHash));
            }
            var batch = new DeploymentBatch(request.Id, now, jobs.Select(j => j.Id).ToList());
            Commit(_state with { Jobs = [.. _state.Jobs, .. jobs], Batches = [.. _state.Batches, batch],
                Confirmations = new(_state.Confirmations) { [request.Id] = DeploymentPolicy.Copy(request) },
                Sessions = _state.Sessions.Select(s => jobs.Any(j => j.SessionId == s.Id) ? s with { State = DeploymentState.Queued } : s).ToList() });
            return DeploymentPolicy.Copy(batch);
        }
    }
    public DeploymentJob? Claim(string sessionId, Func<DeploymentImage, bool> integrity, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (_state.Jobs.Count(j => DeploymentPolicy.Active(j.State)) >= _state.Capacity) return null;
            var job = _state.Jobs.FirstOrDefault(j => j.SessionId == sessionId && j.State == DeploymentState.Queued && j.PanelAccepted);
            if (job is null) return null;
            var session = _state.Sessions.Single(s => s.Id == sessionId);
            var image = _state.Images.Single(i => i.Id == job.Profile.ImageId);
            if (now - session.SeenAtUtc >= TimeSpan.FromMinutes(2) || DeploymentPolicy.Ambiguous(session, _state.Sessions) ||
                job.HardwareFingerprint != DeploymentPolicy.Fingerprint(session.Hardware) || !integrity(image) || job.DestructiveStarted)
            {
                Replace(job with { State = DeploymentState.Attention, Phase = "Cambió el destino o la imagen. Revisión obligatoria; no se ha autorizado otro borrado." });
                return null;
            }
            job = job with { State = DeploymentState.Installing, DestructiveStarted = true, Phase = "Autorizado para Windows Setup" };
            Replace(job); return DeploymentPolicy.Copy(job);
        }
    }
    public void Acknowledge(IEnumerable<string> ids)
    {
        lock (_gate)
        {
            var accepted = ids.ToHashSet();
            if (!_state.Jobs.Any(j => accepted.Contains(j.Id) && !j.PanelAccepted)) return;
            Commit(_state with { Jobs = _state.Jobs.Select(j => accepted.Contains(j.Id) ? j with { PanelAccepted = true } : j).ToList() });
        }
    }
    private void Replace(DeploymentJob job) => Commit(_state with { Jobs = _state.Jobs.Select(j => j.Id == job.Id ? job : j).ToList(),
        Sessions = _state.Sessions.Select(s => s.Id == job.SessionId ? s with { State = job.State } : s).ToList() });
    public void Progress(DeploymentProgress progress)
    {
        lock (_gate)
        {
            var job = _state.Jobs.SingleOrDefault(j => j.Id == progress.JobId) ?? throw new InvalidDataException("Trabajo desconocido.");
            if (progress.Sequence <= job.LastSequence) return;
            DeploymentPolicy.Require(DeploymentPolicy.Active(job.State) && progress.Sequence == job.LastSequence + 1 &&
                progress.State is DeploymentState.Installing or DeploymentState.PostInstall or DeploymentState.Completed or DeploymentState.Attention &&
                !(job.State == DeploymentState.PostInstall && progress.State == DeploymentState.Installing) &&
                progress.Percent is null or >= 0 and <= 100 && progress.Phase.Length is > 0 and <= 200,
                "Evento fuera de secuencia o estado no permitido.");
            DeploymentPolicy.Require(progress.State != DeploymentState.Completed ||
                (progress.WindowsVerified && progress.AccountVerified && progress.ComponentsVerified), "No se puede declarar éxito sin verificar todos los componentes.");
            var applications = progress.ApplicationResult ?? job.ApplicationResult;
            if (applications is not null) DeploymentPolicy.ApplicationResult(job.Profile, applications, progress.ComponentsVerified);
            DeploymentPolicy.Require(job.Profile.ApplicationDefinition is null || !progress.ComponentsVerified || applications?.Complete == true,
                "Falta el resultado verificado de las aplicaciones.");
            Replace(job with { State = progress.State, LastSequence = progress.Sequence, Phase = progress.Phase, Percent = progress.Percent,
                WindowsVerified = progress.WindowsVerified, AccountVerified = progress.AccountVerified,
                ComponentsVerified = progress.ComponentsVerified, RebootRequired = progress.RebootRequired,
                ApplicationResult = applications is null ? null : DeploymentPolicy.Copy(applications) });
        }
    }
    public void Cancel(string id)
    {
        lock (_gate)
        {
            var job = _state.Jobs.Single(j => j.Id == id);
            DeploymentPolicy.Require(job.State == DeploymentState.Queued && !job.DestructiveStarted,
                "Solo se puede cancelar un trabajo en cola. Revisa una instalación activa en el destino.");
            Replace(job with { State = DeploymentState.Cancelled, Phase = "Cancelado antes de instalar" });
        }
    }
    public void ResumeComponents(string id, bool physicallyReviewed = false)
    {
        lock (_gate)
        {
            var job = _state.Jobs.Single(j => j.Id == id);
            DeploymentPolicy.Require(job.State == DeploymentState.Attention && (physicallyReviewed || job.WindowsVerified && job.AccountVerified) && job.DestructiveStarted,
                "Verifica Windows y la cuenta antes de reanudar componentes. Esta acción nunca reinstala Windows.");
            Replace(job with { State = DeploymentState.PostInstall, Phase = "Reanudación de componentes autorizada en la estación" });
        }
    }
    public void CloseReviewedFailure(string id)
    {
        lock (_gate)
        {
            var job = _state.Jobs.Single(j => j.Id == id);
            DeploymentPolicy.Require(job.State == DeploymentState.Attention, "Solo se pueden cerrar incidencias revisadas.");
            Replace(job with { State = DeploymentState.Cancelled, Phase = "Incidencia cerrada tras revisión física; no se repetirá el borrado" });
        }
    }
}
