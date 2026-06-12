# Recompila la app y genera el instalador Setup.exe de un tiron.
# Uso:  .\build-installer.ps1            (version por defecto del .iss)
#       .\build-installer.ps1 -Version 1.1.0
param(
    [string]$Version
)

$ErrorActionPreference = "Stop"
$root      = $PSScriptRoot
$dotnet    = "C:\Users\zits\.dotnet\dotnet.exe"
$iscc      = "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
$csproj    = Join-Path $root "KioskClinicaPC.csproj"
$publishDir= Join-Path $root "publish"
$iss       = Join-Path $root "installer\KioskClinicaPC.iss"

# 1. Limpia el publish anterior (evita arrastrar archivos viejos borrados del proyecto).
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

# 2. Publish Release self-contained win-x64.
Write-Host "==> dotnet publish..." -ForegroundColor Cyan
& $dotnet publish $csproj -c Release -r win-x64 --self-contained true -o $publishDir -nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish fallo (exit $LASTEXITCODE)" }

# 3. Compila el instalador. Si pasas -Version, sobreescribe MyAppVersion del .iss.
Write-Host "==> Compilando instalador..." -ForegroundColor Cyan
$isccArgs = @($iss)
if ($Version) { $isccArgs = @("/DMyAppVersion=$Version", $iss) }
& $iscc @isccArgs
if ($LASTEXITCODE -ne 0) { throw "ISCC fallo (exit $LASTEXITCODE)" }

Write-Host "`nListo. Instalador en: installer\Output\" -ForegroundColor Green
Get-ChildItem (Join-Path $root "installer\Output") | Sort-Object LastWriteTime -Descending | Select-Object -First 1 Name, LastWriteTime
