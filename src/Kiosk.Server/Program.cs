using System.Security.Claims;
using Kiosk.Server.Components;
using Kiosk.Server.Hubs;
using Kiosk.Server.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;

var builder = WebApplication.CreateBuilder(args);

// Configuración (appsettings.json / variables de entorno Kiosk__ApiKey, etc.).
// Vacío o ausente en las rutas → directorios por defecto bajo el ContentRoot.
static string OrDefault(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;
string dataDir   = OrDefault(builder.Configuration["Kiosk:DataDir"],   Path.Combine(builder.Environment.ContentRootPath, "data"));
string assetsDir = OrDefault(builder.Configuration["Kiosk:AssetsDir"], Path.Combine(builder.Environment.ContentRootPath, "assets"));
string? apiKey   = builder.Configuration["Kiosk:ApiKey"];   // vacío = servidor abierto (solo pruebas)
int slideDurationMs = builder.Configuration.GetValue<int?>("Kiosk:SlideDurationMs") ?? 5200; // = default del cliente

Directory.CreateDirectory(assetsDir);
builder.Services.AddSingleton(new ServerConfigStore(dataDir));
builder.Services.AddSingleton(new AssetLibrary(assetsDir));

// Sincronización del bucle de atracción (Fase 2): reloj maestro + hub SignalR + latido periódico.
builder.Services.AddSingleton(new AttractClock(slideDurationMs));
builder.Services.AddSignalR();
builder.Services.AddHostedService<AttractBroadcaster>();

// Panel de administración (Fase 3): Blazor Server + login por cookie (un solo encargado, sin roles).
builder.Services.AddSingleton(new PanelAuthStore(dataDir));
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthorization();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.Cookie.Name = "kiosk_panel";
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.SlidingExpiration = true;
    });

var app = builder.Build();

app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// Guardia de API key: cubre todo /api/*. Fuera de /api (p.ej. /health, panel, hub) queda abierto.
app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.StartsWithSegments("/api") && !string.IsNullOrEmpty(apiKey))
    {
        if (ctx.Request.Headers["X-Api-Key"] != apiKey)
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await ctx.Response.WriteAsync("API key inválida o ausente.");
            return;
        }
    }
    await next();
});

// Sonda de vida (sin auth): el cliente puede comprobar conectividad barata.
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// Config de contenido que consumen todos los kioscos.
app.MapGet("/api/config", (ServerConfigStore store) =>
{
    string json = store.ReadJson();
    return Results.Content(json, "application/json");
});

app.MapGet("/api/config/version", (ServerConfigStore store) => Results.Ok(new { version = store.Version() }));

// Assets (imágenes de marcas/periféricos). Sirve ficheros de assetsDir con guardia anti-traversal.
var contentTypes = new FileExtensionContentTypeProvider();
app.MapGet("/api/assets/{**path}", (string path) =>
{
    string full = Path.GetFullPath(Path.Combine(assetsDir, path));
    // Evita que "../.." salga del directorio de assets.
    if (!full.StartsWith(Path.GetFullPath(assetsDir) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest("Ruta no permitida.");
    if (!File.Exists(full))
        return Results.NotFound();

    if (!contentTypes.TryGetContentType(full, out string? mime))
        mime = "application/octet-stream";
    return Results.File(full, mime);
});

// Login del panel: valida contraseña y emite la cookie de sesión. El formulario (SSR) incluye el token
// antiforgery, que el middleware valida aquí. LocalRedirect evita redirecciones abiertas.
app.MapPost("/login", async (HttpContext ctx, PanelAuthStore auth,
    [FromForm] string password, [FromForm] string? returnUrl) =>
{
    if (!auth.Verify(password))
    {
        string back = "/login?error=1";
        if (!string.IsNullOrEmpty(returnUrl)) back += "&returnUrl=" + Uri.EscapeDataString(returnUrl);
        return Results.Redirect(back);
    }

    var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "encargado") },
        CookieAuthenticationDefaults.AuthenticationScheme);
    await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
    return Results.LocalRedirect(string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl);
});

app.MapPost("/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.LocalRedirect("/login");
});

// Vista previa de imágenes DENTRO del panel: mismo contenido que /api/assets pero autenticado por cookie
// (el <img> del navegador no manda X-Api-Key). Solo para el encargado con sesión.
app.MapGet("/panel/assets/{category}/{file}", (string category, string file, AssetLibrary lib) =>
{
    if (!lib.TryGetFile(category, file, out string full)) return Results.NotFound();
    if (!contentTypes.TryGetContentType(full, out string? mime)) mime = "application/octet-stream";
    return Results.File(full, mime);
}).RequireAuthorization();

// Hub de sincronización del attract. Fuera de /api → sin guardia X-Api-Key (no lleva datos sensibles,
// solo el origen/duración del cronómetro). El cliente escucha el evento "SyncState".
app.MapHub<SyncHub>("/hub/sync");

// Panel Blazor Server. Las páginas [Authorize] exigen la cookie; el resto redirige a /login.
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();

// Expuesto para las pruebas de integración (WebApplicationFactory<Program>).
public partial class Program { }
