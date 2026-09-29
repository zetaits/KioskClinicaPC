using System.Security.Claims;
using Kiosk.Server.Components;
using Kiosk.Server.Hubs;
using Kiosk.Server.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using KioskClinicaPC.Core.Sync;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// En producción systemd recoge stdout/stderr en journalctl. Evita el proveedor EventLog de Windows, que
// requiere permisos adicionales y no debe poder romper una petición si el registro de eventos no es escribible.
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
if (builder.Environment.IsDevelopment()) builder.Logging.AddDebug();

// Configuración (appsettings.json / variables de entorno Kiosk__ApiKey, etc.).
// Vacío o ausente en las rutas → directorios por defecto bajo el ContentRoot.
static string OrDefault(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;
string dataDir   = OrDefault(builder.Configuration["Kiosk:DataDir"],   Path.Combine(builder.Environment.ContentRootPath, "data"));
string assetsDir = OrDefault(builder.Configuration["Kiosk:AssetsDir"], Path.Combine(builder.Environment.ContentRootPath, "assets"));
string installersDir = OrDefault(builder.Configuration["Kiosk:InstallersDir"], Path.Combine(builder.Environment.ContentRootPath, "installers"));
string setupDir = OrDefault(builder.Configuration["Kiosk:SetupDir"], Path.Combine(builder.Environment.ContentRootPath, "setups"));
string updatesDir = OrDefault(builder.Configuration["Kiosk:UpdatesDir"], Path.Combine(builder.Environment.ContentRootPath, "updates"));
string sourceSeedAssetsDir = Path.GetFullPath(Path.Combine(
    builder.Environment.ContentRootPath, "..", "Kiosk.Client", "Assets"));
string publishedSeedAssetsDir = Path.Combine(builder.Environment.ContentRootPath, "seed-assets");
string seedAssetsDir = Directory.Exists(sourceSeedAssetsDir) ? sourceSeedAssetsDir : publishedSeedAssetsDir;
long maxInstallerBytes = builder.Configuration.GetValue<long?>("Kiosk:MaxInstallerBytes") ?? 1024L * 1024 * 1024;
long maxInstallerRequestBytes = checked(maxInstallerBytes + 1024L * 1024); // margen para cabeceras multipart
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = maxInstallerRequestBytes);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = maxInstallerRequestBytes);
string? apiKey   = builder.Configuration["Kiosk:ApiKey"];   // vacío = servidor abierto (solo pruebas)
string? initialSetupKey = builder.Configuration["Kiosk:InitialSetupKey"];
string? panelInitialPassword = builder.Configuration["Kiosk:PanelInitialPassword"];
string? releasePublishKey = builder.Configuration["Kiosk:ReleasePublishKey"];
string updateTokenKey = OrDefault(builder.Configuration["Kiosk:UpdateTokenKey"], apiKey ?? "development-update-token-key");
var updateSigningKeys = builder.Configuration.GetSection("Kiosk:UpdateSigningKeys").GetChildren()
    .Where(x => !string.IsNullOrWhiteSpace(x.Value))
    .ToDictionary(x => x.Key, x => x.Value!, StringComparer.OrdinalIgnoreCase);
string? updateSigningKeysDirectory = builder.Configuration["Kiosk:UpdateSigningKeysDirectory"];
if (!string.IsNullOrWhiteSpace(updateSigningKeysDirectory) && Directory.Exists(updateSigningKeysDirectory))
    foreach (string file in Directory.GetFiles(updateSigningKeysDirectory, "*.pem"))
        updateSigningKeys[Path.GetFileNameWithoutExtension(file)] = File.ReadAllText(file);
int slideDurationMs = builder.Configuration.GetValue<int?>("Kiosk:SlideDurationMs") ?? 5200; // = default del cliente

if (!builder.Environment.IsDevelopment() && string.IsNullOrWhiteSpace(apiKey))
    throw new InvalidOperationException("Kiosk:ApiKey es obligatoria fuera de Development.");
