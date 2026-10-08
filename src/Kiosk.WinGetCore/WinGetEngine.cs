using System.IO;
using Microsoft.Management.Deployment;
using KioskClinicaPC.Core.Sync;

namespace Kiosk.SetupHelper;

internal sealed class WinGetEngine : IPackBackend
{
    private readonly PackageManager _manager;
    private readonly Action<string>? _phase, _info;
    private bool _latest;
    private PackCatalogSession? _catalogSession;
    private CancellationToken _preflightToken;
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
    private PackageCatalogReference OfficialSource()
    {
        _phase?.Invoke("Locating and validating official winget source");
        var source = _manager.GetPackageCatalogByName("winget") ?? throw new IOException("Falta el origen oficial winget.");
        // Do not trust a source merely because it was named winget.
        if (!source.Info.Argument.Equals("https://cdn.winget.microsoft.com/cache", StringComparison.OrdinalIgnoreCase))
            throw new KioskClinicaPC.Equipment.NativeStateException("El origen winget no corresponde al catálogo oficial de Microsoft.");
        source.AcceptSourceAgreements = true;
        return source;
    }
    public async Task PrepareLatest(CancellationToken ct)
    {
        _latest = true; _preflightToken = ct;
        _catalogSession = new PackCatalogSession(Refresh);
        await _catalogSession.Prepare(ct);
    }
    private async Task Refresh(CancellationToken ct)
    {
        _phase?.Invoke("Actualizando el catálogo oficial WinGet…");
        var source = OfficialSource();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromMinutes(2));
        var operation = source.RefreshPackageCatalogAsync();
        using var registration = timeout.Token.Register(operation.Cancel);
        RefreshPackageCatalogResult result;
        try { result = await operation.AsTask().WaitAsync(timeout.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new IOException("El catálogo WinGet no respondió en dos minutos."); }
        if (result.Status != RefreshPackageCatalogStatus.Ok) throw new IOException($"Actualización WinGet fallida ({result.Status}).");
    }
    private async Task<PackageCatalog> Connect(bool installed)
    {
        var source = OfficialSource();
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
            throw new CatalogDriftException($"No se encuentra exactamente {id} en el catálogo oficial WinGet.");
        return result.Matches[0].CatalogPackage;
    }
    private static InstallOptions Options(CatalogPackage package, PackApplication app, string log, string? channel = null)
    {
        // Some WinGet server versions expose IVectorView but not IIterable for this WinRT collection.
        // Indexing uses the stable public API, without depending on the optional iterator interface.
        PackageVersionId? version = null;
        var versions = package.AvailableVersions;
        for (int i = 0; i < versions.Count; i++)
        {
            if (versions[i].Version != app.PinnedVersion || (channel is not null && versions[i].Channel != channel)) continue;
            if (version != null) throw new InvalidDataException("Versión ambigua entre canales del catálogo.");
            version = versions[i];
        }
        if (version == null) throw new CatalogDriftException($"La versión resuelta {app.PinnedVersion} no está disponible en WinGet. Reanuda los pendientes para comprobar las versiones actuales.");
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
        if (_catalogSession is not null) await _catalogSession.Check(() => Check(item, log), _preflightToken);
        else await Check(item, log);
    }
    private async Task Check(PackItemResult item, string log)
    {
        var package = await Find(item.Application.WingetId);
        if (_manager.GetInstallProgress(package, _manager.GetPackageCatalogByName("winget").Info) is { } active && active.Status == Windows.Foundation.AsyncStatus.Started)
        {
            item.RequiresRebootBeforeRetry = true;
            throw new InvalidOperationException("WinGet todavía tiene una instalación activa de esta aplicación. Espera antes de reanudar.");
        }
        if (!item.ResolveLatest && Verified(package, item.Application.PinnedVersion))
        { item.InstalledVersion = package.InstalledVersion.Version; item.State = PackItemState.AlreadyInstalled; item.Message = "Ya instalada y verificada (todo el equipo)."; return; }
        if (_latest) await Resolve(package, item, log);
        // Never silently replace a user's copy or accept unknown version/scope.
        if (package.InstalledVersion is { } installed &&
            (!installed.GetMetadata(PackageVersionMetadataField.InstalledScope).Equals("machine", StringComparison.OrdinalIgnoreCase) ||
             installed.CompareToVersion(item.Application.PinnedVersion) == CompareResult.Unknown))
            throw new InvalidDataException("Ya existe una instalación por usuario o de versión desconocida; requiere revisión.");
        if (Verified(package, item.Application.PinnedVersion))
        { item.InstalledVersion = package.InstalledVersion.Version; item.State = PackItemState.AlreadyInstalled; item.Message = "Ya instalada (versión igual o superior, todo el equipo)."; return; }
        var options = Options(package, item.Application, log, item.ResolvedChannel);
        var installer = package.GetPackageVersionInfo(options.PackageVersionId).GetApplicableInstaller(options);
        string type = (installer.InstallerType == PackageInstallerType.Zip ? installer.NestedInstallerType : installer.InstallerType).ToString().ToLowerInvariant();
        if (!_latest) await ManifestPolicy.ValidateOnline(item.Application.WingetId, item.Application.PinnedVersion, installer.Architecture.ToString().ToLowerInvariant(), type, _preflightToken);
        item.State = PackItemState.Pending; item.Message = "Comprobación previa correcta.";
    }
    private async Task Resolve(CatalogPackage package, PackItemResult item, string log)
    {
        var defaultVersion = package.DefaultInstallVersion ?? throw new CatalogDriftException("WinGet no ofrece una versión aplicable de esta aplicación.");
        var keys = new Dictionary<PackVersionCandidate, PackageVersionId>();
        var available = package.AvailableVersions;
        for (int i = 0; i < available.Count; i++)
        {
            var key = available[i];
            if (key.Channel != defaultVersion.Channel) continue;
            if (!keys.TryAdd(new(key.Version, key.Channel), key)) throw new InvalidDataException("Versión ambigua en el catálogo oficial.");
        }
        var selected = await PackVersionPolicy.Select(keys.Keys, defaultVersion.Channel, (a, b) =>
            package.GetPackageVersionInfo(keys[a]).CompareToVersion(b.Version) switch
            { CompareResult.Greater => 1, CompareResult.Equal => 0, CompareResult.Lesser => -1, _ => null }, async candidate =>
        {
            var application = item.Application with { PinnedVersion = candidate.Version };
            InstallOptions options;
            try { options = Options(package, application, log, candidate.Channel); }
            catch (InvalidDataException) { return false; }
            var installer = package.GetPackageVersionInfo(options.PackageVersionId).GetApplicableInstaller(options);
            var type = (installer.InstallerType == PackageInstallerType.Zip ? installer.NestedInstallerType : installer.InstallerType).ToString().ToLowerInvariant();
            try { await ManifestPolicy.ValidateOnline(application.WingetId, candidate.Version, installer.Architecture.ToString().ToLowerInvariant(), type, _preflightToken); }
            catch (InvalidDataException) { return false; }
            return true;
        }, _preflightToken);
        item.Application = item.Application with { PinnedVersion = selected.Version };
        item.ResolvedChannel = selected.Channel; item.ResolveLatest = false;
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
        if (Verified(package, item.Application.PinnedVersion)) { item.InstalledVersion = package.InstalledVersion.Version; item.State = PackItemState.AlreadyInstalled; item.Message = "Ya instalada y verificada."; return; }
        var options = Options(package, item.Application, log, item.ResolvedChannel);
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
        if (ct.IsCancellationRequested || result.Status == InstallResultStatus.InternalError)
            throw new OperationCanceledException("El estado nativo no puede confirmarse. Se detiene la cola para verificarlo antes de reintentar.", ct);
        if (result.Status != InstallResultStatus.Ok) throw new InvalidOperationException($"WinGet {result.Status}; instalador {result.InstallerErrorCode}; {result.ExtendedErrorCode?.Message}");
        try
        {
            for (int attempt = 0; attempt < 6; attempt++)
            {
                var installed = await Find(item.Application.WingetId);
                if (Verified(installed, item.Application.PinnedVersion))
                { item.InstalledVersion = installed.InstalledVersion.Version; item.State = PackItemState.Succeeded; item.Message = "Instalada y verificada."; return; }
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { throw new OperationCanceledException("Se perdió la verificación del instalador nativo. Se detiene la cola.", ex, ct); }
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
        var entries = await CatalogExportReader.ReadAsync(result.Matches.Count, i =>
        {
            _phase?.Invoke($"Reading catalogue package {i + 1}/{result.Matches.Count}");
            var match = result.Matches[i];
            var package = match.CatalogPackage;
            string id = package.Id;
            _phase?.Invoke($"Reading default version for {id} (package {i + 1}/{result.Matches.Count})");
            var version = package.DefaultInstallVersion ?? throw new InvalidDataException($"No default version for {id}.");
            string name = package.Name;
            _phase?.Invoke($"Validating {id} {version.Version}");
            bool eligible = true; string? reason = null;
            try
            {
                _ = Options(package, new("export", id, name, version.Version), "");
                if (!ManifestPolicy.ValidateLocal(manifestRepository, id, version.Version))
                    throw new InvalidDataException("No declara instalación silenciosa para todo el equipo.");
            }
            catch (InvalidDataException ex) { eligible = false; reason = ExportDiagnostics.Describe(ex); }
            _phase?.Invoke($"Reading publisher metadata for {id} {version.Version}");
            return new WingetIndexEntry(id, name, version.GetCatalogPackageMetadata().Publisher, version.Version, eligible, reason);
        }, _info);
        _phase?.Invoke("Checking catalogue completeness");
        foreach (var rejection in entries.Where(x => !x.Eligible).GroupBy(x => x.Reason).OrderByDescending(x => x.Count()).Take(10))
            _info?.Invoke($"Rejected packages: {rejection.Count()}; reason: {rejection.Key}");
        return new(DateTime.UtcNow, entries);
    }
}
