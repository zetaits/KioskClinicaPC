# Recompila la app y genera el instalador Setup.exe de un tiron.
# Uso:  .\build-installer.ps1                 (version leida del <Version> del .csproj)
#       .\build-installer.ps1 -Version 1.1.0  (sobreescribe la version)
#       .\build-installer.ps1 -Publish        (ademas crea el GitHub Release vX.Y.Z)
param(
    [string]$Version,
    [switch]$Publish
)

$ErrorActionPreference = "Stop"
$root      = $PSScriptRoot
$dotnet    = "C:\Users\zits\.dotnet\dotnet.exe"
$iscc      = "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
$csproj    = Join-Path $root "src\Kiosk.Client\Kiosk.Client.csproj"
$publishDir= Join-Path $root "publish"
$iss       = Join-Path $root "installer\KioskClinicaPC.iss"
$outputDir = Join-Path $root "installer\Output"

# 0. Version = fuente unica. Si no se pasa -Version, se lee del <Version> del .csproj.
if (-not $Version) {
    $Version = ([xml](Get-Content $csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    if (-not $Version) { throw "No se encontro <Version> en el .csproj y no se paso -Version." }
}
Write-Host "==> Version: $Version" -ForegroundColor Cyan

# 1. Limpia el publish anterior (evita arrastrar archivos viejos borrados del proyecto).
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

# 2. Publish Release self-contained win-x64. Fija la version del assembly = $Version (la lee el
#    auto-update en runtime para comparar con el ultimo release de GitHub).
Write-Host "==> dotnet publish..." -ForegroundColor Cyan
& $dotnet publish $csproj -c Release -r win-x64 --self-contained true -p:Version=$Version -o $publishDir -nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish fallo (exit $LASTEXITCODE)" }

# 3. Compila el instalador con la misma version.
Write-Host "==> Compilando instalador..." -ForegroundColor Cyan
& $iscc "/DMyAppVersion=$Version" $iss
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
