param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot "src\Kiosk.Server\bin\Release\publish-linux")
)

$ErrorActionPreference = "Stop"
$dotnetCandidates = @()
$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
if ($dotnetCommand) { $dotnetCandidates += $dotnetCommand.Source }
$dotnetCandidates += Join-Path $env:USERPROFILE ".dotnet\dotnet.exe"
$dotnetCandidates += Join-Path $env:TEMP "clinicapc-dotnet\dotnet.exe"
$dotnet = $null
Push-Location $PSScriptRoot
try {
    foreach ($candidate in ($dotnetCandidates | Select-Object -Unique)) {
        if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) { continue }
        try {
            $sdkVersion = & $candidate --version 2>$null
            if ($LASTEXITCODE -eq 0 -and $sdkVersion -match '^10\.') {
                $dotnet = $candidate
                break
            }
        }
        catch { continue }
    }
}
finally { Pop-Location }
if (-not $dotnet) { throw "No se encuentra un SDK .NET 10 compatible. Instálalo y vuelve a ejecutar el script." }
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