if (!string.IsNullOrWhiteSpace(initialSetupKey) &&
    (initialSetupKey.Length != 64 || !initialSetupKey.All(Uri.IsHexDigit)))
    throw new InvalidOperationException("Kiosk:InitialSetupKey debe tener 64 caracteres hexadecimales.");

// Zona horaria de la tienda para evaluar la vigencia de los eventos (no la del VPS). Id de Windows
// (p.ej. "Romance Standard Time" para España); vacío = zona local del servidor.
string? tzId = builder.Configuration["Kiosk:TimeZone"];
TimeZoneInfo storeTz = TimeZoneInfo.Local;
string? tzWarning = null;
if (!string.IsNullOrWhiteSpace(tzId))
{
    // OJO: el id de zona depende del SO del servidor (Windows "Romance Standard Time" vs Linux/IANA
    // "Europe/Madrid"). Si no resuelve, caemos a la hora local del VPS y los eventos podrían activarse
    // con horas corridas → se avisa a voces en el arranque en vez de tragarlo en silencio.
    try { storeTz = TimeZoneInfo.FindSystemTimeZoneById(tzId); }
    catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
    {
        tzWarning = $"Zona horaria '{tzId}' no válida en este SO ({ex.GetType().Name}); usando la hora local " +
                    $"del servidor ({TimeZoneInfo.Local.Id}). Los eventos pueden activarse con horas corridas.";
    }
}

Directory.CreateDirectory(dataDir);
Directory.CreateDirectory(assetsDir);
string dataProtectionDir = Path.Combine(dataDir, "dataprotection-keys");
Directory.CreateDirectory(dataProtectionDir);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionDir))
    .SetApplicationName("Kiosk.Server");

// Solo se confía en los proxies loopback que ForwardedHeadersOptions trae por defecto. El despliegue
// recomendado ejecuta Caddy en el mismo host, por lo que RemoteIpAddress y Request.Scheme reflejan al cliente.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});

var configStore = new ServerConfigStore(dataDir);
var eventStore = new EventStore(dataDir);
builder.Services.AddSingleton(configStore);
builder.Services.AddSingleton(eventStore);
builder.Services.AddSingleton(new ContentResolver(configStore, eventStore, storeTz));
builder.Services.AddSingleton(new AssetLibrary(assetsDir, seedAssetsDir));
builder.Services.AddSingleton(new FleetRegistry(dataDir, storeTz));
builder.Services.AddSingleton(new InstallerCatalog(dataDir, installersDir, maxInstallerBytes));
builder.Services.AddSingleton(new InstallationJobStore(dataDir));
builder.Services.AddSingleton(new InitialSetupSessionStore(dataDir));
builder.Services.AddSingleton(new InitialSetupBundleStore(setupDir));
builder.Services.AddSingleton(new MaintenanceJobStore(dataDir));
builder.Services.AddSingleton(new KioskUpdateStore(dataDir, updatesDir, maxInstallerBytes, storeTz,
    updateTokenKey, updateSigningKeys));
builder.Services.AddSingleton(new PanelAccessLog(dataDir));

// Sincronización del bucle de atracción (Fase 2): reloj maestro + hub SignalR + latido periódico.
builder.Services.AddSingleton(new AttractClock(slideDurationMs));
builder.Services.AddSignalR();
builder.Services.AddHostedService<AttractBroadcaster>();
builder.Services.AddHostedService<EventTransitionBroadcaster>();

// Panel de administración (Fase 3): Blazor Server + login por cookie (un solo encargado, sin roles).
builder.Services.AddSingleton(new PanelAuthStore(dataDir, panelInitialPassword));
builder.Services.AddSingleton<LoginThrottle>();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options => options.AddPolicy("initial-setup", context =>
    RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0,
            AutoReplenishment = true
        })));
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.Cookie.Name = "kiosk_panel";
        options.Cookie.HttpOnly = true;
        options.Cookie.IsEssential = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.SlidingExpiration = true;
    });

