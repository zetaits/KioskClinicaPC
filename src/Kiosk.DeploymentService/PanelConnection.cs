using System.Net.Http.Json;
using Kiosk.Deployment;
using Microsoft.AspNetCore.SignalR.Client;

namespace Kiosk.DeploymentService;

public sealed class PanelConnection(StationState state) : BackgroundService
{
    private readonly SemaphoreSlim _sync = new(1, 1);
    private readonly HttpClient _http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };
    public DeploymentConfiguration? Configuration { get; private set; }
    public bool Connected { get; private set; }
    private HubConnection? _hub;
    public static Uri Server(string value)
    {
        DeploymentPolicy.Require(Uri.TryCreate(value.TrimEnd('/') + "/", UriKind.Absolute, out var uri) && uri.Scheme == "https" &&
            uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0, "Introduce una dirección HTTPS válida del panel.");
        return uri!;
    }
    public async Task Enroll(string server, string code, string name, CancellationToken ct)
    {
        using var response = await _http.PostAsJsonAsync(new Uri(Server(server), "api/deployment/v1/enrollment"), new EnrollmentRequest(1, code, name), ct);
        DeploymentPolicy.Require(response.IsSuccessStatusCode, "No se pudo vincular. Comprueba el panel y genera un código nuevo.");
        var result = await response.Content.ReadFromJsonAsync<EnrollmentResponse>(ct) ?? throw new InvalidDataException("Alta incompatible.");
        DeploymentPolicy.Require(result.ProtocolVersion == 1 && DeploymentPolicy.IsId(result.StationId) && DeploymentPolicy.IsHash(result.Credential), "Alta incompatible.");
        state.SaveSecrets(state.Secrets with { Credential = result.Credential });
        state.Configure(state.Settings with { ServerUrl = server, StationId = result.StationId, Enabled = false });
        await Synchronize(ct);
    }
    private HttpRequestMessage Request(HttpMethod method, string route)
    {
        var request = new HttpRequestMessage(method, new Uri(Server(state.Settings.ServerUrl), route));
        request.Headers.Add("X-Deployment-Credential", state.Secrets.Credential); return request;
    }
    public async Task Synchronize(CancellationToken ct)
    {
        await _sync.WaitAsync(ct);
        try
        {
            Connected = false;
            DeploymentPolicy.Require(state.Settings.StationId is not null, "Vincula la estación al panel.");
            // Fetch pending options before sending inventory or confirming; server revisions always win.
            using var get = Request(HttpMethod.Get, "api/deployment/v1/configuration");
            using var response = await _http.SendAsync(get, ct);
            DeploymentPolicy.Require(response.IsSuccessStatusCode, "El panel no está disponible o el vínculo fue revocado. Los trabajos autorizados continúan.");
            var config = await response.Content.ReadFromJsonAsync<DeploymentConfiguration>(ct) ?? throw new InvalidDataException("Configuración incompatible.");
            DeploymentPolicy.Require(config.ProtocolVersion == 1, "Actualiza la estación: protocolo incompatible.");
            state.Queue.PendingOptions(config.PendingOptions); Configuration = config;
            var q = state.Queue.Snapshot();
            using var post = Request(HttpMethod.Post, "api/deployment/v1/inventory");
            post.Content = JsonContent.Create(new DeploymentInventory(1, q.Capacity, q.Images, q.Sessions, q.Batches, q.Jobs));
            using var sent = await _http.SendAsync(post, ct);
            DeploymentPolicy.Require(sent.IsSuccessStatusCode, "El panel rechazó el seguimiento. Revisa el vínculo de la estación.");
            state.Queue.Acknowledge(q.Jobs.Where(j => j.State == DeploymentState.Queued).Select(j => j.Id));
            Connected = true;
        }
        finally { _sync.Release(); }
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (state.Settings.StationId is not null)
                {
                    await Synchronize(stoppingToken);
                    if (_hub is null)
                    {
                        _hub = new HubConnectionBuilder().WithUrl(new Uri(Server(state.Settings.ServerUrl), "hub/deployment"), options =>
                            options.Headers["X-Deployment-Credential"] = state.Secrets.Credential).WithAutomaticReconnect().Build();
                        _hub.On("Changed", async () => { try { await Synchronize(stoppingToken); } catch { Connected = false; } });
                        await _hub.StartAsync(stoppingToken);
                    }
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or OperationCanceledException or InvalidOperationException)
            { Connected = false; }
            try { await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken); } catch (OperationCanceledException) { }
        }
        if (_hub is not null) await _hub.DisposeAsync();
    }
}
