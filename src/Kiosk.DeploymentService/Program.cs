using System.Security.Cryptography;
using System.Security.Principal;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Kiosk.Deployment;
using Kiosk.DeploymentService;
using Microsoft.Extensions.Hosting.WindowsServices;

using (var identity = WindowsIdentity.GetCurrent())
    if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator) && !identity.IsSystem)
        throw new InvalidOperationException("El servicio de despliegue requiere una instalación administrativa.");
if (args is ["--can-maintain"])
{
    var snapshot = AtomicState.Read(Path.Combine(StationState.Root, "queue-v1.json"), () => new DeploymentQueueState(1, 3, [], [], [], [], []));
    Environment.ExitCode = snapshot.Jobs.Any(j => DeploymentPolicy.Active(j.State) || j.State == DeploymentState.Attention && j.DestructiveStarted) ? 2 : 0; return;
}
if (args is ["--prepare-maintenance"])
{
    try
    {
        using var pipe = new NamedPipeClientStream(".", DeploymentPipe.Name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(5000);
        using var reader = new StreamReader(pipe, Encoding.UTF8, false, 65536, true);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 65536, true) { AutoFlush = true };
        await writer.WriteLineAsync(JsonSerializer.Serialize(new LocalDeploymentRequest("maintenance", JsonSerializer.SerializeToElement(new { }))));
        var response = JsonSerializer.Deserialize<LocalDeploymentResponse>(await reader.ReadLineAsync() ?? "null", AtomicState.Json);
        Environment.ExitCode = response?.Success == true ? 0 : 2;
    }
    catch { Environment.ExitCode = 2; }
    return;
}
if (args.Length > 0 && args is not ["--initialize-user", _]) { Environment.ExitCode = 64; return; }
var state = new StationState();
if (args is ["--initialize-user", var account])
{
    var sid = (SecurityIdentifier)new NTAccount(account).Translate(typeof(SecurityIdentifier));
    state.Configure(state.Settings with { OperatorSid = sid.Value, Enabled = false }); return;
}
// Restart requires explicit activation and review; authorized jobs are never erased/replayed.
if (state.Settings.Enabled) state.Enable(false);
var builder = WebApplication.CreateBuilder(args);
builder.Host.UseWindowsService(options => options.ServiceName = "ClinicaPCDeployment");
builder.Logging.ClearProviders(); // No request paths, worker bodies, credentials or answer files enter a shared log.
builder.WebHost.ConfigureKestrel(server =>
{
    server.Limits.MaxRequestBodySize = 256 * 1024;
    server.ListenAnyIP(8089);
    server.ListenAnyIP(8449, endpoint => endpoint.UseHttps(state.Certificate));
});
builder.Services.AddSingleton(state);
builder.Services.AddSingleton<ImageLibrary>();
builder.Services.AddSingleton<NetworkBoot>();
builder.Services.AddSingleton<DriverLibrary>();
builder.Services.AddSingleton<PanelConnection>();
builder.Services.AddHostedService(p => p.GetRequiredService<PanelConnection>());
builder.Services.AddHostedService<LocalControl>();
builder.Services.AddHostedService<PowerGuard>();
var app = builder.Build();
app.Use(async (ctx, next) =>
{
    if (!state.Settings.Enabled || !state.InSubnet(ctx.Connection.RemoteIpAddress)) { ctx.Response.StatusCode = 403; return; }
    if (ctx.Request.Path.StartsWithSegments("/worker") && !ctx.Request.IsHttps) { ctx.Response.StatusCode = 403; return; }
    try { await next(); }
    catch (InvalidDataException ex) { ctx.Response.StatusCode = 409; await ctx.Response.WriteAsJsonAsync(new { error = ex.Message }); }
    catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException or ArgumentException)
    { ctx.Response.StatusCode = 409; await ctx.Response.WriteAsJsonAsync(new { error = "Revisa el trabajo y la conexión con la estación." }); }
});
app.MapGet("/boot/{name}", (string name, NetworkBoot boot) =>
{
    if (name == "station.json") return Results.File(Path.Combine(StationState.Root, "boot-station.json"), "application/json");
    if (!NetworkBoot.Files.Contains(name)) return Results.NotFound();
    string path = name == "autoexec.ipxe" ? Path.Combine(StationState.Root, name) : Path.Combine(boot.BootRoot, name);
    if (name == "boot.wim") path = boot.WindowsPePath();
    return Results.File(path, "application/octet-stream", enableRangeProcessing: true);
});
app.MapPost("/worker/enroll", (HttpContext ctx, DeploymentHardware hardware) =>
{
    DeploymentPolicy.Require(StationState.SecretEqual(state.Secrets.BootstrapToken, ctx.Request.Headers["X-Bootstrap-Token"]), "Arranque no autorizado.");
    var worker = state.NewWorker();
    var session = new BootSession(worker.Id, worker.Id[..6].ToUpperInvariant(), DateTimeOffset.UtcNow, hardware);
    state.Queue.Observe(session); return Results.Ok(new { worker.Id, worker.Token, session.DisplayId });
});
WorkerSession Worker(string id, HttpContext ctx) => state.Worker(id, ctx.Request.Headers["X-Worker-Token"])
    ?? throw new InvalidDataException("Sesión de instalación no autorizada.");