var app = builder.Build();

// Avisos de arranque: configuración insegura o dudosa debe verse en el log, no descubrirse en producción.
if (tzWarning != null) app.Logger.LogWarning("{TzWarning}", tzWarning);
app.Logger.LogInformation("Zona horaria de la tienda: {TimeZone}.", storeTz.Id);
if (string.IsNullOrEmpty(apiKey))
    app.Logger.LogWarning("Kiosk:ApiKey vacía: /api/* se sirve SIN autenticación. Fija una clave antes de exponer el servidor a internet.");
if (string.IsNullOrEmpty(initialSetupKey))
    app.Logger.LogWarning("Kiosk:InitialSetupKey vacía: el pack del instalador inicial queda deshabilitado.");
if (string.IsNullOrWhiteSpace(releasePublishKey) || updateSigningKeys.Count == 0)
    app.Logger.LogWarning("Las releases administradas están deshabilitadas: configura ReleasePublishKey y UpdateSigningKeys.");

app.UseForwardedHeaders();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseAntiforgery();

static bool SecretEquals(string? expected, string? actual)
{
    if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(actual)) return false;
    byte[] left = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
    byte[] right = SHA256.HashData(Encoding.UTF8.GetBytes(actual));
    return CryptographicOperations.FixedTimeEquals(left, right);
}

// El bootstrap inicial usa una clave limitada propia; el resto de /api conserva la clave de los kioscos.
app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.StartsWithSegments("/api/releases"))
    {
        // La importación de CI usa una credencial independiente y no expone la API key de la flota.
    }
    else if (ctx.Request.Path.StartsWithSegments("/api/setup"))
    {
        if (!SecretEquals(initialSetupKey, ctx.Request.Headers["X-Setup-Key"].FirstOrDefault()))
        {
            ctx.Response.StatusCode = string.IsNullOrEmpty(initialSetupKey)
                ? StatusCodes.Status503ServiceUnavailable
                : StatusCodes.Status401Unauthorized;
            await ctx.Response.WriteAsync("Instalación inicial no autorizada.");
            return;
        }
    }
    else if (ctx.Request.Path.StartsWithSegments("/api") && !string.IsNullOrEmpty(apiKey))
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

// Ficha PDF pública que abren los QR. En desarrollo se sirve directamente desde docs/; el publish
// copia exactamente esos recursos a wwwroot/ficha, por lo que producción no depende del repositorio.
// El fragmento #... nunca forma parte de la petición HTTP: las specs se procesan solo en el teléfono.
string sourceQrDir = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "..", "docs"));
string publishedQrDir = Path.Combine(
    builder.Environment.WebRootPath ?? Path.Combine(builder.Environment.ContentRootPath, "wwwroot"), "ficha");
string qrDir = Directory.Exists(sourceQrDir) ? sourceQrDir : publishedQrDir;
string sourceFontDir = Path.GetFullPath(Path.Combine(
    builder.Environment.ContentRootPath, "..", "Kiosk.Client", "Fonts"));
var qrContentTypes = new FileExtensionContentTypeProvider();

