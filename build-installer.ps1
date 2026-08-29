# Recompila la app y genera el instalador Setup.exe de un tiron.
# Uso:  .\build-installer.ps1                 (version leida del <Version> del .csproj)
#       .\build-installer.ps1 -Version 1.1.0  (sobreescribe la version)
#       .\build-installer.ps1 -ServerApiKey $env:KIOSK_SERVER_API_KEY (Setup provisionado)
#       .\build-installer.ps1 -Publish        (ademas crea el GitHub Release vX.Y.Z)
param(
    [string]$Version,
    [string]$ServerUrl = "https://vps-9c7061ff.vps.ovh.net",
    [string]$ServerApiKey = $env:KIOSK_SERVER_API_KEY,
    [switch]$Publish
)

$ErrorActionPreference = "Stop"
$root      = $PSScriptRoot
$dotnet    = "C:\Users\zits\.dotnet\dotnet.exe"
$iscc      = "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
$csproj    = Join-Path $root "src\Kiosk.Client\Kiosk.Client.csproj"
$publishDir= Join-Path $root "publish"
$agentProject = Join-Path $root "src\Kiosk.InstallerAgent\Kiosk.InstallerAgent.csproj"
$agentPublishDir = Join-Path $root "publish-agent"
$maintenanceProject = Join-Path $root "src\Kiosk.MaintenanceRunner\Kiosk.MaintenanceRunner.csproj"
$maintenancePublishDir = Join-Path $root "publish-maintenance"
$iss       = Join-Path $root "installer\KioskClinicaPC.iss"
$outputDir = Join-Path $root "installer\Output"

# 0. Version = fuente unica. Si no se pasa -Version, se lee del <Version> del .csproj.
if (-not $Version) {
    $Version = ([xml](Get-Content $csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    if (-not $Version) { throw "No se encontro <Version> en el .csproj y no se paso -Version." }
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

# 1. Limpia el publish anterior (evita arrastrar archivos viejos borrados del proyecto).
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
if (Test-Path $agentPublishDir) { Remove-Item $agentPublishDir -Recurse -Force }
if (Test-Path $maintenancePublishDir) { Remove-Item $maintenancePublishDir -Recurse -Force }

# 2. Publish Release self-contained win-x64. Fija la version del assembly = $Version (la lee el
#    auto-update en runtime para comparar con el ultimo release de GitHub).
Write-Host "==> dotnet publish..." -ForegroundColor Cyan
& $dotnet publish $csproj -c Release -r win-x64 --self-contained true -p:Version=$Version -o $publishDir -nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish fallo (exit $LASTEXITCODE)" }

Write-Host "==> dotnet publish del agente de instalaciones..." -ForegroundColor Cyan
& $dotnet publish $agentProject -c Release -r win-x64 --self-contained true -p:Version=$Version -o $agentPublishDir -nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish del agente fallo (exit $LASTEXITCODE)" }

Write-Host "==> dotnet publish del ejecutor de mantenimiento..." -ForegroundColor Cyan
& $dotnet publish $maintenanceProject -c Release -r win-x64 --self-contained true -p:Version=$Version -o $maintenancePublishDir -nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish del ejecutor de mantenimiento fallo (exit $LASTEXITCODE)" }

# 3. Compila el instalador con la misma version.
Write-Host "==> Compilando instalador..." -ForegroundColor Cyan
$isccArgs = @("/DMyAppVersion=$Version")
if ($ServerUrl -and $ServerApiKey) {
    $isccArgs += "/DDefaultServerUrl=$ServerUrl"
    $isccArgs += "/DDefaultServerApiKey=$ServerApiKey"
}
$isccArgs += $iss
& $iscc @isccArgs
if ($LASTEXITCODE -ne 0) { throw "ISCC fallo (exit $LASTEXITCODE)" }

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
    Write-Host "==> Publicando release v$Version en GitHub..." -ForegroundColor Cyan
    & gh release create "v$Version" $setup $shaFile --title "v$Version" --notes "Version $Version"
    if ($LASTEXITCODE -ne 0) { throw "gh release create fallo (exit $LASTEXITCODE)" }
    Write-Host "Release v$Version publicado." -ForegroundColor Green
}

Write-Host "`nListo. Instalador en: installer\Output\" -ForegroundColor Green
Get-ChildItem $outputDir | Sort-Object LastWriteTime -Descending | Select-Object -First 2 Name, LastWriteTime
