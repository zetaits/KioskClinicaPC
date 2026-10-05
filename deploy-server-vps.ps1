param(
    [string]$HostName = 'vps-9c7061ff.vps.ovh.net',
    [string]$UserName = 'ubuntu',
    [switch]$PackageOnly,
    [switch]$CheckOnly
)

$ErrorActionPreference = 'Stop'
if ($HostName -notmatch '^[A-Za-z0-9.-]+$' -or $UserName -notmatch '^[A-Za-z0-9_-]+$') {
    throw 'Invalid SSH host or user.'
}
if ($PackageOnly -and $CheckOnly) { throw 'Use either -PackageOnly or -CheckOnly.' }

$publish = Join-Path $PSScriptRoot 'src\Kiosk.Server\bin\Release\publish-linux'
$artifacts = Join-Path $PSScriptRoot 'src\Kiosk.Server\bin\Release\vps-releases'
$remote = "$UserName@$HostName"
$helper = Join-Path $PSScriptRoot 'deploy\ubuntu\deploy-release.sh'
$preflight = Join-Path $PSScriptRoot 'deploy\ubuntu\preflight.py'

if (-not $PackageOnly) {
    # Stream the read-only script into remote Python. sudo reads its password from the SSH TTY.
    # The payload contains only base64 characters, so no nested SSH/shell quotes are needed.
    $payload = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes(
        [IO.File]::ReadAllText($preflight)))
    $command = "printf %s $payload | base64 -d | sudo python3 -"
    & ssh.exe -tt -o StrictHostKeyChecking=yes -o ConnectTimeout=10 -- $remote $command
    if ($LASTEXITCODE -ne 0) { throw "Remote preflight failed with exit code $LASTEXITCODE" }
    if ($CheckOnly) { return }
}

$commit = (& git -C $PSScriptRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $commit -notmatch '^[0-9a-f]{40}$') { throw 'Cannot identify the source commit.' }
$sourcePaths = @('src/Kiosk.Server', 'src/Kiosk.Shared', 'src/Kiosk.Client/Assets',
    'src/Kiosk.Client/Fonts', 'docs', 'deploy/ubuntu', 'build-server-linux.ps1',
    'deploy-server-vps.ps1', 'global.json')
$sourceDirty = [bool]@(& git -C $PSScriptRoot status --porcelain --untracked-files=all -- $sourcePaths)
if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect Git status.' }
if ($sourceDirty) { Write-Warning 'Server release inputs contain uncommitted changes; this is recorded in the release manifest.' }

& (Join-Path $PSScriptRoot 'build-server-linux.ps1')
if ($LASTEXITCODE -ne 0) { throw "Server build failed with exit code $LASTEXITCODE" }

$settingsPath = Join-Path $publish 'appsettings.json'
$settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
foreach ($name in @('DataDir', 'AssetsDir', 'InstallersDir', 'SetupDir', 'UpdatesDir')) {
    $directory = switch ($name) {
        'DataDir' { 'data' }
        'AssetsDir' { 'assets' }
        'InstallersDir' { 'installers' }
        'SetupDir' { 'setups' }
        'UpdatesDir' { 'updates' }
    }
    $settings.Kiosk.$name = "/var/lib/kiosk-server/$directory"
}
$utf8 = New-Object System.Text.UTF8Encoding($false)
[IO.File]::WriteAllText($settingsPath, ($settings | ConvertTo-Json -Depth 64), $utf8)

$unsafe = Get-ChildItem -LiteralPath $publish -Recurse -Force | Where-Object {
    ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -or
    ($_.PSIsContainer -and $_.Name -in @('data', 'assets', 'installers', 'setups', 'updates'))
}
if ($unsafe) { throw "Package contains runtime data or links: $($unsafe.FullName -join ', ')" }

New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
$dllHash = (Get-FileHash -LiteralPath (Join-Path $publish 'Kiosk.Server.dll') -Algorithm SHA256).Hash.ToLowerInvariant()
$releaseId = (Get-Date -Format 'yyyyMMddHHmmss') + '-' + $dllHash.Substring(0, 16)
$archive = Join-Path $artifacts "kiosk-server-$releaseId.tar.gz"
if (Test-Path -LiteralPath $archive) { throw "Archive already exists: $archive" }
$releaseInfo = [ordered]@{
    releaseId = $releaseId
    sourceCommit = $commit
    sourceDirty = $sourceDirty
    dllSha256 = $dllHash
    packagedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
}
[IO.File]::WriteAllText((Join-Path $publish 'release-info.json'), ($releaseInfo | ConvertTo-Json -Depth 4), $utf8)

& tar.exe -czf $archive -C $publish .
if ($LASTEXITCODE -ne 0) { throw "tar failed with exit code $LASTEXITCODE" }
$archiveHash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host "Release: $releaseId"
Write-Host "DLL SHA-256: $dllHash"
Write-Host "Package SHA-256: $archiveHash"
Get-ChildItem -LiteralPath $artifacts -Filter 'kiosk-server-*.tar.gz' -File |
    Where-Object FullName -ne $archive | Remove-Item -Force
if ($PackageOnly) {
    Write-Host "Package prepared without uploading: $archive"
    return
}

# Send all three files through one authenticated SCP connection. Keep the legacy protocol
# because this VPS previously closed SFTP sessions. Generated basenames match the remote helper.
$helperUpload = Join-Path $artifacts "kiosk-server-deploy-$releaseId.sh"
$preflightUpload = Join-Path $artifacts "kiosk-server-preflight-$releaseId.py"
try {
    Copy-Item -LiteralPath $helper -Destination $helperUpload
    Copy-Item -LiteralPath $preflight -Destination $preflightUpload
    Write-Host 'Uploading the package and both deployment helpers in one SSH connection.'
    & scp.exe -O -o StrictHostKeyChecking=yes -o ConnectTimeout=10 -- $archive $helperUpload $preflightUpload "${remote}:/home/ubuntu/"
    if ($LASTEXITCODE -ne 0) {
        throw "Deployment upload failed with exit code $LASTEXITCODE. Activation has not started; the running release is unchanged."
    }
}
finally {
    foreach ($uploadCopy in @($helperUpload, $preflightUpload)) {
        if (Test-Path -LiteralPath $uploadCopy -PathType Leaf) { Remove-Item -LiteralPath $uploadCopy -Force }
    }
}

# -tt lets ssh and sudo request passwords interactively. Arguments are locally validated.
$command = "sudo python3 /home/ubuntu/kiosk-server-preflight-$releaseId.py && sudo bash /home/ubuntu/kiosk-server-deploy-$releaseId.sh $releaseId $archiveHash"
& ssh.exe -tt -o StrictHostKeyChecking=yes -o ConnectTimeout=10 -- $remote $command
if ($LASTEXITCODE -ne 0) { throw "Remote deployment failed with exit code $LASTEXITCODE" }
Write-Host "Server release $releaseId passed its local readiness check."
