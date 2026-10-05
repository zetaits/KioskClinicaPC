using System.IO;
using Microsoft.Management.Deployment;
using KioskClinicaPC.Core.Sync;

namespace Kiosk.SetupHelper;

internal sealed class WinGetEngine : IPackBackend
{
    private readonly PackageManager _manager;
    private readonly Action<string>? _phase, _info;
    public WinGetEngine(Action<string>? phase = null, Action<string>? info = null)
    {
        (_phase, _info) = (phase, info);
        _phase?.Invoke("Activating WinGet PackageManager COM API");
        _manager = WinGetActivation.Create<PackageManager>();
        _info?.Invoke("WinGet COM activation completed.");
    }
    public void RequireSupportedRuntime()
    {
        // The version property is available in supported recent clients; older COM interfaces trigger bootstrap.
        if (!Version.TryParse(_manager.Version.TrimStart('v'), out var version) || version < new Version(1, 29, 380))
            throw new System.Runtime.InteropServices.COMException("Se necesita WinGet 1.29.380 o superior.", unchecked((int)0x80004002));
    }
    private async Task<PackageCatalog> Connect(bool installed)
    {
        _phase?.Invoke("Locating and validating official winget source");
        var source = _manager.GetPackageCatalogByName("winget") ?? throw new InvalidOperationException("Falta el origen oficial winget.");
        // Do not trust a source merely because it was named winget.
        if (!source.Info.Argument.Equals("https://cdn.winget.microsoft.com/cache", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("El origen winget no corresponde al catálogo oficial de Microsoft.");
        source.AcceptSourceAgreements = true;
        if (installed)
        {
            var options = WinGetActivation.Create<CreateCompositePackageCatalogOptions>();
            options.Catalogs.Add(source);
            options.CompositeSearchBehavior = CompositeSearchBehavior.RemotePackagesFromAllCatalogs;
            source = _manager.CreateCompositePackageCatalog(options);
        }
        _phase?.Invoke("Connecting to official winget source");
        var result = await source.ConnectAsync();
        _info?.Invoke($"Source connection status: {result.Status}.");
        if (result.Status != ConnectResultStatus.Ok) throw new IOException($"WinGet: conexión fallida ({result.Status}).");
        return result.PackageCatalog;
    }
    private async Task<CatalogPackage> Find(string id, bool installed = true)
    {
        var catalog = await Connect(installed);
        var filter = WinGetActivation.Create<PackageMatchFilter>(); filter.Field = PackageMatchField.Id;
        filter.Option = PackageFieldMatchOption.EqualsCaseInsensitive; filter.Value = id;
        var options = WinGetActivation.Create<FindPackagesOptions>(); options.Selectors.Add(filter);
        var result = await catalog.FindPackagesAsync(options);
        if (result.Status != FindPackagesResultStatus.Ok || result.Matches.Count != 1)
            throw new InvalidDataException($"No se encuentra exactamente {id} en WinGet.");
        return result.Matches[0].CatalogPackage;
    }
    private static InstallOptions Options(CatalogPackage package, PackApplication app, string log)
    {
        // Some WinGet server versions expose IVectorView but not IIterable for this WinRT collection.
        // Indexing uses the stable public API, without depending on the optional iterator interface.
        PackageVersionId? version = null;
        var versions = package.AvailableVersions;
        for (int i = 0; i < versions.Count; i++)
        {
            if (versions[i].Version != app.PinnedVersion) continue;
            if (version != null) throw new InvalidDataException("Versión ambigua entre canales del catálogo.");
            version = versions[i];
        }
        if (version == null) throw new InvalidDataException($"La versión fijada {app.PinnedVersion} ya no está disponible.");
        var options = WinGetActivation.Create<InstallOptions>();
        options.PackageVersionId = version; options.PackageInstallScope = PackageInstallScope.System;
        options.PackageInstallMode = PackageInstallMode.Silent; options.AcceptPackageAgreements = true;
        options.AllowHashMismatch = false; options.SkipDependencies = false; options.Force = false;
        options.LogOutputPath = log; options.CorrelationData = app.Id;
        var info = package.GetPackageVersionInfo(version);
        if (!info.HasApplicableInstaller(options)) throw new InvalidDataException("No hay instalador compatible silencioso para todo el equipo.");
        var installer = info.GetApplicableInstaller(options);
        if (installer.Scope != PackageInstallerScope.System || installer.AuthenticationInfo.AuthenticationType != AuthenticationType.None ||
            installer.InstallerType is PackageInstallerType.MSStore or PackageInstallerType.Portable or PackageInstallerType.Msix)
            throw new InvalidDataException("Instalador por usuario, autenticación interactiva o tipo no admitido.");
        return options;
    }
    private static bool Verified(CatalogPackage package, string version) => package.InstalledVersion is { } installed &&
        installed.GetMetadata(PackageVersionMetadataField.InstalledScope).Equals("machine", StringComparison.OrdinalIgnoreCase) &&
        installed.CompareToVersion(version) is CompareResult.Equal or CompareResult.Greater;
    public async Task Preflight(PackItemResult item, string log)
    {
        var package = await Find(item.Application.WingetId);
        if (_manager.GetInstallProgress(package, _manager.GetPackageCatalogByName("winget").Info) is { } active && active.Status == Windows.Foundation.AsyncStatus.Started)
            throw new InvalidOperationException("WinGet todavía tiene una instalación activa de esta aplicación. Espera antes de reanudar.");
        if (Verified(package, item.Application.PinnedVersion)) { item.State = PackItemState.AlreadyInstalled; item.Message = "Ya instalada (versión igual o superior, todo el equipo)."; return; }
        // Never silently replace a user's copy or accept unknown version/scope.
        if (package.InstalledVersion is { } installed &&
            (!installed.GetMetadata(PackageVersionMetadataField.InstalledScope).Equals("machine", StringComparison.OrdinalIgnoreCase) ||
             installed.CompareToVersion(item.Application.PinnedVersion) == CompareResult.Unknown))
            throw new InvalidDataException("Ya existe una instalación por usuario o de versión desconocida; requiere revisión.");
        var options = Options(package, item.Application, log);
        var installer = package.GetPackageVersionInfo(options.PackageVersionId).GetApplicableInstaller(options);
        string type = (installer.InstallerType == PackageInstallerType.Zip ? installer.NestedInstallerType : installer.InstallerType).ToString().ToLowerInvariant();
        await ManifestPolicy.ValidateOnline(item.Application.WingetId, item.Application.PinnedVersion, installer.Architecture.ToString().ToLowerInvariant(), type);
        item.State = PackItemState.Pending; item.Message = "Comprobación previa correcta.";
    }
    public async Task<PackItemResult> Inspect(string id, string version)
    {
        if (version == "latest") version = (await Find(id)).DefaultInstallVersion.Version;
        var item = new PackItemResult { Application = new("inspect", id, id, version) };
        await Preflight(item, ""); return item;
    }
    public async Task Install(PackItemResult item, string log, Action<string> progress, CancellationToken ct)
    {
        var package = await Find(item.Application.WingetId);
        if (Verified(package, item.Application.PinnedVersion)) { item.State = PackItemState.AlreadyInstalled; item.Message = "Ya instalada y verificada."; return; }
        var options = Options(package, item.Application, log);
        Windows.Foundation.IAsyncOperationWithProgress<InstallResult, InstallProgress> operation;
        try { operation = package.InstalledVersion == null ? _manager.InstallPackageAsync(package, options) : _manager.UpgradePackageAsync(package, options); }
        catch (System.Runtime.InteropServices.COMException ex)
        { throw new OperationCanceledException("No se puede determinar si WinGet inició el instalador. Se detiene la cola.", ex, ct); }
        operation.Progress = (_, p) => progress(p.State == PackageInstallProgressState.Downloading
            ? $"Descargando {p.BytesDownloaded / 1048576} / {p.BytesRequired / 1048576} MB" : p.State switch
            {
                PackageInstallProgressState.Queued => "En cola", PackageInstallProgressState.Installing => "Instalando…",
                PackageInstallProgressState.PostInstall => "Finalizando instalación…", _ => "Verificando resultado…"
            });
        // Cancel stops a download, but cannot roll back an active native installer. Stop the queue on timeout/cancel.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromMinutes(60));
        using var registration = timeout.Token.Register(operation.Cancel);
        var task = operation.AsTask();
        InstallResult result;
        try { result = await task.WaitAsync(timeout.Token); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            try { operation.Cancel(); } catch { }
            throw new OperationCanceledException("Se perdió el resultado de WinGet. El instalador puede seguir activo; se detiene la cola.", ex, ct);
        }
        item.RebootRequired |= result.RebootRequired;
        if (result.Status != InstallResultStatus.Ok) throw new InvalidOperationException($"WinGet {result.Status}; instalador {result.InstallerErrorCode}; {result.ExtendedErrorCode?.Message}");
        for (int attempt = 0; attempt < 6; attempt++)
        {
            if (Verified(await Find(item.Application.WingetId), item.Application.PinnedVersion))
            { item.State = PackItemState.Succeeded; item.Message = "Instalada y verificada."; return; }
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
        }
        item.State = PackItemState.VerificationPending;
        item.RequiresRebootBeforeRetry = true;
        item.Message = "WinGet terminó, pero la versión machine-wide aún no se puede verificar. Revisar o reanudar tras reiniciar.";
    }
    public async Task<WingetIndex> Export(string manifestRepository)
    {
        var catalog = await Connect(false);
        _phase?.Invoke("Creating catalogue search options");
        var options = WinGetActivation.Create<FindPackagesOptions>();
        _phase?.Invoke("Enumerating official catalogue packages");
        var result = await catalog.FindPackagesAsync(options);
        _info?.Invoke($"Search status: {result.Status}; matches: {result.Matches.Count}; limit exceeded: {result.WasLimitExceeded}.");
        if (result.Status != FindPackagesResultStatus.Ok || result.WasLimitExceeded || result.Matches.Count < 1000)
            throw new InvalidDataException($"Exportación incompleta: status={result.Status}, matches={result.Matches.Count}, limitExceeded={result.WasLimitExceeded}; no se publicará el índice.");
        var entries = new List<WingetIndexEntry>();
        for (int i = 0; i < result.Matches.Count; i++)
        {
            _phase?.Invoke($"Reading catalogue package {i + 1}/{result.Matches.Count}");
            var match = result.Matches[i];
            var package = match.CatalogPackage; var version = package.DefaultInstallVersion;
            if (version == null) continue;
            _phase?.Invoke($"Validating {package.Id} {version.Version}");
            bool eligible = true; string? reason = null;
            try
            {
                _ = Options(package, new("export", package.Id, package.Name, version.Version), "");
                if (!ManifestPolicy.ValidateLocal(manifestRepository, package.Id, version.Version))
                    throw new InvalidDataException("No declara instalación silenciosa para todo el equipo.");
            }
            catch (Exception ex) { eligible = false; reason = ExportDiagnostics.Describe(ex); }
            _phase?.Invoke($"Reading publisher metadata for {package.Id} {version.Version}");
            entries.Add(new(package.Id, package.Name, version.GetCatalogPackageMetadata().Publisher, version.Version, eligible, reason));
            if ((i + 1) % 100 == 0) _info?.Invoke($"Progress: {i + 1}/{result.Matches.Count}; entries: {entries.Count}; eligible: {entries.Count(x => x.Eligible)}.");
        }
        _phase?.Invoke("Checking catalogue completeness");
        _info?.Invoke($"Catalogue totals: {entries.Count} entries; {entries.Count(x => x.Eligible)} eligible.");
        foreach (var rejection in entries.Where(x => !x.Eligible).GroupBy(x => x.Reason).OrderByDescending(x => x.Count()).Take(10))
            _info?.Invoke($"Rejected packages: {rejection.Count()}; reason: {rejection.Key}");
        if (entries.Count < 1000 || entries.Count(x => x.Eligible) < 100)
            throw new InvalidDataException($"Índice incompleto: {entries.Count} entradas, {entries.Count(x => x.Eligible)} elegibles (mínimos: 1000/100). Se conserva el índice anterior.");
        return new(DateTime.UtcNow, entries);
    }
}