IResult ServeQrFile(string? path, HttpContext ctx)
{
    string relative = string.IsNullOrWhiteSpace(path)
        ? "index.html"
        : path.Replace('/', Path.DirectorySeparatorChar);
    string baseDir = relative.StartsWith("fonts" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
        && Directory.Exists(sourceFontDir)
            ? sourceFontDir
            : qrDir;
    if (baseDir == sourceFontDir)
        relative = relative[("fonts" + Path.DirectorySeparatorChar).Length..];

    string root = Path.GetFullPath(baseDir);
    string full = Path.GetFullPath(Path.Combine(root, relative));
    if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
        || !File.Exists(full))
        return Results.NotFound();

    ctx.Response.Headers.CacheControl = Path.GetFileName(full).Equals("index.html", StringComparison.OrdinalIgnoreCase)
        ? "public, no-cache"
        : "public, max-age=86400";
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["X-Frame-Options"] = "DENY";
    ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
    ctx.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
    ctx.Response.Headers["Content-Security-Policy"] =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data: blob:; font-src 'self'; connect-src 'none'; object-src 'none'; " +
        "frame-ancestors 'none'; base-uri 'none'; form-action 'none'";

    if (!qrContentTypes.TryGetContentType(full, out string? mime)) mime = "application/octet-stream";
    return Results.File(full, mime, enableRangeProcessing: true);
}

app.MapGet("/ficha", (HttpContext ctx) =>
    ctx.Request.Path.Value?.EndsWith('/') == true
        ? ServeQrFile(null, ctx)
        : Results.Redirect("/ficha/")).AllowAnonymous();
app.MapGet("/ficha/{**path}", (string? path, HttpContext ctx) => ServeQrFile(path, ctx)).AllowAnonymous();

// Config de contenido que consumen todos los kioscos: contenido efectivo (base + evento vigente).
app.MapGet("/api/config", (ContentResolver content) =>
    Results.Content(content.EffectiveJson(), "application/json"));

// La misma versión engloba contenido y biblioteca visual. Los clientes antiguos siguen viendo un string;
// los nuevos aprovechan el cambio para sincronizar también imágenes.
app.MapGet("/api/config/version", (ContentResolver content, AssetLibrary assets) =>
    Results.Ok(new { version = content.Version() + "-" + assets.Version() }));

app.MapGet("/api/assets/manifest", (AssetLibrary assets) => Results.Ok(assets.Manifest()));

static string? UpdateToken(HttpContext ctx) => ctx.Request.Headers["X-Update-Token"].FirstOrDefault();

app.MapPost("/api/releases", async (HttpContext ctx, KioskUpdateStore updates) =>
{
    if (!SecretEquals(releasePublishKey, ctx.Request.Headers["X-Release-Publish-Key"].FirstOrDefault()))
        return string.IsNullOrWhiteSpace(releasePublishKey) ? Results.StatusCode(503) : Results.Unauthorized();
    try
    {
        var form = await ctx.Request.ReadFormAsync(ctx.RequestAborted);
        var manifestFile = form.Files.GetFile("manifest") ?? throw new InvalidDataException("Falta el manifiesto.");
        var setup = form.Files.GetFile("setup") ?? throw new InvalidDataException("Falta el instalador.");
        string signature = form["signature"].ToString();
        if (manifestFile.Length is <= 0 or > 128 * 1024) throw new InvalidDataException("Tamaño de manifiesto no válido.");
        byte[] manifestBytes;
        await using (var input = manifestFile.OpenReadStream())
        using (var memory = new MemoryStream()) { await input.CopyToAsync(memory, ctx.RequestAborted); manifestBytes = memory.ToArray(); }
        await using var setupStream = setup.OpenReadStream();
        var release = await updates.ImportAsync(manifestBytes, signature, setup.FileName, setupStream, ctx.RequestAborted);
        return Results.Ok(new { release.Version, release.Sha256, release.State });
    }
    catch (Exception ex) when (ex is InvalidDataException or IOException or CryptographicException or System.Text.Json.JsonException)
    { return Results.BadRequest(new { error = ex.Message }); }
});

app.MapGet("/api/updates/assignment", (string deviceId, string currentVersion, string? deviceName,
    KioskUpdateStore updates) =>
{
    var assignment = updates.GetAssignment(deviceId, deviceName ?? deviceId, currentVersion);
    return assignment == null ? Results.NoContent() : Results.Ok(assignment);
});

