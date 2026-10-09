param(
    [string]$Version = '1.5.3',
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
$passwordSeed = $null
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
if ($Publish) {
    $ready = Invoke-WebRequest -Uri "$serverUrl/health/ready" -MaximumRedirection 0
    if (($ready.Content | ConvertFrom-Json).status -ne 'ok' -or $ready.Headers['X-Kiosk-Password-Provisioning'] -ne '1') { throw 'Despliega primero el servidor con aprovisionamiento de contraseña del panel.' }
    $passwordSeed = Invoke-RestMethod -Uri "$serverUrl/api/releases/setup/kiosk-password" -Headers @{ 'X-Release-Publish-Key' = $env:KIOSK_RELEASE_PUBLISH_KEY } -MaximumRedirection 0
}
if ($Publish -and -not $passwordSeed) { throw 'Falta el aprovisionamiento inicial de contraseña del panel.' }
if ($passwordSeed) {
    # Validate without echoing the verifier or parser errors into the build log.
    $validSeed = $false
    try {
        $parts = $passwordSeed.passwordHash.Split(':')
        $validSeed = $passwordSeed.schemaVersion -eq 1 -and $passwordSeed.passwordPolicyVersion -eq 1 -and $parts.Count -eq 2 -and
            ([Convert]::FromBase64String($parts[0])).Length -eq 16 -and ([Convert]::FromBase64String($parts[1])).Length -eq 32 -and
            [Convert]::ToBase64String([Convert]::FromBase64String($parts[0])) -ceq $parts[0] -and [Convert]::ToBase64String([Convert]::FromBase64String($parts[1])) -ceq $parts[1]
    } catch { }
    if (-not $validSeed -or [Version]$KioskVersion -lt [Version]'1.2.1') { throw 'La contraseña inicial requiere un aprovisionamiento válido y Kiosk 1.2.1 o posterior.' }
}
$build = Join-Path $root 'equipment-build'
$resources = Join-Path $build 'resources'
$worker = Join-Path $build 'worker'
$output = Join-Path $root 'installer\Output'
$bundle = Join-Path $output "Setup-EquipoClinicaPC-$Version.bundle.json"
$setups = @{ online = (Join-Path $output "Setup-EquipoClinicaPC-$Version.exe"); complete = (Join-Path $output "Setup-EquipoClinicaPC-$Version-Completo.exe") }
foreach ($path in @($bundle, $setups.online, $setups.complete)) {
    if (Test-Path -LiteralPath $path) { throw "Ya existe $path. No se sobrescribe una versión; retira explícitamente un build local ficticio antes de repetir." }
}
if ([IO.Path]::GetFullPath($build) -ne [IO.Path]::GetFullPath((Join-Path $root 'equipment-build'))) { throw 'Salida fuera del workspace.' }
if (Test-Path -LiteralPath $build) { Remove-Item -LiteralPath $build -Recurse -Force }
New-Item -ItemType Directory -Path $resources,$worker,$output -Force | Out-Null
$buildArgs = @('--disable-build-servers', '-m:1', '-p:UseSharedCompilation=false')
$sourceCommit = (& git -C $root rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $sourceCommit -notmatch '^[a-f0-9]{40}$') { throw 'No se pudo determinar SourceCommit.' }
function Write-Json($path, $value) { [IO.File]::WriteAllText($path, ($value | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false)) }
function Get-Hash($path) { (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Diagnose($path) {
    $start = [Diagnostics.ProcessStartInfo]::new($path)
    $start.UseShellExecute = $false; $start.CreateNoWindow = $true; $start.RedirectStandardOutput = $true
    $start.Arguments = '--diagnose-json'
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $process = [Diagnostics.Process]::Start($start)
    try {
        $read = $process.StandardOutput.ReadToEndAsync()
        if (-not $process.WaitForExit(120000)) { throw 'El diagnóstico empaquetado no terminó.' }
        $diagnostic = $read.GetAwaiter().GetResult() | ConvertFrom-Json
        if ($process.ExitCode -ne 0) { throw 'Falló el diagnóstico empaquetado.' }
        $timer.Stop()
        Write-Host "Diagnóstico de $([IO.Path]::GetFileName($path)): $($timer.ElapsedMilliseconds) ms"
        return $diagnostic
    } finally { $process.Dispose() }
}
try {
    # No -Publish: building these payloads never imports or activates a Kiosk release.
    & (Join-Path $root 'build-installer.ps1') -Version $KioskVersion -ServerUrl $serverUrl -ServerApiKey $serverKey -SigningKeyId $env:KIOSK_UPDATE_SIGNING_KEY_ID -SigningPublicKey $env:KIOSK_UPDATE_SIGNING_PUBLIC_KEY
    Copy-Item -LiteralPath (Join-Path $output "Setup-KioskClinicaPC-$KioskVersion.exe") -Destination (Join-Path $resources 'kiosk.exe')
    & $dotnet publish (Join-Path $root 'src\Kiosk.PackWorker\Kiosk.PackWorker.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:Version=$Version -p:DebugType=None -p:DebugSymbols=false -o $worker -nologo @buildArgs
    if ($LASTEXITCODE -ne 0) { throw 'No se pudo publicar el trabajador sin WPF.' }
    foreach ($file in @('KioskSetupHelper.exe','KioskSetupHelper.dll','KioskSetupHelper.runtimeconfig.json','Microsoft.Management.Deployment.winmd')) {
        if (-not (Test-Path -LiteralPath (Join-Path $worker $file))) { throw "Falta $file en el trabajador." }
    }
    if (Test-Path -LiteralPath (Join-Path $worker 'PresentationFramework.dll')) { throw 'El trabajador contiene WPF.' }
    $workerDiagnostic = Diagnose (Join-Path $worker 'KioskSetupHelper.exe')
    if ($workerDiagnostic.wpf -or $workerDiagnostic.catalogApiVersion -ne 3 -or $workerDiagnostic.workerVersion -ne $Version) { throw 'Trabajador incompatible.' }
    Write-Json (Join-Path $worker 'pack-worker.json') @{schemaVersion=1; catalogApiVersion=3; workerVersion=$Version; sourceCommit=$sourceCommit}
    Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::Open((Join-Path $resources 'worker.zip'), [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in (Get-ChildItem -LiteralPath $worker -Recurse -File)) {
            $relative = $file.FullName.Substring($worker.Length + 1).Replace('\', '/')
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $relative, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    } finally { $archive.Dispose() }
    $workerComponent = @{kind='worker'; version=$Version; sizeBytes=(Get-Item -LiteralPath (Join-Path $resources 'worker.zip')).Length; sha256=(Get-Hash (Join-Path $resources 'worker.zip'))}
    $kioskComponent = @{kind='kiosk'; version=$KioskVersion; sizeBytes=(Get-Item -LiteralPath (Join-Path $resources 'kiosk.exe')).Length; sha256=(Get-Hash (Join-Path $resources 'kiosk.exe'))}
    if ($workerComponent.sizeBytes -ge 66.6 * 1024 * 1024) { throw 'worker.zip debe ser menor de 66,6 MiB.' }
    $editions = @()
    foreach ($edition in @('online','complete')) {
        $editionResources = Join-Path $build "resources-$edition"
        $frontend = Join-Path $build "frontend-$edition"
        New-Item -ItemType Directory -Path $editionResources,$frontend -Force | Out-Null
        if ($edition -eq 'complete') {
            Copy-Item -LiteralPath (Join-Path $resources 'kiosk.exe'),(Join-Path $resources 'worker.zip') -Destination $editionResources
        }
        $payload = [ordered]@{
            schemaVersion=3; installerKind='equipment-wpf'; catalogApiVersion=3; componentProtocolVersion=1; edition=$edition; sourceCommit=$sourceCommit
            assistantVersion=$Version; workerVersion=$Version; kioskVersion=$KioskVersion
            workerSha256=$workerComponent.sha256; kioskSha256=$kioskComponent.sha256; worker=$workerComponent; kiosk=$kioskComponent
        }
        Write-Json (Join-Path $editionResources 'payload.json') $payload
        Write-Json (Join-Path $editionResources 'config.json') @{serverUrl=$serverUrl; setupKey=$setupKey}
        if ($passwordSeed) { Write-Json (Join-Path $editionResources 'kiosk-password.json') $passwordSeed }
        # Separate intermediate directories prevent one edition's embedded-resource inventory leaking into the other.
        & $dotnet publish (Join-Path $root 'src\Kiosk.EquipmentSetup\Kiosk.EquipmentSetup.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:Version=$Version "-p:EquipmentResourcesDir=$editionResources" -p:DebugType=None -p:DebugSymbols=false -o $frontend -nologo @buildArgs
        if ($LASTEXITCODE -ne 0) { throw "No se pudo publicar el asistente $edition." }
        Copy-Item -LiteralPath (Join-Path $frontend 'Setup-EquipoClinicaPC.exe') -Destination $setups[$edition]
        $diagnostic = Diagnose $setups[$edition]
        if (-not $diagnostic.compatible -or $diagnostic.edition -ne $edition -or $diagnostic.componentProtocolVersion -ne 1 -or $diagnostic.assistantVersion -ne $Version) { throw 'Diagnóstico de manifiesto incompatible.' }
        if ($passwordSeed -and -not $diagnostic.panelPasswordProvisioned) { throw 'El asistente no contiene el aprovisionamiento inicial de contraseña.' }
        if ($edition -eq 'online') {
            if (-not $diagnostic.binariesAbsent -or $diagnostic.workerResourceVerified -or $diagnostic.kioskResourceVerified) { throw 'Online contiene binarios o afirma verificarlos.' }
            if ((Get-Item -LiteralPath $setups[$edition]).Length -ge 100000000) { throw 'Online debe ser menor de 100 MB.' }
        } elseif (-not $diagnostic.workerResourceVerified -or -not $diagnostic.kioskResourceVerified -or $diagnostic.binariesAbsent) { throw 'Completo no supera integridad de recursos.' }
        $info = Get-Item -LiteralPath $setups[$edition]
        $editions += @{edition=$edition; fileName=$info.Name; sizeBytes=$info.Length; sha256=(Get-Hash $info.FullName)}
        Write-Host "$edition : $($info.Length) bytes"
    }
    if ($editions[1].sizeBytes -ge 366646608) { throw 'Completo debe ser menor que el asistente anterior (349,7 MiB).' }
    $manifest = [ordered]@{
        schemaVersion=3; installerKind='equipment-wpf'; catalogApiVersion=3; componentProtocolVersion=1; sourceCommit=$sourceCommit
        version=$Version; assistantVersion=$Version; workerVersion=$Version; kioskVersion=$KioskVersion
        serverUrl=$serverUrl; createdAtUtc=[DateTime]::UtcNow.ToString('o'); editions=$editions; components=@($workerComponent,$kioskComponent)
    }
    Write-Json $bundle $manifest
    Write-Host "worker.zip : $($workerComponent.sizeBytes) bytes"
    if ($Publish) {
        $ready = Invoke-WebRequest -Uri "$serverUrl/health/ready" -MaximumRedirection 0
        if (($ready.Content | ConvertFrom-Json).status -ne 'ok' -or $ready.Headers['X-Setup-Catalog-Version'] -ne '3' -or $ready.Headers['X-Setup-Component-Protocol'] -ne '1') { throw 'Despliega primero el servidor con catálogo 3 y protocolo de componentes 1.' }
        $headers = @{ 'X-Release-Publish-Key' = $env:KIOSK_RELEASE_PUBLISH_KEY }
        $form = @{manifest=(Get-Item -LiteralPath $bundle); online=(Get-Item -LiteralPath $setups.online); complete=(Get-Item -LiteralPath $setups.complete); worker=(Get-Item -LiteralPath (Join-Path $resources 'worker.zip')); kiosk=(Get-Item -LiteralPath (Join-Path $resources 'kiosk.exe'))}
        # Exact retries use the same manifest/files; no activation is performed here.
        for ($attempt = 0; $attempt -lt 3; $attempt++) {
            try { Invoke-RestMethod -Method Post -Uri "$serverUrl/api/releases/setup/v3" -Headers $headers -Form $form -MaximumRedirection 0 | Out-Null; break }
            catch {
                $status = if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { 0 }
                if ($attempt -eq 2 -or ($status -gt 0 -and $status -lt 500 -and $status -ne 408 -and $status -ne 429)) { throw }
                Start-Sleep -Seconds ($attempt + 1)
            }
        }
        Write-Host 'Candidata publicada. Prueba ambas ediciones en equipos piloto y activa desde el panel.'
    }
    Write-Host "Ambas ediciones $Version generadas y comprobadas: $output"
} finally {
    foreach ($edition in @('online','complete')) {
        foreach ($resourceName in @('config.json','kiosk-password.json')) {
            $config = Join-Path $build "resources-$edition\$resourceName"
            if (Test-Path -LiteralPath $config) { Remove-Item -LiteralPath $config -Force }
        }
    }
}
