# Recompila la app y genera el instalador Setup.exe de un tiron.
# Uso:  .\build-installer.ps1                 (version leida del <Version> del .csproj)
#       .\build-installer.ps1 -Version 1.1.0  (sobreescribe la version)
#       .\build-installer.ps1 -ServerApiKey $env:KIOSK_SERVER_API_KEY (Setup provisionado)
#       $env:KIOSK_INITIAL_SETUP_KEY = '<64 hex>'           (genera además el Setup interno con pack)
#       .\build-installer.ps1 -Publish        (ademas crea el GitHub Release vX.Y.Z)
param(
    [string]$Version,
    [string]$ServerUrl = "https://vps-9c7061ff.vps.ovh.net",
    [string]$ServerApiKey = $env:KIOSK_SERVER_API_KEY,
    [string]$InitialSetupKey = $env:KIOSK_INITIAL_SETUP_KEY,
    [string]$SigningKeyId = $env:KIOSK_UPDATE_SIGNING_KEY_ID,
    [string]$SigningPublicKey = $env:KIOSK_UPDATE_SIGNING_PUBLIC_KEY,
    [string]$SigningPrivateKey = $env:KIOSK_UPDATE_SIGNING_PRIVATE_KEY,
    [string]$ReleasePublishKey = $env:KIOSK_RELEASE_PUBLISH_KEY,
    [switch]$Publish
)

$ErrorActionPreference = "Stop"
$root      = $PSScriptRoot
$dotnet    = if (Get-Command dotnet -ErrorAction SilentlyContinue) { (Get-Command dotnet).Source } else { throw "dotnet no está en PATH." }
$iscc      = if (Get-Command iscc -ErrorAction SilentlyContinue) { (Get-Command iscc).Source } elseif (Test-Path "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe") { "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" } else { throw "ISCC no está instalado o no está en PATH." }
$csproj    = Join-Path $root "src\Kiosk.Client\Kiosk.Client.csproj"
$publishDir= Join-Path $root "publish"
$agentProject = Join-Path $root "src\Kiosk.InstallerAgent\Kiosk.InstallerAgent.csproj"
$agentPublishDir = Join-Path $root "publish-agent"
$maintenanceProject = Join-Path $root "src\Kiosk.MaintenanceRunner\Kiosk.MaintenanceRunner.csproj"
$maintenancePublishDir = Join-Path $root "publish-maintenance"
$setupHelperProject = Join-Path $root "src\Kiosk.SetupHelper\Kiosk.SetupHelper.csproj"
$setupHelperPublishDir = Join-Path $root "publish-setup-helper"
$updateRunnerProject = Join-Path $root "src\Kiosk.UpdateRunner\Kiosk.UpdateRunner.csproj"
$updateRunnerPublishDir = Join-Path $root "publish-update-runner"
$releaseToolProject = Join-Path $root "tools\Kiosk.ReleaseTool\Kiosk.ReleaseTool.csproj"
$iss       = Join-Path $root "installer\KioskClinicaPC.iss"
$outputDir = Join-Path $root "installer\Output"
$dotnetBuildArgs = @("--disable-build-servers", "-m:1", "-p:UseSharedCompilation=false")