app.MapGet("/api/updates/{jobId}/manifest", (string jobId, HttpContext ctx, KioskUpdateStore updates) =>
{
    var authorized = updates.Authorize(jobId, UpdateToken(ctx));
    if (authorized == null) return Results.NotFound();
    ctx.Response.Headers["X-Update-Signature"] = authorized.Value.Release.SignatureBase64;
    ctx.Response.Headers["X-Update-Key-Id"] = authorized.Value.Release.KeyId;
    return Results.File(updates.ManifestBytes(authorized.Value.Release), "application/json");
});

app.MapGet("/api/updates/{jobId}/download", (string jobId, HttpContext ctx, KioskUpdateStore updates) =>
{
    var authorized = updates.Authorize(jobId, UpdateToken(ctx));
    return authorized == null ? Results.NotFound() : Results.File(updates.ResolveFile(authorized.Value.Release),
        "application/vnd.microsoft.portable-executable", enableRangeProcessing: true);
});

app.MapGet("/api/updates/{jobId}/authorize", (string jobId, HttpContext ctx, KioskUpdateStore updates) =>
{
    var assignment = updates.AuthorizedAssignment(jobId, UpdateToken(ctx));
    return assignment == null ? Results.NotFound() : Results.Ok(assignment);
});

app.MapPost("/api/updates/{jobId}/status", async (string jobId, HttpContext ctx, KioskUpdateStore updates) =>
{
    var update = await ctx.Request.ReadFromJsonAsync<KioskUpdateStatusUpdate>(ctx.RequestAborted);
    return update != null && updates.Update(jobId, UpdateToken(ctx), update) ? Results.NoContent() : Results.BadRequest();
});

static string? InstallToken(HttpContext ctx) => ctx.Request.Headers["X-Install-Token"].FirstOrDefault();
static string? MaintenanceToken(HttpContext ctx) => ctx.Request.Headers["X-Maintenance-Token"].FirstOrDefault();

// Un trabajo solo puede leer SU manifiesto/binario con API key + token aleatorio ligado a equipo y paquete.
app.MapGet("/api/installations/{jobId}/manifest", (string jobId, HttpContext ctx,
    InstallationJobStore jobs, InstallerCatalog catalog) =>
{
    string? token = InstallToken(ctx);
    var job = token == null ? null : jobs.Authorize(jobId, token);
    var package = job == null ? null : catalog.Find(job.PackageId);
    if (job == null || package == null || package.Sha256 != job.PackageSha256) return Results.NotFound();
    return Results.Ok(new InstallationManifest
    {
        JobId = job.Id, DeviceId = job.DeviceId, PackageId = package.Id, DisplayName = package.DisplayName,
        FileName = package.OriginalFileName, Kind = package.Kind, SizeBytes = package.SizeBytes,
        Sha256 = package.Sha256, AllowUnsigned = package.AllowUnsigned
    });
});

app.MapGet("/api/installations/{jobId}/download", (string jobId, HttpContext ctx,
    InstallationJobStore jobs, InstallerCatalog catalog) =>
{
    string? token = InstallToken(ctx);
    var job = token == null ? null : jobs.Authorize(jobId, token);
    var package = job == null ? null : catalog.Find(job.PackageId);
    if (job == null || package == null || package.Sha256 != job.PackageSha256) return Results.NotFound();
    return Results.File(catalog.ResolveFile(package), "application/octet-stream", enableRangeProcessing: true);
});

app.MapPost("/api/installations/{jobId}/status", async (string jobId, HttpContext ctx,
    InstallationJobStore jobs) =>
{
    string? token = InstallToken(ctx);
    if (token == null) return Results.Unauthorized();
    var update = await ctx.Request.ReadFromJsonAsync<InstallationStatusUpdate>();
    if (update == null) return Results.BadRequest();
    return jobs.Update(jobId, token, update) ? Results.NoContent() : Results.BadRequest();
});

static string? SetupToken(HttpContext ctx) => ctx.Request.Headers["X-Setup-Token"].FirstOrDefault();

