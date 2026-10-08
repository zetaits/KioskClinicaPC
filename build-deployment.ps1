param(
    [string]$Version = '0.2.0',
    [Parameter(Mandatory)][string]$BootDirectory,
    [string]$Validation,
    [string]$WorkerZip,
    [string]$KioskInstaller,
    [string]$KioskVersion,
    [switch]$Publish
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'tools\resolve-dotnet.ps1')
$deploymentDotnet = Resolve-KioskDotnet
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Versión X.Y.Z requerida.' }
$boot = [IO.Path]::GetFullPath($BootDirectory)
$hashes = Get-Content -LiteralPath (Join-Path $boot 'hashes.json') -Raw | ConvertFrom-Json
foreach ($name in @('ipxe-shim.efi','ipxe.efi','shimx64.efi','wimboot','BCD','boot.sdi','boot.wim')) {
    if ($hashes.$name -notmatch '^[a-fA-F0-9]{64}$' -or (Get-FileHash -LiteralPath (Join-Path $boot $name)).Hash -ne $hashes.$name) { throw 'Entorno de arranque incompleto o alterado.' }
}
$sourceCommit = (& git -C $PSScriptRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $sourceCommit -notmatch '^[a-f0-9]{40}$') { throw 'No se pudo determinar el commit de origen.' }
if ($WorkerZip) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $workerArchive = [IO.Compression.ZipFile]::OpenRead([IO.Path]::GetFullPath($WorkerZip))
    try {
        $entry = $workerArchive.GetEntry('pack-worker.json')
        if (-not $entry -or $entry.Length -gt 4096) { throw 'Reconstruye worker.zip con el asistente 1.4.0 o posterior: falta la compatibilidad v3.' }
        $reader = [IO.StreamReader]::new($entry.Open())
        try { $metadata = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
        if ($metadata.schemaVersion -ne 1 -or $metadata.catalogApiVersion -ne 3 -or $metadata.workerVersion -notmatch '^\d+\.\d+\.\d+$' -or $metadata.sourceCommit -notmatch '^[a-fA-F0-9]{40}$') { throw 'El trabajador no admite la política de versiones actuales.' }
    } finally { $workerArchive.Dispose() }
}
$validated = $false
if ($Validation) {
    $evidence = Get-Content -LiteralPath $Validation -Raw | ConvertFrom-Json
    $checks = @('pxeRealNetworkSecureBoot','homeUefiSecureBoot','proUefiSecureBoot','threeSimultaneous','twoPhysicalModels','nonSelectedDiskIntact','vpsDisconnect','localNetworkDisconnect','stationRestart','windowsOnly','windowsPack','windowsPackKiosk','desktopAccountCleanupTracking','packUnavailableContinues','packResumeVerifiedOnly','packFrozenAfterPreflight','packNativeUncertainStops','legacyConfirmedJobUnchanged')
    $validated = $evidence.schemaVersion -eq 1 -and $evidence.sourceCommit -eq $sourceCommit -and $evidence.operator -and $evidence.evidence -and $evidence.validatedAtUtc
    foreach ($check in $checks) { if ($evidence.checks.$check -ne $true) { $validated = $false } }
    foreach ($name in $hashes.PSObject.Properties.Name) { if ($evidence.bootHashes.$name -ne $hashes.$name) { $validated = $false } }
    if (-not $WorkerZip -or -not $KioskInstaller -or $evidence.payloadHashes.'worker.zip' -ne (Get-FileHash -LiteralPath $WorkerZip).Hash -or $evidence.payloadHashes.'kiosk.exe' -ne (Get-FileHash -LiteralPath $KioskInstaller).Hash) { $validated = $false }
}
if ($Publish -and (-not $validated -or -not $env:KIOSK_RELEASE_PUBLISH_KEY)) { throw 'Publicación bloqueada: se requieren todas las pruebas reales del commit y entorno de arranque exactos, y la credencial de publicación.' }
if ($Publish -and (& git -C $PSScriptRoot status --porcelain)) { throw 'Publica desde un checkout limpio del commit validado.' }
$build = Join-Path $PSScriptRoot "deployment-build\$Version"
if (Test-Path -LiteralPath $build) { throw 'Esta carpeta de versión ya existe. Usa una versión nueva o revisa manualmente el build anterior.' }
New-Item -ItemType Directory -Path $build | Out-Null
foreach ($project in @('Manager','Service','PostInstall')) {
    $destination = if ($project -eq 'PostInstall') { Join-Path $build 'service\postinstall' } else { Join-Path $build $project.ToLowerInvariant() }
    & $deploymentDotnet publish (Join-Path $PSScriptRoot "src\Kiosk.Deployment$project\Kiosk.Deployment$project.csproj") -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:Version=$Version -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o $destination -nologo
    if ($LASTEXITCODE -ne 0) { throw "No se pudo construir $project." }
}
Copy-Item -LiteralPath $boot -Destination (Join-Path $build 'service\boot') -Recurse
$post = Join-Path $build 'service\postinstall'
$postHashes = [ordered]@{'Kiosk.DeploymentPostInstall.exe'=(Get-FileHash -LiteralPath (Join-Path $post 'Kiosk.DeploymentPostInstall.exe')).Hash}
if ($WorkerZip) { Copy-Item -LiteralPath $WorkerZip -Destination (Join-Path $post 'worker.zip'); $postHashes['worker.zip'] = (Get-FileHash -LiteralPath $WorkerZip).Hash }
if ($KioskInstaller) {
    if ($KioskVersion -notmatch '^\d+\.\d+\.\d+$') { throw 'Fija la versión del instalador Kiosk incluido.' }
    Copy-Item -LiteralPath $KioskInstaller -Destination (Join-Path $post 'kiosk.exe'); $postHashes['kiosk.exe'] = (Get-FileHash -LiteralPath $KioskInstaller).Hash
    [IO.File]::WriteAllText((Join-Path $post 'kiosk-version.json'), ($KioskVersion | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
}
[IO.File]::WriteAllText((Join-Path $post 'hashes.json'), ($postHashes | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
$compiler = Get-Command ISCC.exe -ErrorAction SilentlyContinue
$iscc = if ($compiler) { $compiler.Source } else { 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe' }
& $iscc "/DMyAppVersion=$Version" "/DDeploymentPublishDir=$build" (Join-Path $PSScriptRoot 'installer\Deployment.iss')
if ($LASTEXITCODE -ne 0) { throw 'No se pudo empaquetar el instalador de la estación.' }
$package = Join-Path $PSScriptRoot "installer\Output\Setup-InstalacionRedClinicaPC-$Version.exe"
$info = Get-Item -LiteralPath $package
$manifest = [ordered]@{schemaVersion=1;protocolVersion=1;installerKind='deployment-wpf';version=$Version;fileName=$info.Name;sizeBytes=$info.Length;
    sha256=(Get-FileHash -LiteralPath $package).Hash;sourceCommit=$sourceCommit;adkVersion='10.1.26100.9457';bootHashes=$hashes;realValidationPassed=[bool]$validated}
$manifestPath = $package + '.json'
[IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))
if ($Publish) {
    if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'La publicación requiere PowerShell 7.' }
    Invoke-RestMethod -Uri 'https://panel.clinicapc.es/api/releases/deployment' -Method Post -Headers @{'X-Release-Publish-Key'=$env:KIOSK_RELEASE_PUBLISH_KEY} -Form @{manifest=(Get-Item -LiteralPath $manifestPath);setup=$info} | Out-Null
}
Write-Host "Instalador construido: $package. Validación real completa: $validated"
