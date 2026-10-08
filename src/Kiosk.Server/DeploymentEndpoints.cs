using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kiosk.Deployment;
using Kiosk.Server.Hubs;
using Kiosk.Server.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.SignalR;

namespace Kiosk.Server;

public static class DeploymentEndpoints
{
    public static void MapDeployment(this WebApplication app, string? publishKey)
    {
        static string Station(HttpContext ctx) => (string)ctx.Items["DeploymentStation"]!;
        static async Task<IResult> Guard(Func<Task<IResult>> action)
        {
            try { return await action(); }
            catch (AntiforgeryValidationException) { return Results.BadRequest(new { error = "Sesión de formulario caducada. Recarga la página." }); }
            catch (InvalidDataException ex) { return Results.BadRequest(new { error = ex.Message }); }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException)
            { return Results.BadRequest(new { error = "Solicitud no válida. Recarga y revisa los datos." }); }
        }
        static Task Notify(IHubContext<DeploymentHub> hub) => hub.Clients.Groups("deployment-panel", "deployment-stations").SendAsync("Changed");
        app.MapPost("/api/deployment/v1/enrollment", (EnrollmentRequest request, DeploymentStore store) => Guard(() =>
            Task.FromResult<IResult>(Results.Ok(store.Enroll(request))))).RequireRateLimiting("deployment-enrollment");
        app.MapGet("/api/deployment/v1/configuration", (HttpContext ctx, DeploymentStore store, PackCatalogStore catalog) =>
            Results.Ok(store.Configuration(Station(ctx), catalog.Snapshot(), catalog.Definition(),
                ctx.Request.Headers["X-Deployment-Component-Policy"] == "2" ? 2 : 1)));
        app.MapPost("/api/deployment/v1/inventory", (HttpContext ctx, DeploymentInventory inventory, DeploymentStore store,
            IHubContext<DeploymentHub> hub) => Guard(async () =>
            { store.Sync(Station(ctx), inventory); await hub.Clients.Group("deployment-panel").SendAsync("Changed"); return Results.NoContent(); }));
        app.MapGet("/panel/deployment/state", (DeploymentStore store) => Results.Ok(store.Snapshot())).RequireAuthorization();
        app.MapPost("/panel/deployment/enrollment", (HttpContext ctx, IAntiforgery antiforgery, DeploymentStore store) => Guard(async () =>
            { await antiforgery.ValidateRequestAsync(ctx); return Results.Ok(store.CreateCode()); })).RequireAuthorization();
        app.MapPost("/panel/deployment/stations/{id}/revoke", (string id, HttpContext ctx, IAntiforgery antiforgery,
            DeploymentStore store, IHubContext<DeploymentHub> hub) => Guard(async () =>
            { await antiforgery.ValidateRequestAsync(ctx); store.Revoke(id); await Notify(hub); return Results.NoContent(); })).RequireAuthorization();
        app.MapPost("/panel/deployment/profiles", (ProfileRequest request, HttpContext ctx, IAntiforgery antiforgery,
            DeploymentStore store, PackCatalogStore catalog, IHubContext<DeploymentHub> hub) => Guard(async () =>
            { await antiforgery.ValidateRequestAsync(ctx); var result = store.SaveProfile(request, catalog.Snapshot(), catalog.Definition()); await Notify(hub); return Results.Ok(result); })).RequireAuthorization();
        app.MapPost("/panel/deployment/stations/{stationId}/sessions/{sessionId}/username", (string stationId, string sessionId,
            PendingUsernameRequest request, HttpContext ctx, IAntiforgery antiforgery, DeploymentStore store, IHubContext<DeploymentHub> hub) => Guard(async () =>
            { await antiforgery.ValidateRequestAsync(ctx); store.PendingUsername(stationId, sessionId, request); await Notify(hub); return Results.NoContent(); })).RequireAuthorization();
        app.MapGet("/panel/deployment/download", (HttpContext ctx, DeploymentReleaseStore releases) =>
        {
            ctx.Response.Headers.CacheControl = "no-store";
            var release = releases.Latest();
            if (release is null) return Results.NotFound(new { error = "No hay una versión de instalación por red publicada y validada." });
            if (Guid.TryParseExact(ctx.Request.Query["downloadId"], "N", out var id))
                ctx.Response.Cookies.Append("kioskDeploymentDownload", id.ToString("N"), new CookieOptions
                { Path = "/instalador", MaxAge = TimeSpan.FromMinutes(2), SameSite = SameSiteMode.Strict, Secure = ctx.Request.IsHttps });
            return Results.File(release.Value.Path, "application/octet-stream", release.Value.Release.FileName, enableRangeProcessing: true);
        }).RequireAuthorization();
        app.MapPost("/api/releases/deployment", (HttpContext ctx, DeploymentReleaseStore releases) => Guard(async () =>
        {
            string? key = ctx.Request.Headers["X-Release-Publish-Key"].FirstOrDefault();
            if (string.IsNullOrEmpty(publishKey)) return Results.StatusCode(503);
            if (string.IsNullOrEmpty(key) || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(key)),
                SHA256.HashData(Encoding.UTF8.GetBytes(publishKey)))) return Results.Unauthorized();
            var form = await ctx.Request.ReadFormAsync(ctx.RequestAborted);
            var manifest = form.Files.GetFile("manifest"); var file = form.Files.GetFile("setup");
            DeploymentPolicy.Require(manifest is not null && manifest.Length is > 0 and <= 128 * 1024 && file is not null,
                "Faltan el manifiesto y el paquete de despliegue.");
            using var input = manifest!.OpenReadStream();
            var release = await JsonSerializer.DeserializeAsync<DeploymentRelease>(input, AtomicState.Json, ctx.RequestAborted)
                ?? throw new InvalidDataException("Manifiesto vacío.");
            DeploymentPolicy.Require(file!.FileName == release.FileName, "Nombre de paquete incorrecto.");
            using var package = file.OpenReadStream(); await releases.Import(release, package, ctx.RequestAborted);
            return Results.Ok(new { release.Version });
        })).DisableAntiforgery();
        app.MapHub<DeploymentHub>("/hub/deployment");
    }
}