app.MapGet("/api/setup/catalog", (InstallerCatalog catalog) =>
    Results.Ok(new InitialSetupCatalog
    {
        Packages = catalog.InitialSetupList().Select(p => new InitialSetupPackage
        {
            Id = p.Id, DisplayName = p.DisplayName, Kind = p.Kind, SizeBytes = p.SizeBytes,
            Sha256 = p.Sha256, SelectedByDefault = p.SelectedByDefault, Order = p.InitialSetupOrder
        }).ToList()
    })).RequireRateLimiting("initial-setup");

app.MapPost("/api/setup/sessions", async (HttpContext ctx, InstallerCatalog catalog,
    InitialSetupSessionStore sessions) =>
{
    var request = await ctx.Request.ReadFromJsonAsync<InitialSetupSessionRequest>();
    if (request == null || request.PackageIds.Count == 0) return Results.BadRequest("Selecciona al menos una aplicación.");
    var ids = request.PackageIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    var available = catalog.InitialSetupList().ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);
    if (ids.Count != request.PackageIds.Count || ids.Any(id => !available.ContainsKey(id)))
        return Results.Conflict("La selección ha cambiado; vuelve a cargar el catálogo.");
    var selected = ids.Select(id => available[id]).ToList();
    var created = sessions.Create(request.MachineName, request.SetupVersion, selected);
    return Results.Ok(new InitialSetupSessionResponse
    {
        SessionId = created.Session.Id,
        Token = created.Token,
        Packages = selected.Select(p => new InstallationManifest
        {
            JobId = created.Session.Id, DeviceId = created.Session.MachineName, PackageId = p.Id,
            DisplayName = p.DisplayName, FileName = p.OriginalFileName, Kind = p.Kind,
            SizeBytes = p.SizeBytes, Sha256 = p.Sha256, AllowUnsigned = p.AllowUnsigned
        }).ToList()
    });
}).RequireRateLimiting("initial-setup");

app.MapGet("/api/setup/sessions/{sessionId}/packages/{packageId}/download",
    (string sessionId, string packageId, HttpContext ctx, InitialSetupSessionStore sessions, InstallerCatalog catalog) =>
{
    string? token = SetupToken(ctx);
    var sessionPackage = token == null ? null : sessions.AuthorizePackage(sessionId, packageId, token);
    var package = sessionPackage == null ? null : catalog.Find(packageId);
    if (sessionPackage == null || package == null || package.Sha256 != sessionPackage.PackageSha256) return Results.NotFound();
    return Results.File(catalog.ResolveFile(package), "application/octet-stream", enableRangeProcessing: true);
});

app.MapPost("/api/setup/sessions/{sessionId}/packages/{packageId}/status",
    async (string sessionId, string packageId, HttpContext ctx, InitialSetupSessionStore sessions) =>
{
    string? token = SetupToken(ctx);
    if (token == null) return Results.Unauthorized();
    var update = await ctx.Request.ReadFromJsonAsync<InitialSetupStatusUpdate>();
    if (update == null) return Results.BadRequest();
    return sessions.Update(sessionId, packageId, token, update) ? Results.NoContent() : Results.BadRequest();
});

