$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
# WinPE uses Windows PowerShell/NetFX optional components, never WPF or .NET 10.
Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
public sealed class ClinicaPCStation {
    private readonly string root, pin;
    public ClinicaPCStation(string root, string pin) { this.root = root; this.pin = pin; }
    private HttpWebRequest Request(string path, string method, string header, string token) {
        var uri = new Uri(new Uri(root), path);
        if (uri.Scheme != "https") throw new InvalidDataException("HTTPS required");
        var req = (HttpWebRequest)WebRequest.Create(uri); req.Method = method;
        req.AllowAutoRedirect = false; req.Timeout = 30000; req.ReadWriteTimeout = 30000;
        req.ServerCertificateValidationCallback = (sender, cert, chain, errors) => {
            if (cert == null) return false;
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(cert.GetRawCertData())).Replace("-", "").Equals(pin, StringComparison.OrdinalIgnoreCase);
        };
        req.Headers.Add(header, token); return req;
    }
    public string Json(string path, string method, string body, string header, string token) {
        var req = Request(path, method, header, token); req.ContentType = "application/json";
        if (method == "POST") { var bytes = Encoding.UTF8.GetBytes(body ?? ""); req.ContentLength = bytes.Length; using (var s = req.GetRequestStream()) s.Write(bytes,0,bytes.Length); }
        using(var response = (HttpWebResponse)req.GetResponse()) {
            if (response.StatusCode == HttpStatusCode.NoContent) return null;
            using (var r = new StreamReader(response.GetResponseStream())) { var result = r.ReadToEnd(); if (result.Length > 1048576) throw new InvalidDataException(); return result; }
        }
    }
    public void Download(string path, string file, string token, string hash) {
        var req = Request(path,"GET","X-Worker-Token",token);
        using (var response = req.GetResponse()) using(var input = response.GetResponseStream()) using(var output = File.Create(file)) input.CopyTo(output);
        using(var input = File.OpenRead(file)) using(var sha = SHA256.Create())
            if (!BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").Equals(hash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Resource integrity failed");
    }
}
'@
function Get-Inventory {
    wpeutil UpdateBootInfo | Out-Null
    $computer = Get-CimInstance Win32_ComputerSystem
    $product = Get-CimInstance Win32_ComputerSystemProduct
    $bios = Get-CimInstance Win32_BIOS
    $network = Get-CimInstance Win32_NetworkAdapterConfiguration | Where-Object { $_.IPEnabled -and $_.MACAddress } | Select-Object -First 1
    $tpm = Get-CimInstance -Namespace 'root\CIMV2\Security\MicrosoftTpm' -ClassName Win32_Tpm -ErrorAction SilentlyContinue
    $disks = @()
    foreach ($disk in (Get-Disk)) {
        $bus = $disk.BusType.ToString()
        $disks += @{number=[int]$disk.Number;uniqueId=[string]$disk.UniqueId;model=[string]$disk.FriendlyName;serial=[string]$disk.SerialNumber;sizeBytes=[long]$disk.Size;busType=$bus;internal=($bus -in @('NVMe','SATA','SAS','ATA','RAID','SCSI'))}
    }
    @{manufacturer=[string]$computer.Manufacturer;model=[string]$computer.Model;serial=[string]$bios.SerialNumber;uuid=[string]$product.UUID;mac=[string]$network.MACAddress;
      uefi=((Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control').PEFirmwareType -eq 2);tpm2=($null -ne $tpm -and $tpm.SpecVersion -match '^2\.0');memoryBytes=[long]$computer.TotalPhysicalMemory;disks=$disks}
}
$config = Get-Content -LiteralPath 'X:\Windows\System32\station.json' -Raw | ConvertFrom-Json
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$station = New-Object ClinicaPCStation($config.server, $config.certificateSha256)
$session = $null
$job = $null
$sequence = 0
function Send-Progress([int]$state, [string]$phase) {
    $nextSequence = $script:sequence + 1
    $event = @{jobId=$job.id;sequence=$nextSequence;state=$state;phase=$phase;percent=$null}
    $body = $event | ConvertTo-Json -Compress
    # Retry the same sequence. A transient local network failure cannot replay a disk erase.
    for ($attempt=0; $attempt -lt 12; $attempt++) {
        try { $station.Json("worker/$($session.id)/progress", 'POST', $body, 'X-Worker-Token', $session.token) | Out-Null; $script:sequence = $nextSequence; return }
        catch { Start-Sleep -Seconds 5 }
    }
    throw 'No se pudo registrar el progreso en la estación.'
}
try {
    $inventory = Get-Inventory
    $session = $station.Json('worker/enroll', 'POST', ($inventory | ConvertTo-Json -Depth 10 -Compress), 'X-Bootstrap-Token', $config.bootstrapToken) | ConvertFrom-Json
    Clear-Host
    Write-Host "CLÍNICAPC · Equipo $($session.displayId)" -ForegroundColor Cyan
    Write-Host "$($inventory.manufacturer) $($inventory.model) · Serie $($inventory.serial)"
    Write-Host 'Esperando selección y confirmación desde el PC del encargado. No se modificará ningún disco mientras espera.'
    while ($true) {
        try {
            $inventory = Get-Inventory
            $station.Json("worker/$($session.id)/inventory", 'POST', ($inventory | ConvertTo-Json -Depth 10 -Compress), 'X-Worker-Token', $session.token) | Out-Null
            $commandJson = $station.Json("worker/$($session.id)/claim", 'POST', '{}', 'X-Worker-Token', $session.token)
            if ($commandJson) { break }
            $status = $station.Json("worker/$($session.id)/status", 'GET', $null, 'X-Worker-Token', $session.token) | ConvertFrom-Json
            if ($status -and $status.destructiveStarted) {
                $job = $status; $sequence = [long]$status.lastSequence
                Send-Progress 7 'No se recibió la autorización completa. Revisión obligatoria; no se ejecutó Windows Setup.'
                throw 'La autorización fue consumida y no se repetirá.'
            }
        } catch {
            if ($job) { throw }
            Write-Host 'Esperando conexión con la estación…' -ForegroundColor Yellow
        }
        Start-Sleep -Seconds 10
    }
    $command = $commandJson | ConvertFrom-Json
    $job = $command.job
    $sequence = [long]$job.lastSequence
    $disk = Get-Disk -Number $job.disk.number
    if (-not $disk -or $disk.UniqueId -ne $job.disk.uniqueId -or [long]$disk.Size -ne [long]$job.disk.sizeBytes -or [string]$disk.SerialNumber -ne $job.disk.serial -or $disk.BusType.ToString() -ne $job.disk.busType -or $disk.BusType -eq 'USB') { throw 'El disco cambió. Instalación detenida antes de Windows Setup.' }
    if (-not $inventory.uefi -or -not $inventory.tpm2) { throw 'UEFI/TPM no disponible. Windows Setup no se ejecutará.' }
    $credential = New-Object Management.Automation.PSCredential($command.shareUser, (ConvertTo-SecureString $command.sharePassword -AsPlainText -Force))
    New-PSDrive -Name Z -PSProvider FileSystem -Root $command.share -Credential $credential -Persist -Scope Global | Out-Null
    if ((Get-AuthenticodeSignature -LiteralPath 'Z:\setup.exe').Status -ne 'Valid') { throw 'Firma de Windows Setup no válida.' }
    # Download every selected resource before Setup can erase a disk or the first reboot can occur.
    $staging = 'X:\ClinicaPCJob'
    New-Item -ItemType Directory -Path $staging -Force | Out-Null
    $resources = @{ 'Kiosk.DeploymentPostInstall.exe'=$command.postInstallSha256 }
    if ($command.workerSha256) { $resources['worker.zip'] = $command.workerSha256 }
    if ($command.kioskSha256) { $resources['kiosk.exe'] = $command.kioskSha256 }
    if ($command.driverSha256) { $resources['drivers.zip'] = $command.driverSha256 }
    foreach ($name in $resources.Keys) { $station.Download("worker/$($session.id)/resources/$name", (Join-Path $staging $name), $session.token, $resources[$name]) }
    $answer = 'X:\ClinicaPC-unattend.xml'
    [IO.File]::WriteAllText($answer, $command.unattend, [Text.UTF8Encoding]::new($false))
    $command.unattend = $null; $command.sharePassword = $null; $commandJson = $null; $credential = $null
    Send-Progress 4 'Windows Setup: instalación limpia en el disco confirmado'
    # Windows Setup keeps its hardware requirements. No bypass switches or registry changes.
    $setup = Start-Process -FilePath 'Z:\setup.exe' -ArgumentList @('/unattend:X:\ClinicaPC-unattend.xml','/NoReboot') -PassThru -Wait
    if ($setup.ExitCode -ne 0) { throw 'Windows Setup no terminó correctamente. Revisa el destino antes de reintentar.' }
    $installed = Get-Partition -DiskNumber $job.disk.number | Where-Object { $_.DriveLetter -and (Test-Path -LiteralPath "$($_.DriveLetter):\Windows\System32\config\SYSTEM") } | Select-Object -First 1
    if (-not $installed) { throw 'No se encontró Windows en el disco autorizado. No se reiniciará.' }
    if ($command.driverSha256) {
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $driverTarget = Join-Path $staging 'drivers'
        [IO.Compression.ZipFile]::ExtractToDirectory((Join-Path $staging 'drivers.zip'), $driverTarget)
        Add-WindowsDriver -Path "$($installed.DriveLetter):\" -Driver $driverTarget -Recurse | Out-Null
    }
    $destination = "$($installed.DriveLetter):\ProgramData\ClinicaPC\DeploymentJob"
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    & icacls.exe $destination '/inheritance:r' '/grant:r' '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'No se pudo proteger la preparación posterior.' }
    Get-ChildItem -LiteralPath $staging -File | Copy-Item -Destination $destination
    Send-Progress 5 'Windows preparado; esperando primer inicio de sesión'
    $job.lastSequence = $sequence
    $plan = @{schemaVersion=1;job=$job;server=$config.server;sessionId=$session.id;callbackToken=$session.token;certificateSha256=$config.certificateSha256;
              workerSha256=$command.workerSha256;kioskSha256=$command.kioskSha256;kioskVersion=$command.kioskVersion}
    [IO.File]::WriteAllText((Join-Path $destination 'plan.json'), ($plan | ConvertTo-Json -Depth 30), [Text.UTF8Encoding]::new($false))
    Remove-Item -LiteralPath $answer -Force
    Remove-PSDrive -Name Z
    Write-Host 'Windows preparado. Reiniciando para completar el escritorio…'
    wpeutil Reboot
} catch {
    if (Test-Path -LiteralPath 'X:\ClinicaPC-unattend.xml') { Remove-Item -LiteralPath 'X:\ClinicaPC-unattend.xml' -Force }
    if ($job) { try { Send-Progress 7 'Instalación detenida. Revisa el destino; no se repetirá el borrado.' } catch { } }
    Write-Host 'REQUIERE ATENCIÓN. Comprueba red, imagen y disco en el PC del encargado. No reinicies ni repitas la instalación sin revisar el destino.' -ForegroundColor Yellow
    Read-Host 'Pulsa Intro para dejar el equipo en espera' | Out-Null
}
