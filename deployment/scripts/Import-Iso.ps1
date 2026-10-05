$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$inputData = [Console]::ReadLine() | ConvertFrom-Json
$iso = [IO.Path]::GetFullPath($inputData.iso)
$target = [IO.Path]::GetFullPath($inputData.target)
if ([IO.Path]::GetExtension($iso) -ne '.iso' -or -not (Test-Path -LiteralPath $iso -PathType Leaf)) { throw 'ISO local requerida.' }
$mounted = $null
try {
    if ((Get-DiskImage -ImagePath $iso).Attached) { throw 'Desmonta la ISO antes de importarla.' }
    $mounted = Mount-DiskImage -ImagePath $iso -StorageType ISO -PassThru
    $volume = $mounted | Get-Volume
    $media = "$($volume.DriveLetter):\"
    $setup = Join-Path $media 'setup.exe'
    if ((Get-AuthenticodeSignature -LiteralPath $setup).Status -ne 'Valid' -or (Get-AuthenticodeSignature -LiteralPath $setup).SignerCertificate.Subject -notmatch 'Microsoft') { throw 'Windows Setup no tiene una firma Microsoft válida.' }
    $imagePath = Join-Path $media 'sources\install.wim'
    if (-not (Test-Path -LiteralPath $imagePath)) { $imagePath = Join-Path $media 'sources\install.esd' }
    $editions = @()
    $build = 0
    foreach ($summary in (Get-WindowsImage -ImagePath $imagePath)) {
        $image = Get-WindowsImage -ImagePath $imagePath -Index $summary.ImageIndex
        if ($image.EditionId -notin @('Core','Professional')) { continue }
        if ($image.Architecture -ne 9 -or $image.Version.Build -lt 26100 -or $image.DefaultLanguage -ne 'es-ES') { throw 'Se requiere Windows 11 Home/Pro x64 24H2+ español.' }
        $build = [Math]::Max($build, $image.Version.Build)
        $editions += @{index=[int]$image.ImageIndex; name=$image.ImageName; editionId=$image.EditionId}
    }
    if ($editions.Count -eq 0) { throw 'La ISO no contiene Home/Pro compatibles.' }
    $drive = [IO.DriveInfo]::new([IO.Path]::GetPathRoot($target))
    $total = (Get-ChildItem -LiteralPath $media -Recurse -File | Measure-Object Length -Sum).Sum
    if ($drive.AvailableFreeSpace -lt ($total + 10GB)) { throw 'No hay espacio suficiente para extraer la ISO.' }
    Get-ChildItem -LiteralPath $media -Force | Copy-Item -Destination $target -Recurse
    @{id=$inputData.hash; name=[IO.Path]::GetFileNameWithoutExtension($iso); architecture='x64'; build=$build; language='es-ES'; editions=$editions; verified=$true; importedAtUtc=[DateTimeOffset]::UtcNow.ToString('o')} | ConvertTo-Json -Depth 8 -Compress
} finally { if ($mounted) { Dismount-DiskImage -ImagePath $iso | Out-Null } }