// El runner independiente sigue pudiendo confirmar el resultado cuando Kiosk y su servicio ya no existen.
app.MapPost("/api/maintenance/{jobId}/status", async (string jobId, HttpContext ctx,
    MaintenanceJobStore jobs, FleetRegistry fleet) =>
{
    string? token = MaintenanceToken(ctx);
    if (token == null) return Results.Unauthorized();
    var update = await ctx.Request.ReadFromJsonAsync<MaintenanceStatusUpdate>();
    if (update == null) return Results.BadRequest();
    if (!jobs.Update(jobId, token, update)) return Results.BadRequest();
    if (update.State is MaintenanceJobState.Succeeded or MaintenanceJobState.RebootRequired)
        fleet.MarkUninstalled(update.DeviceId);
    return Results.NoContent();
});

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
app.MapPost("/login", async (HttpContext ctx, PanelAuthStore auth, LoginThrottle throttle, PanelAccessLog accessLog,
    [FromForm] string password, [FromForm] string? returnUrl) =>
{
    string ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "desconocida";

    static string Back(string error, string? returnUrl)
    {
        string back = "/login?error=" + error;
        if (!string.IsNullOrEmpty(returnUrl)) back += "&returnUrl=" + Uri.EscapeDataString(returnUrl);
        return back;
    }

    // Demasiados fallos seguidos desde esta IP: rechaza sin ni siquiera comprobar la contraseña.
    if (throttle.IsLocked(ip))
    {
        accessLog.Record(ip, AccessOutcome.Locked);
        return Results.Redirect(Back("locked", returnUrl));
    }

    if (!auth.Verify(password))
    {
        throttle.RecordFailure(ip);
        accessLog.Record(ip, AccessOutcome.Fail);
        return Results.Redirect(Back("1", returnUrl));
    }

    throttle.RecordSuccess(ip);
    accessLog.Record(ip, AccessOutcome.Success);
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

app.MapGet("/panel/setup/download", (InitialSetupBundleStore bundles) =>
{
    var bundle = bundles.Latest(out _);
    return bundle == null
        ? Results.NotFound()
        : Results.File(bundle.FullPath, "application/vnd.microsoft.portable-executable",
            bundle.Manifest.FileName, enableRangeProcessing: true);
}).RequireAuthorization();

// Subida desde formulario SSR para no transportar binarios grandes por el circuito de Blazor.
app.MapPost("/panel/installers/upload", async (HttpContext ctx, InstallerCatalog catalog, IAntiforgery antiforgery) =>
{
    bool wantsJson = ctx.Request.Headers.Accept.Any(x =>
        x?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true);
    try
    {
        await antiforgery.ValidateRequestAsync(ctx);
        var form = await ctx.Request.ReadFormAsync();
        var file = form.Files.GetFile("installer") ?? throw new ArgumentException("Selecciona un archivo.");
        if (file.Length > catalog.MaxBytes) throw new ArgumentException($"El archivo supera el límite de {catalog.MaxBytes / 1024 / 1024} MB.");
        string name = form["displayName"].ToString();
        bool allowUnsigned = form["allowUnsigned"] == "on";
        await using var stream = file.OpenReadStream();
        await catalog.AddAsync(name, file.FileName, stream, allowUnsigned, ctx.RequestAborted);
        const string message = "Aplicación añadida al catálogo.";
        return wantsJson
            ? Results.Ok(new { ok = true, message })
            : Results.LocalRedirect("/aplicaciones?ok=" + Uri.EscapeDataString(message));
    }
    catch (Exception ex) when (ex is ArgumentException or IOException or InvalidDataException)
    {
        return wantsJson
            ? Results.Json(new { ok = false, message = ex.Message }, statusCode: StatusCodes.Status400BadRequest)
            : Results.LocalRedirect("/aplicaciones?error=" + Uri.EscapeDataString(ex.Message));
    }
}).RequireAuthorization();

// Hub de sincronización del attract. Fuera de /api → sin guardia X-Api-Key (no lleva datos sensibles,
// solo el origen/duración del cronómetro). El cliente escucha el evento "SyncState".
app.MapHub<SyncHub>("/hub/sync");

// Hub de control de la flota: registro + heartbeat de los kioscos y órdenes dirigidas del panel. Exige
// X-Api-Key en el handshake (validado dentro del hub) porque mueve control sensible (reiniciar/apagar).
app.MapHub<FleetHub>("/hub/fleet");

// Panel Blazor Server. Las páginas [Authorize] exigen la cookie; el resto redirige a /login.
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();

// Expuesto para las pruebas de integración (WebApplicationFactory<Program>).
public partial class Program { }
