using Kiosk.Server.Services;
using Microsoft.AspNetCore.StaticFiles;

var builder = WebApplication.CreateBuilder(args);

// Configuración (appsettings.json / variables de entorno Kiosk__ApiKey, etc.).
// Vacío o ausente en las rutas → directorios por defecto bajo el ContentRoot.
static string OrDefault(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;
string dataDir   = OrDefault(builder.Configuration["Kiosk:DataDir"],   Path.Combine(builder.Environment.ContentRootPath, "data"));
string assetsDir = OrDefault(builder.Configuration["Kiosk:AssetsDir"], Path.Combine(builder.Environment.ContentRootPath, "assets"));
string? apiKey   = builder.Configuration["Kiosk:ApiKey"];   // vacío = servidor abierto (solo pruebas)

Directory.CreateDirectory(assetsDir);
builder.Services.AddSingleton(new ServerConfigStore(dataDir));

var app = builder.Build();

// Guardia de API key: cubre todo /api/*. Fuera de /api (p.ej. /health) queda abierto.
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

// Config de contenido que consumen todos los kioscos. X-Config-Version deja al cliente
// (Fase 2/3) detectar cambios sin re-parsear el cuerpo completo.
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

app.Run();

// Expuesto para las pruebas de integración (WebApplicationFactory<Program>).
public partial class Program { }