app.MapPost("/worker/{id}/inventory", (string id, HttpContext ctx, DeploymentHardware hardware) =>
{
    var worker = Worker(id, ctx);
    state.Queue.Observe(new(id, id[..6].ToUpperInvariant(), DateTimeOffset.UtcNow, hardware)); return Results.NoContent();
});
var claimGate = new SemaphoreSlim(1, 1);
app.MapPost("/worker/{id}/claim", async (string id, HttpContext ctx, ImageLibrary library) =>
{
    await claimGate.WaitAsync(ctx.RequestAborted);
    try
    {
        var worker = Worker(id, ctx); if (worker.CommandDelivered) return Results.NoContent();
        string post = Path.Combine(AppContext.BaseDirectory, "postinstall", "Kiosk.DeploymentPostInstall.exe");
        var hashes = AtomicState.Read<Dictionary<string, string>>(Path.Combine(AppContext.BaseDirectory, "postinstall", "hashes.json"), () => throw new InvalidDataException("Falta la preparación posterior verificada."));
        string Hash(string name)
        {
            string path = Path.Combine(AppContext.BaseDirectory, "postinstall", name);
            DeploymentPolicy.Require(hashes.TryGetValue(name, out var expected) && DeploymentPolicy.IsHash(expected), "Recurso posterior no autorizado.");
            using var file = File.OpenRead(path); string hash = Convert.ToHexString(SHA256.HashData(file));
            DeploymentPolicy.Require(hash.Equals(expected, StringComparison.OrdinalIgnoreCase), "Recurso posterior alterado."); return hash;
        }
        string postHash = Hash("Kiosk.DeploymentPostInstall.exe");
        var queued = state.Queue.Snapshot().Jobs.FirstOrDefault(j => j.SessionId == id && j.State == DeploymentState.Queued);
        if (queued is null) return Results.NoContent();
        string? workerHash = queued.Profile.Applications.Applications.Count > 0 ? Hash("worker.zip") : null;
        string? kioskHash = queued.Profile.Kiosk ? Hash("kiosk.exe") : null;
        string? kioskVersion = queued.Profile.Kiosk ? AtomicState.Read<string>(Path.Combine(AppContext.BaseDirectory, "postinstall", "kiosk-version.json"), () => throw new InvalidDataException("Falta la versión de Kiosk.")) : null;
        string? password = state.JobPassword(queued.Id);
        if (password is null) return Results.NoContent(); // Confirmation may still be persisting protected per-target options.
        var job = state.Queue.Claim(id, library.VerifiedForClaim, DateTimeOffset.UtcNow); if (job is null) return Results.NoContent();
        string answer = UnattendWriter.Create(job, password);
        state.Delivered(worker); // Persist before sending; a reconnect never returns an erase command again.
        return Results.Ok(new WorkerAuthorization(job, answer, $"\\\\{state.Settings.Address}\\ClinicaPCDeployment\\{job.Profile.ImageId}",
            Environment.MachineName + "\\ClinicaPCDeployment", state.Secrets.SharePassword, worker.Token, state.CertificateSha256,
            postHash, workerHash, kioskHash, kioskVersion, job.DriverSha256));
    }
    finally { claimGate.Release(); }
});
app.MapGet("/worker/{id}/resources/{name}", (string id, string name, HttpContext ctx) =>
{
    Worker(id, ctx);
    var job = state.Queue.Snapshot().Jobs.LastOrDefault(j => j.SessionId == id && j.DestructiveStarted);
    if (job is null || name is not ("Kiosk.DeploymentPostInstall.exe" or "worker.zip" or "kiosk.exe" or "drivers.zip") ||
        name == "worker.zip" && job.Profile.Applications.Applications.Count == 0 || name == "kiosk.exe" && !job.Profile.Kiosk) return Results.NotFound();
    if (name == "drivers.zip") return job.DriverSha256 is null ? Results.NotFound() : Results.File(Path.Combine(StationState.Root, "drivers", job.DriverSha256 + ".zip"), "application/octet-stream");
    return Results.File(Path.Combine(AppContext.BaseDirectory, "postinstall", name), "application/octet-stream", enableRangeProcessing: true);
});
app.MapPost("/worker/{id}/progress", (string id, HttpContext ctx, DeploymentProgress progress) =>
{
    Worker(id, ctx);
    DeploymentPolicy.Require(state.Queue.Snapshot().Jobs.Any(j => j.Id == progress.JobId && j.SessionId == id), "El trabajo pertenece a otra sesión.");
    state.Queue.Progress(progress);
    if (progress.State is DeploymentState.PostInstall or DeploymentState.Completed or DeploymentState.Attention) state.ClearJobPassword(progress.JobId);
    return Results.NoContent();
});
app.MapGet("/worker/{id}/status", (string id, HttpContext ctx) =>
{ Worker(id, ctx); return Results.Ok(state.Queue.Snapshot().Jobs.LastOrDefault(j => j.SessionId == id)); });
app.Run();
