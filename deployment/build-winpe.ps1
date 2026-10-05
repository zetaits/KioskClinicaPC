param(
    [Parameter(Mandatory)][string]$InputManifest,
    [Parameter(Mandatory)][string]$Output,
    [string]$AdkRoot = 'C:\Program Files (x86)\Windows Kits\10\Assessment and Deployment Kit'
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$manifest = Get-Content -LiteralPath $InputManifest -Raw | ConvertFrom-Json
if ($manifest.adkVersion -ne '10.1.26100.9457' -or -not $manifest.ipxeVersion -or -not $manifest.wimbootVersion) { throw 'Fija las versiones oficiales del ADK, iPXE y wimboot antes de construir WinPE.' }
function Assert-Hash([string]$Path, [string]$Hash) {
    if ($Hash -notmatch '^[A-Fa-f0-9]{64}$' -or (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -ne $Hash) { throw "Integridad incorrecta: $([IO.Path]::GetFileName($Path))" }
}
$pe = Join-Path $AdkRoot 'Windows Preinstallation Environment\amd64'
$base = Join-Path $pe 'en-us\winpe.wim'
Assert-Hash $base $manifest.winpeBaseSha256
$outputRoot = [IO.Path]::GetFullPath($Output)
if (Test-Path -LiteralPath $outputRoot) { throw 'La carpeta de salida debe ser nueva; no se sobrescriben entornos existentes.' }
New-Item -ItemType Directory -Path $outputRoot | Out-Null
$boot = Join-Path $outputRoot 'boot.wim'
Copy-Item -LiteralPath $base -Destination $boot
Copy-Item -LiteralPath (Join-Path $pe 'media\Boot\BCD') -Destination (Join-Path $outputRoot 'BCD')
Copy-Item -LiteralPath (Join-Path $pe 'media\Boot\boot.sdi') -Destination (Join-Path $outputRoot 'boot.sdi')
foreach ($name in @('ipxe-shim.efi','ipxe.efi','shimx64.efi','wimboot')) {
    $entry = $manifest.files | Where-Object { $_.name -eq $name }
    if (@($entry).Count -ne 1 -or -not $entry.path) { throw "Falta el binario oficial fijado: $name" }
    Assert-Hash $entry.path $entry.sha256
    # iPXE includes a signed shim; wimboot is signed too. Hash pinning also applies to non-PE resources.
    if ((Get-AuthenticodeSignature -LiteralPath $entry.path).Status -ne 'Valid') { throw "Firma UEFI no válida: $name" }
    Copy-Item -LiteralPath $entry.path -Destination (Join-Path $outputRoot $name)
}
$mount = Join-Path $outputRoot 'mount'
New-Item -ItemType Directory -Path $mount | Out-Null
$mounted = $false
try {
    Mount-WindowsImage -ImagePath $boot -Index 1 -Path $mount | Out-Null
    $mounted = $true
    Add-WindowsPackage -Path $mount -PackagePath (Join-Path $pe 'WinPE_OCs\es-es\lp.cab') | Out-Null
    foreach ($component in @('WinPE-WMI','WinPE-NetFX','WinPE-Scripting','WinPE-PowerShell','WinPE-StorageWMI','WinPE-SecureStartup','WinPE-DismCmdlets','WinPE-Setup','WinPE-Setup-Client')) {
        Add-WindowsPackage -Path $mount -PackagePath (Join-Path $pe "WinPE_OCs\$component.cab") | Out-Null
        Add-WindowsPackage -Path $mount -PackagePath (Join-Path $pe "WinPE_OCs\en-us\${component}_en-us.cab") | Out-Null
        Add-WindowsPackage -Path $mount -PackagePath (Join-Path $pe "WinPE_OCs\es-es\${component}_es-es.cab") | Out-Null
    }
    if ($null -eq $manifest.winpeUpdates -or -not $manifest.servicingReviewedAtUtc) { throw 'Documenta la revisión de las actualizaciones oficiales aplicables y fija sus hashes, incluso si no se requiere ninguna.' }
    foreach ($update in $manifest.winpeUpdates) {
        Assert-Hash $update.path $update.sha256
        Add-WindowsPackage -Path $mount -PackagePath $update.path | Out-Null
    }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'winpe\Start-Deployment.ps1') -Destination (Join-Path $mount 'Windows\System32\Start-Deployment.ps1')
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'winpe\startnet.cmd') -Destination (Join-Path $mount 'Windows\System32\startnet.cmd')
    Dismount-WindowsImage -Path $mount -Save | Out-Null
    $mounted = $false
} finally { if ($mounted) { Dismount-WindowsImage -Path $mount -Discard | Out-Null } }
$hashes = [ordered]@{}
foreach ($name in @('ipxe-shim.efi','ipxe.efi','shimx64.efi','wimboot','BCD','boot.sdi','boot.wim')) { $hashes[$name] = (Get-FileHash -LiteralPath (Join-Path $outputRoot $name) -Algorithm SHA256).Hash }
[IO.File]::WriteAllText((Join-Path $outputRoot 'hashes.json'), ($hashes | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
Copy-Item -LiteralPath $InputManifest -Destination (Join-Path $outputRoot 'build-inputs.json')
Write-Host "WinPE construido y fijado por SHA-256: $outputRoot. Todavía requiere pruebas reales antes de publicar."
