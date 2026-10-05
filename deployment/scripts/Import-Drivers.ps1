$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$inputData = [Console]::ReadLine() | ConvertFrom-Json
$source = [IO.Path]::GetFullPath($inputData.source)
$target = [IO.Path]::GetFullPath($inputData.target)
if (-not (Test-Path -LiteralPath $source -PathType Container)) { throw 'Carpeta de controladores requerida.' }
$files = @(Get-ChildItem -LiteralPath $source -Recurse -File)
if ($files.Count -gt 10000 -or ($files | Where-Object { $_.Extension -in @('.exe','.msi','.cmd','.bat','.ps1') -or ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) })) { throw 'Solo se admiten paquetes de controlador INF y sus recursos.' }
$infs = @($files | Where-Object { $_.Extension -eq '.inf' })
if ($infs.Count -eq 0) { throw 'La carpeta no contiene controladores INF.' }
foreach ($inf in $infs) {
    $text = Get-Content -LiteralPath $inf.FullName
    $catalogs = @($text | Where-Object { $_ -match '^\s*CatalogFile(?:\.[^=]+)?\s*=' })
    if ($catalogs.Count -eq 0) { throw 'INF sin catálogo firmado.' }
    foreach ($line in $catalogs) {
        $catalogName = (($line -split '=',2)[1] -split ';',2)[0].Trim().Trim('"')
        if ([IO.Path]::GetFileName($catalogName) -ne $catalogName) { throw 'Ruta de catálogo no válida.' }
        $cat = Join-Path $inf.DirectoryName $catalogName
        if ((Get-AuthenticodeSignature -LiteralPath $cat).Status -ne 'Valid') { throw 'Catálogo de controlador sin firma válida.' }
    }
}
New-Item -ItemType Directory -Path $target -Force | Out-Null
Get-ChildItem -LiteralPath $source -Force | Copy-Item -Destination $target -Recurse
$boot = $inputData.boot
Copy-Item -LiteralPath $inputData.baseBoot -Destination $boot
$mount = Join-Path $target 'mount'
New-Item -ItemType Directory -Path $mount | Out-Null
$mounted = $false
try {
    Mount-WindowsImage -ImagePath $boot -Index 1 -Path $mount | Out-Null
    $mounted = $true
    Add-WindowsDriver -Path $mount -Driver $target -Recurse | Out-Null
    Dismount-WindowsImage -Path $mount -Save | Out-Null
    $mounted = $false
} finally {
    if ($mounted) { Dismount-WindowsImage -Path $mount -Discard | Out-Null }
}
@{ready=$true;count=$infs.Count} | ConvertTo-Json -Compress
