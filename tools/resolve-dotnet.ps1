function Resolve-KioskDotnet {
    $candidates = @()
    $command = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($command) { $candidates += $command.Source }
    $candidates += Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
    $candidates += Join-Path $env:TEMP 'clinicapc-dotnet\dotnet.exe'
    $candidates += 'C:\Users\zits\.dotnet\dotnet.exe'
    foreach ($candidate in ($candidates | Select-Object -Unique)) {
        if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) { continue }
        try {
            $version = & $candidate --version 2>$null
            if ($LASTEXITCODE -eq 0 -and $version -match '^10\.') { return $candidate }
        } catch { continue }
    }
    throw 'No se encuentra un SDK .NET 10 compatible con global.json.'
}