# 0. Version = fuente unica. Si no se pasa -Version, se lee del <Version> del .csproj.
if (-not $Version) {
    $Version = ([xml](Get-Content $csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    if (-not $Version) { throw "No se encontro <Version> en el .csproj y no se paso -Version." }
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Version debe tener formato X.Y.Z."
}
Write-Host "==> Version: $Version" -ForegroundColor Cyan

# La URL puede vivir en el repositorio; la API key no. Para generar el instalador de una tienda,
# pásala con -ServerApiKey o en KIOSK_SERVER_API_KEY. ISCC la incrusta en el Setup, pero no queda
# escrita en este script ni se imprime en la consola.
$ServerUrl = $ServerUrl.Trim().TrimEnd('/')
if ($ServerUrl -and -not [Uri]::IsWellFormedUriString($ServerUrl, [UriKind]::Absolute)) {
    throw "ServerUrl no es una URL absoluta válida."
}
if ($ServerUrl -and -not $ServerUrl.StartsWith("https://", [StringComparison]::OrdinalIgnoreCase)) {
    throw "ServerUrl debe usar HTTPS."
}
if ($ServerApiKey -and $ServerApiKey -notmatch '^[0-9a-fA-F]{64}$') {
    throw "ServerApiKey debe tener 64 caracteres hexadecimales."
}
if ($InitialSetupKey -and $InitialSetupKey -notmatch '^[0-9a-fA-F]{64}$') {
    throw "InitialSetupKey debe tener 64 caracteres hexadecimales."
}
if ([string]::IsNullOrWhiteSpace($ServerApiKey)) {
    Write-Warning "No se indicó ServerApiKey: el instalador no preconfigurará el servidor."
}
if ([string]::IsNullOrWhiteSpace($InitialSetupKey)) {
    Write-Warning "No se indicó InitialSetupKey: no se generará el instalador interno con pack."
}

# 1. Limpia el publish anterior (evita arrastrar archivos viejos borrados del proyecto).
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
if (Test-Path $agentPublishDir) { Remove-Item $agentPublishDir -Recurse -Force }
if (Test-Path $maintenancePublishDir) { Remove-Item $maintenancePublishDir -Recurse -Force }
if (Test-Path $setupHelperPublishDir) { Remove-Item $setupHelperPublishDir -Recurse -Force }
if (Test-Path $updateRunnerPublishDir) { Remove-Item $updateRunnerPublishDir -Recurse -Force }

# 2. Publish Release self-contained win-x64. Fija la version del assembly = $Version (la lee el
#    auto-update en runtime para comparar con el ultimo release de GitHub).
Write-Host "==> dotnet publish..." -ForegroundColor Cyan
& $dotnet publish $csproj -c Release -r win-x64 --self-contained true -p:Version=$Version -o $publishDir -nologo @dotnetBuildArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet publish fallo (exit $LASTEXITCODE)" }

Write-Host "==> dotnet publish del agente de instalaciones..." -ForegroundColor Cyan
& $dotnet publish $agentProject -c Release -r win-x64 --self-contained true -p:Version=$Version -o $agentPublishDir -nologo @dotnetBuildArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet publish del agente fallo (exit $LASTEXITCODE)" }

Write-Host "==> dotnet publish del ejecutor de mantenimiento..." -ForegroundColor Cyan
& $dotnet publish $maintenanceProject -c Release -r win-x64 --self-contained true -p:Version=$Version -o $maintenancePublishDir -nologo @dotnetBuildArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet publish del ejecutor de mantenimiento fallo (exit $LASTEXITCODE)" }

Write-Host "==> dotnet publish del helper de instalación inicial..." -ForegroundColor Cyan
& $dotnet publish $setupHelperProject -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true `
    -p:DebugType=None -p:DebugSymbols=false -p:Version=$Version -o $setupHelperPublishDir -nologo @dotnetBuildArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet publish del helper fallo (exit $LASTEXITCODE)" }

Write-Host "==> dotnet publish del runner de actualizaciones..." -ForegroundColor Cyan
& $dotnet publish $updateRunnerProject -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true `
    -p:DebugType=None -p:DebugSymbols=false -p:Version=$Version -o $updateRunnerPublishDir -nologo @dotnetBuildArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet publish del runner de actualizaciones fallo (exit $LASTEXITCODE)" }

# Las claves públicas viajan con Kiosk; la clave privada nunca entra en el artefacto.
if ($SigningKeyId -and $SigningPublicKey) {
    if ($SigningKeyId -notmatch '^[A-Za-z0-9_-]{1,40}$') { throw "SigningKeyId no válido." }
    $keyDir = Join-Path $publishDir "update-keys"
    New-Item -ItemType Directory -Path $keyDir -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $keyDir "$SigningKeyId.pem"), $SigningPublicKey, [Text.UTF8Encoding]::new($false))
}
elseif ($Publish) { throw "Para publicar se requieren KIOSK_UPDATE_SIGNING_KEY_ID y KIOSK_UPDATE_SIGNING_PUBLIC_KEY." }

# 3. Compila primero el artefacto público/auto-update, siempre Kiosk-only y sin secretos.
Write-Host "==> Compilando instalador público Kiosk-only..." -ForegroundColor Cyan
& $iscc "/DMyAppVersion=$Version" $iss
if ($LASTEXITCODE -ne 0) { throw "ISCC fallo (exit $LASTEXITCODE)" }

# El instalador interno se sirve desde el panel y contiene credenciales de provisión de esta tienda.
$internalSetup = $null
if ($ServerUrl -and $ServerApiKey -and $InitialSetupKey) {
    Write-Host "==> Compilando instalador interno con pack..." -ForegroundColor Cyan
    $internalArgs = @(
        "/DMyAppVersion=$Version",
        "/DInternalSetup=1",
        "/DDefaultServerUrl=$ServerUrl",
        "/DDefaultServerApiKey=$ServerApiKey",
        "/DDefaultInitialSetupKey=$InitialSetupKey",
        $iss
    )
    & $iscc @internalArgs
    if ($LASTEXITCODE -ne 0) { throw "ISCC interno fallo (exit $LASTEXITCODE)" }
    $internalSetup = Join-Path $outputDir "Setup-EquipoClinicaPC-$Version.exe"
    if (-not (Test-Path $internalSetup)) { throw "No se encontró el instalador interno esperado: $internalSetup" }
    $internalInfo = Get-Item $internalSetup
    $internalHash = (Get-FileHash $internalSetup -Algorithm SHA256).Hash.ToLower()
    $bundleManifest = [ordered]@{
        version = $Version
        fileName = $internalInfo.Name
        sizeBytes = $internalInfo.Length
        sha256 = $internalHash
        serverUrl = $ServerUrl
        createdAtUtc = [DateTime]::UtcNow.ToString("o")
    }
    $bundlePath = Join-Path $outputDir "Setup-EquipoClinicaPC-$Version.bundle.json"
    $bundleManifest | ConvertTo-Json | Out-File -LiteralPath $bundlePath -Encoding utf8
    Write-Host "Instalador interno: $internalSetup" -ForegroundColor Green
}

