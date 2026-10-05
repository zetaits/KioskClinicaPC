$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$inputData = [Console]::ReadLine() | ConvertFrom-Json
$adapter = Get-NetAdapter | Where-Object { $_.InterfaceGuid.ToString() -eq $inputData.adapterId.Trim('{}') }
if (-not $adapter -or $adapter.Status -ne 'Up') { throw 'Interfaz Ethernet no disponible.' }
$root = [IO.Path]::GetFullPath($inputData.storage)
if (-not (Test-Path -LiteralPath $root -PathType Container) -or $root.StartsWith('\\')) { throw 'Carpeta local requerida.' }
$shareRoot = Join-Path $root 'images'
if (-not (Test-Path -LiteralPath $shareRoot)) { New-Item -ItemType Directory -Path $shareRoot | Out-Null }
$user = 'ClinicaPCDeployment'
$secure = ConvertTo-SecureString $inputData.sharePassword -AsPlainText -Force
if (Get-LocalUser -Name $user -ErrorAction SilentlyContinue) { Set-LocalUser -Name $user -Password $secure }
else { New-LocalUser -Name $user -Password $secure -PasswordNeverExpires -UserMayNotChangePassword | Out-Null }
$localUser = Get-LocalUser -Name $user
$acl = Get-Acl -LiteralPath $shareRoot
$acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($localUser.SID, 'ReadAndExecute', 'ContainerInherit,ObjectInherit', 'None', 'Allow'))
Set-Acl -LiteralPath $shareRoot -AclObject $acl
# ISO children inherit this read-only technical account. The account cannot modify images or queue state.
foreach ($directory in (Get-ChildItem -LiteralPath $shareRoot -Directory)) {
    $childAcl = Get-Acl -LiteralPath $directory.FullName
    $childAcl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($localUser.SID, 'ReadAndExecute', 'ContainerInherit,ObjectInherit', 'None', 'Allow'))
    Set-Acl -LiteralPath $directory.FullName -AclObject $childAcl
}
$share = Get-SmbShare -Name 'ClinicaPCDeployment' -ErrorAction SilentlyContinue
if ($share -and $share.Path -ne $shareRoot) { throw 'Existe un recurso ClínicaPC con otra carpeta. Revisa la configuración.' }
if (-not $share) { New-SmbShare -Name 'ClinicaPCDeployment' -Path $shareRoot -ReadAccess "$env:COMPUTERNAME\$user" -CachingMode None -EncryptData $true | Out-Null }
$ports = @(@{name='PXE';protocol='UDP';ports='67,69,4011'}, @{name='Contenido';protocol='TCP';ports='445,8089,8449'})
foreach ($rule in $ports) {
    $name = "ClinicaPCDeployment-$($rule.name)"
    Get-NetFirewallRule -Name $name -ErrorAction SilentlyContinue | Remove-NetFirewallRule
    New-NetFirewallRule -Name $name -DisplayName $name -Direction Inbound -Action Allow -Protocol $rule.protocol -LocalPort $rule.ports -InterfaceAlias $adapter.Name -LocalAddress $inputData.address -RemoteAddress $inputData.subnet -Profile Any | Out-Null
}
@{ready=$true} | ConvertTo-Json -Compress
