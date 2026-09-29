param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot "src\Kiosk.Server\bin\Release\publish-linux")
)

$ErrorActionPreference = "Stop"
$dotnet = "C:\Users\zits\.dotnet\dotnet.exe"
$project = Join-Path $PSScriptRoot "src\Kiosk.Server\Kiosk.Server.csproj"
$allowedRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "src\Kiosk.Server\bin\Release"))
$output = [IO.Path]::GetFullPath($OutputDirectory)

if (-not $output.StartsWith($allowedRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "La salida debe estar dentro de $allowedRoot"
}

if (Test-Path -LiteralPath $output) {
    Remove-Item -LiteralPath $output -Recurse -Force
}

& $dotnet publish $project -c Release -nologo -o $output -m:1 `
    -p:BuildInParallel=false -p:UseSharedCompilation=false -nodeReuse:false
if ($LASTEXITCODE -ne 0) { throw "dotnet publish terminó con código $LASTEXITCODE" }

$deployOutput = Join-Path $output "deploy"
New-Item -ItemType Directory -Path $deployOutput | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "deploy\ubuntu\kiosk-server.service") -Destination $deployOutput
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "deploy\ubuntu\kiosk-server.env.example") -Destination $deployOutput
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "deploy\ubuntu\Caddyfile.example") -Destination $deployOutput
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "docs\SERVIDOR.md") -Destination (Join-Path $output "LEEME-DESPLIEGUE.md")

$forbidden = Get-ChildItem -LiteralPath $output -Directory -Recurse |
    Where-Object Name -in @("data", "assets", "installers", "setups", "updates")
if ($forbidden) {
    throw "El paquete contiene directorios de datos runtime y no es seguro subirlo: $($forbidden.FullName -join ', ')"
}

Write-Host "Paquete Linux preparado en: $output"