# 4. Localiza el Setup y genera su checksum SHA256 (lo verifica el auto-update antes de instalar).
$setup = Join-Path $outputDir "Setup-KioskClinicaPC-$Version.exe"
if (-not (Test-Path $setup)) { throw "No se encontro el instalador esperado: $setup" }
$shaFile = "$setup.sha256"
$hash = (Get-FileHash $setup -Algorithm SHA256).Hash.ToLower()
# Formato estandar "<hash> *<archivo>" (el cliente toma el primer token como hash).
"$hash *$(Split-Path $setup -Leaf)" | Out-File -FilePath $shaFile -Encoding ascii -NoNewline
Write-Host "==> SHA256: $hash" -ForegroundColor Green

# 5. Opcional: publica el GitHub Release con el Setup + su .sha256 como assets.
if ($Publish) {
    if (-not $SigningPrivateKey) { throw "KIOSK_UPDATE_SIGNING_PRIVATE_KEY es obligatoria para publicar." }
    if (-not $ReleasePublishKey) { throw "KIOSK_RELEASE_PUBLISH_KEY es obligatoria para importar la release en la VPS." }
    Write-Host "==> Publicando release v$Version en GitHub..." -ForegroundColor Cyan
    $githubRepository = if ($env:GITHUB_REPOSITORY) { $env:GITHUB_REPOSITORY } else { "zetaits/KioskClinicaPC" }
    $fallbackUrl = "https://github.com/$githubRepository/releases/download/v$Version/$(Split-Path $setup -Leaf)"
    $manifestPath = Join-Path $outputDir "kiosk-release-$Version.json"
    $signaturePath = "$manifestPath.sig"
    $manifest = [ordered]@{
        schemaVersion = 1; version = $Version; fileName = (Split-Path $setup -Leaf)
        sizeBytes = (Get-Item $setup).Length; sha256 = $hash
        publishedAtUtc = [DateTime]::UtcNow.ToString("o"); keyId = $SigningKeyId
        minimumUpdaterVersion = "1.2.0"; releaseNotes = "Version $Version"; githubFallbackUrl = $fallbackUrl
    }
    [IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Compress), [Text.UTF8Encoding]::new($false))
    $previousPrivateKey = $env:KIOSK_UPDATE_SIGNING_PRIVATE_KEY
    $previousPublicKey = $env:KIOSK_UPDATE_SIGNING_PUBLIC_KEY
    try {
        $env:KIOSK_UPDATE_SIGNING_PRIVATE_KEY = $SigningPrivateKey
        $env:KIOSK_UPDATE_SIGNING_PUBLIC_KEY = $SigningPublicKey
        & $dotnet run --project $releaseToolProject -c Release -- sign $manifestPath $signaturePath
        if ($LASTEXITCODE -ne 0) { throw "La firma ECDSA del manifiesto falló (exit $LASTEXITCODE)" }
    }
    finally {
        $env:KIOSK_UPDATE_SIGNING_PRIVATE_KEY = $previousPrivateKey
        $env:KIOSK_UPDATE_SIGNING_PUBLIC_KEY = $previousPublicKey
    }
    & gh release view "v$Version" --json tagName *> $null
    if ($LASTEXITCODE -eq 0) {
        throw "La release v$Version ya existe. No se sobrescribe porque sus binarios y manifiesto son inmutables."
    }
    & gh release create "v$Version" $setup $shaFile $manifestPath $signaturePath --title "v$Version" --notes "Version $Version"
    if ($LASTEXITCODE -ne 0) { throw "gh release create fallo (exit $LASTEXITCODE)" }
    $headers = @{ "X-Release-Publish-Key" = $ReleasePublishKey }
    $form = @{ manifest = Get-Item $manifestPath; signature = Get-Content $signaturePath -Raw; setup = Get-Item $setup }
    Invoke-RestMethod -Method Post -Uri "$($ServerUrl.TrimEnd('/'))/api/releases" -Headers $headers -Form $form | Out-Null
    Write-Host "Release v$Version publicado." -ForegroundColor Green
}

Write-Host "`nListo. Instalador en: installer\Output\" -ForegroundColor Green
Get-ChildItem $outputDir | Sort-Object LastWriteTime -Descending | Select-Object -First 2 Name, LastWriteTime
