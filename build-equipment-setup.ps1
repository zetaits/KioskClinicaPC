param(
    [string]$Version = '1.4.0',
    [string]$KioskVersion,
    [switch]$Publish
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
. (Join-Path $root 'tools\resolve-dotnet.ps1')
$dotnet = Resolve-KioskDotnet
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Version debe tener formato X.Y.Z.' }
if (-not $KioskVersion) { $KioskVersion = ([xml](Get-Content -LiteralPath (Join-Path $root 'src\Kiosk.Client\Kiosk.Client.csproj'))).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1 }
if ($KioskVersion -notmatch '^\d+\.\d+\.\d+$') { throw 'KioskVersion debe tener formato X.Y.Z.' }
$serverUrl = 'https://setup.invalid'
$serverKey = '0' * 64
$setupKey = '1' * 64
if ($Publish) {
    if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'La publicación requiere PowerShell 7.' }
    $serverUrl = $env:KIOSK_SERVER_URL
    $serverKey = $env:KIOSK_SERVER_API_KEY
    $setupKey = $env:KIOSK_INITIAL_SETUP_KEY
    if (-not $serverUrl -or $serverKey -notmatch '^[a-fA-F0-9]{64}$' -or $setupKey -notmatch '^[a-fA-F0-9]{64}$' -or -not $env:KIOSK_RELEASE_PUBLISH_KEY) { throw 'Faltan las credenciales de production para publicar.' }
}
$serverUri = $null
if (-not [Uri]::TryCreate($serverUrl, [UriKind]::Absolute, [ref]$serverUri) -or $serverUri.Scheme -ne 'https' -or $serverUri.UserInfo -or $serverUri.Query -or $serverUri.Fragment) { throw 'Servidor HTTPS no válido.' }
$serverUrl = $serverUrl.TrimEnd('/')
$build = Join-Path $root 'equipment-build'
$resources = Join-Path $build 'resources'
$worker = Join-Path $build 'worker'
$frontend = Join-Path $build 'frontend'
$output = Join-Path $root 'installer\Output'
$setup = Join-Path $output "Setup-EquipoClinicaPC-$Version.exe"
$bundle = Join-Path $output "Setup-EquipoClinicaPC-$Version.bundle.json"
if (Test-Path -LiteralPath $setup) { throw "Ya existe el asistente $Version. No se sobrescribe; elige otra versión o retira manualmente un build local ficticio." }
if (Test-Path -LiteralPath $bundle) { throw "Ya existe el manifiesto $Version." }
# Validate the absolute recursive deletion target before removing generated files.
if ([IO.Path]::GetFullPath($build) -ne [IO.Path]::GetFullPath((Join-Path $root 'equipment-build'))) { throw 'Salida fuera del workspace.' }
if (Test-Path -LiteralPath $build) { Remove-Item -LiteralPath $build -Recurse -Force }
New-Item -ItemType Directory -Path $resources,$worker,$frontend,$output -Force | Out-Null
$buildArgs = @('--disable-build-servers', '-m:1', '-p:UseSharedCompilation=false')
$sourceCommit = (& git -C $root rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $sourceCommit -notmatch '^[a-f0-9]{40}$') { throw 'No se pudo determinar SourceCommit.' }
try {
    # Builds the Kiosk payload without publishing/importing/activating any Kiosk release.
    & (Join-Path $root 'build-installer.ps1') -Version $KioskVersion -ServerUrl $serverUrl -ServerApiKey $serverKey -SigningKeyId $env:KIOSK_UPDATE_SIGNING_KEY_ID -SigningPublicKey $env:KIOSK_UPDATE_SIGNING_PUBLIC_KEY
    Copy-Item -LiteralPath (Join-Path $output "Setup-KioskClinicaPC-$KioskVersion.exe") -Destination (Join-Path $resources 'kiosk.exe')
    & $dotnet publish (Join-Path $root 'src\Kiosk.SetupHelper\Kiosk.SetupHelper.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:Version=$Version -p:DebugType=None -p:DebugSymbols=false -o $worker -nologo @buildArgs
    if ($LASTEXITCODE -ne 0) { throw 'No se pudo publicar el trabajador WinGet.' }
    if (-not (Test-Path -LiteralPath (Join-Path $worker 'Microsoft.Management.Deployment.winmd'))) { throw 'Faltan los metadatos físicos WinGet.' }
    [IO.File]::WriteAllText((Join-Path $worker 'pack-worker.json'), (@{
        schemaVersion=1; catalogApiVersion=3; workerVersion=$Version; sourceCommit=$sourceCommit
    } | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
    # Windows PowerShell Compress-Archive writes backslash paths. Always emit canonical ZIP paths.
    Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::Open((Join-Path $resources 'worker.zip'), [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in (Get-ChildItem -LiteralPath $worker -Recurse -File)) {
            $relative = $file.FullName.Substring($worker.Length + 1).Replace('\', '/')
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $relative, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    } finally { $archive.Dispose() }
    $payload = [ordered]@{
        schemaVersion = 2; installerKind = 'equipment-wpf'; catalogApiVersion = 3; sourceCommit = $sourceCommit
        assistantVersion = $Version; workerVersion = $Version; kioskVersion = $KioskVersion
        workerSha256 = (Get-FileHash -LiteralPath (Join-Path $resources 'worker.zip') -Algorithm SHA256).Hash.ToLowerInvariant()
        kioskSha256 = (Get-FileHash -LiteralPath (Join-Path $resources 'kiosk.exe') -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    [IO.File]::WriteAllText((Join-Path $resources 'payload.json'), ($payload | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $resources 'config.json'), (@{serverUrl=$serverUrl; setupKey=$setupKey} | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
    & $dotnet publish (Join-Path $root 'src\Kiosk.EquipmentSetup\Kiosk.EquipmentSetup.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:Version=$Version "-p:EquipmentResourcesDir=$resources" -p:DebugType=None -p:DebugSymbols=false -o $frontend -nologo @buildArgs
    if ($LASTEXITCODE -ne 0) { throw 'No se pudo publicar el asistente WPF.' }
    Copy-Item -LiteralPath (Join-Path $frontend 'Setup-EquipoClinicaPC.exe') -Destination $setup
    # Smoke test the final single EXE, hashing resources without extracting/installing anything.
    $start = [Diagnostics.ProcessStartInfo]::new($setup)
    $start.UseShellExecute = $false; $start.CreateNoWindow = $true; $start.RedirectStandardOutput = $true
    $start.Arguments = '--diagnose-json'
    $process = [Diagnostics.Process]::Start($start)
    $read = $process.StandardOutput.ReadToEndAsync()
    if (-not $process.WaitForExit(120000)) { throw 'El diagnóstico del EXE empaquetado no terminó.' }
    $diagnostic = $read.GetAwaiter().GetResult() | ConvertFrom-Json
    if ($process.ExitCode -ne 0 -or -not $diagnostic.compatible -or -not $diagnostic.workerResourceVerified -or -not $diagnostic.kioskResourceVerified -or $diagnostic.assistantVersion -ne $Version) { throw 'Falló el smoke test del EXE empaquetado.' }
    $process.Dispose()
    $info = Get-Item -LiteralPath $setup
    $manifest = [ordered]@{
        schemaVersion = 2; installerKind = 'equipment-wpf'; catalogApiVersion = 3; sourceCommit = $sourceCommit
        version = $Version; assistantVersion = $Version; workerVersion = $Version; kioskVersion = $KioskVersion
        fileName = $info.Name; sizeBytes = $info.Length; sha256 = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant()
        serverUrl = $serverUrl; createdAtUtc = [DateTime]::UtcNow.ToString('o')
    }
    [IO.File]::WriteAllText($bundle, ($manifest | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
    if ($Publish) {
        $headers = @{ 'X-Release-Publish-Key' = $env:KIOSK_RELEASE_PUBLISH_KEY }
        $form = @{manifest=(Get-Item -LiteralPath $bundle); setup=(Get-Item -LiteralPath $setup)}
        Invoke-RestMethod -Method Post -Uri "$serverUrl/api/releases/setup" -Headers $headers -Form $form | Out-Null
    }
    Write-Host "Asistente WPF $Version generado y comprobado: $setup"
} finally {
    # Limited provisioning configuration is embedded only during the build.
    if (Test-Path -LiteralPath (Join-Path $resources 'config.json')) { Remove-Item -LiteralPath (Join-Path $resources 'config.json') -Force }
}
