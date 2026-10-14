# Recompila la app y genera el instalador Setup.exe de un tiron.
# Uso:  .\build-installer.ps1                 (version leida del <Version> del .csproj)
#       .\build-installer.ps1 -Version 1.1.0  (sobreescribe la version)
#       .\build-installer.ps1 -ServerApiKey $env:KIOSK_SERVER_API_KEY (Setup provisionado)
#       .\build-installer.ps1 -PrepareRelease (firma los artefactos para importar en la VPS)
param(
    [string]$Version,
    [string]$ServerUrl = "https://panel.clinicapc.es",
    [string]$ServerApiKey = $env:KIOSK_SERVER_API_KEY,
    [string]$SigningKeyId = $env:KIOSK_UPDATE_SIGNING_KEY_ID,
    [string]$SigningPublicKey = $env:KIOSK_UPDATE_SIGNING_PUBLIC_KEY,
    [string]$SigningPrivateKey = $env:KIOSK_UPDATE_SIGNING_PRIVATE_KEY,
    [switch]$PrepareRelease
)

$ErrorActionPreference = "Stop"
$root      = $PSScriptRoot
. (Join-Path $root 'tools\resolve-dotnet.ps1')
$dotnet = Resolve-KioskDotnet
$iscc      = if (Get-Command iscc -ErrorAction SilentlyContinue) { (Get-Command iscc).Source } elseif (Test-Path "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe") { "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" } else { throw "ISCC no está instalado o no está en PATH." }
$csproj    = Join-Path $root "src\Kiosk.Client\Kiosk.Client.csproj"
$publishDir= Join-Path $root "publish"
$agentProject = Join-Path $root "src\Kiosk.InstallerAgent\Kiosk.InstallerAgent.csproj"
$agentPublishDir = Join-Path $root "publish-agent"
$maintenanceProject = Join-Path $root "src\Kiosk.MaintenanceRunner\Kiosk.MaintenanceRunner.csproj"
$maintenancePublishDir = Join-Path $root "publish-maintenance"
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
if ([string]::IsNullOrWhiteSpace($ServerApiKey)) {
    Write-Warning "No se indicó ServerApiKey: el instalador no preconfigurará el servidor."
}
if ($PrepareRelease -and ([string]::IsNullOrWhiteSpace($ServerUrl) -or [string]::IsNullOrWhiteSpace($ServerApiKey))) {
    throw "Para preparar una release que conecte los kioscos se requieren ServerUrl y KIOSK_SERVER_API_KEY."
}

# 1. Valida las rutas antes de limpiar salidas generadas.
foreach ($generatedPath in @($publishDir, $agentPublishDir, $maintenancePublishDir, $updateRunnerPublishDir)) {
    $resolvedPath = [IO.Path]::GetFullPath($generatedPath)
    if ([IO.Path]::GetDirectoryName($resolvedPath) -ne [IO.Path]::GetFullPath($root)) { throw "Salida fuera del workspace: $resolvedPath" }
}
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
if (Test-Path $agentPublishDir) { Remove-Item $agentPublishDir -Recurse -Force }
if (Test-Path $maintenancePublishDir) { Remove-Item $maintenancePublishDir -Recurse -Force }
if (Test-Path $updateRunnerPublishDir) { Remove-Item $updateRunnerPublishDir -Recurse -Force }

# 2. Publish Release self-contained win-x64. Fija la version del assembly = $Version
#    para comparar con la version objetivo asignada por la VPS.
Write-Host "==> dotnet publish..." -ForegroundColor Cyan
& $dotnet publish $csproj -c Release -r win-x64 --self-contained true -p:Version=$Version -o $publishDir -nologo @dotnetBuildArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet publish fallo (exit $LASTEXITCODE)" }

Write-Host "==> dotnet publish del agente de instalaciones..." -ForegroundColor Cyan
& $dotnet publish $agentProject -c Release -r win-x64 --self-contained true -p:Version=$Version -o $agentPublishDir -nologo @dotnetBuildArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet publish del agente fallo (exit $LASTEXITCODE)" }

Write-Host "==> dotnet publish del ejecutor de mantenimiento..." -ForegroundColor Cyan
& $dotnet publish $maintenanceProject -c Release -r win-x64 --self-contained true -p:Version=$Version -o $maintenancePublishDir -nologo @dotnetBuildArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet publish del ejecutor de mantenimiento fallo (exit $LASTEXITCODE)" }

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
elseif ($PrepareRelease) { throw "Para preparar una release se requieren KIOSK_UPDATE_SIGNING_KEY_ID y KIOSK_UPDATE_SIGNING_PUBLIC_KEY." }

# 3. Compila el componente Kiosk-only. Con API key, aprovisiona perfiles antiguos
#    sin servidor; la clave se puede extraer de cualquier copia del componente.
Write-Host "==> Compilando componente Kiosk-only..." -ForegroundColor Cyan
$setupArgs = @("/DMyAppVersion=$Version")
if ($ServerUrl -and $ServerApiKey) {
    $setupArgs += "/DDefaultServerUrl=$ServerUrl"
    $setupArgs += "/DDefaultServerApiKey=$ServerApiKey"
}
$setupArgs += $iss
& $iscc @setupArgs
if ($LASTEXITCODE -ne 0) { throw "ISCC fallo (exit $LASTEXITCODE)" }

# 4. Localiza el Setup y genera su checksum SHA256 (lo verifica el auto-update antes de instalar).
$setup = Join-Path $outputDir "Setup-KioskClinicaPC-$Version.exe"
if (-not (Test-Path $setup)) { throw "No se encontro el instalador esperado: $setup" }
$shaFile = "$setup.sha256"
$hash = (Get-FileHash $setup -Algorithm SHA256).Hash.ToLower()
# Formato estandar "<hash> *<archivo>" (el cliente toma el primer token como hash).
"$hash *$(Split-Path $setup -Leaf)" | Out-File -FilePath $shaFile -Encoding ascii -NoNewline
Write-Host "==> SHA256: $hash" -ForegroundColor Green

# 5. Opcional: firma el instalador para la importación privada en la VPS.
if ($PrepareRelease) {
    if (-not $SigningPrivateKey) { throw "KIOSK_UPDATE_SIGNING_PRIVATE_KEY es obligatoria para publicar." }
    Write-Host "==> Firmando release v$Version para la VPS..." -ForegroundColor Cyan
    $manifestPath = Join-Path $outputDir "kiosk-release-$Version.json"
    $signaturePath = "$manifestPath.sig"
    $manifest = [ordered]@{
        schemaVersion = 1; version = $Version; fileName = (Split-Path $setup -Leaf)
        sizeBytes = (Get-Item $setup).Length; sha256 = $hash
        publishedAtUtc = [DateTime]::UtcNow.ToString("o"); keyId = $SigningKeyId
        minimumUpdaterVersion = "1.2.0"; releaseNotes = "Version $Version"
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
    Write-Host "Release v$Version firmada. Importa estos artefactos desde el workflow protegido." -ForegroundColor Green
}

Write-Host "`nListo. Instalador en: installer\Output\" -ForegroundColor Green
Get-ChildItem $outputDir | Sort-Object LastWriteTime -Descending | Select-Object -First 2 Name, LastWriteTime
