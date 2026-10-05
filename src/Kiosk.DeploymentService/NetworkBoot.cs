using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Kiosk.Deployment;

namespace Kiosk.DeploymentService;

public sealed class NetworkBoot(StationState state) : IDisposable
{
    private readonly object _gate = new();
    private CancellationTokenSource? _stop;
    private List<UdpClient> _listeners = [];
    private readonly SemaphoreSlim _transfers = new(16, 16);
    private readonly Dictionary<string, NetworkDevice> _observed = [];
    public static readonly string[] Files = ["ipxe-shim.efi", "ipxe.efi", "shimx64.efi", "autoexec.ipxe", "wimboot", "BCD", "boot.sdi", "boot.wim"];
    public List<NetworkDevice> Devices { get { lock (_gate) return _observed.Values.ToList(); } }
    public string BootRoot => Path.Combine(AppContext.BaseDirectory, "boot");
    public string WindowsPePath()
    {
        var custom = AtomicState.Read<DriverBoot?>(Path.Combine(StationState.Root, "driver-boot.json"), () => null);
        if (custom is null) return Path.Combine(BootRoot, "boot.wim");
        DeploymentPolicy.Require(Path.GetFullPath(custom.Path).StartsWith(StationState.Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "Ruta de entorno personalizado no válida.");
        using var input = File.OpenRead(custom.Path);
        DeploymentPolicy.Require(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(input)) == custom.Hash, "El entorno WinPE con controladores cambió.");
        return custom.Path;
    }
    public async Task<List<NetworkDevice>> Scan(CancellationToken ct)
    {
        var (address, mask) = state.Network();
        uint a = BinaryPrimitives.ReadUInt32BigEndian(address.GetAddressBytes()), m = BinaryPrimitives.ReadUInt32BigEndian(mask.GetAddressBytes());
        uint count = ~m;
        DeploymentPolicy.Require(count is >= 3 and <= 1023, "Selecciona una subred de hasta 1024 direcciones para explorar.");
        uint network = a & m;
        using var capacity = new SemaphoreSlim(32, 32);
        await Task.WhenAll(Enumerable.Range(1, (int)count - 1).Select(async offset =>
        {
            await capacity.WaitAsync(ct);
            try
            {
                byte[] bytes = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(bytes, network + (uint)offset); var target = new IPAddress(bytes);
                if (target.Equals(address)) return;
                using var ping = new System.Net.NetworkInformation.Ping();
                var reply = await ping.SendPingAsync(target, TimeSpan.FromMilliseconds(500), cancellationToken: ct);
                if (reply.Status == System.Net.NetworkInformation.IPStatus.Success)
                    lock (_gate) _observed[target.ToString()] = new(target.ToString(), null, DeploymentState.Detected);
            }
            catch (System.Net.NetworkInformation.PingException) { }
            finally { capacity.Release(); }
        }));
        return Devices;
    }
    public void ValidateBundle()
    {
        var manifest = AtomicState.Read<Dictionary<string, string>>(Path.Combine(BootRoot, "hashes.json"), () => throw new InvalidDataException("Falta el entorno WinPE. Instala un paquete completo de la estación."));
        foreach (string name in Files.Where(n => n != "autoexec.ipxe"))
        {
            string path = Path.Combine(BootRoot, name);
            DeploymentPolicy.Require(File.Exists(path) && manifest.TryGetValue(name, out var hash) && DeploymentPolicy.IsHash(hash), "Falta un archivo de arranque verificado.");
            using var input = File.OpenRead(path);
            DeploymentPolicy.Require(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(input)).Equals(manifest[name], StringComparison.OrdinalIgnoreCase), "Integridad del entorno PXE incorrecta.");
        }
    }
    public async Task Activate(CancellationToken ct)
    {
        DeploymentPolicy.Require(!state.Settings.Enabled, "La red ya está activada.");
        DeploymentPolicy.Require(state.Settings.StationId is not null && state.Secrets.DefaultPassword.Length >= 8 && state.Queue.Snapshot().Images.Any(),
            "Vincula la estación, configura la contraseña e importa una imagen antes de activar PXE.");
        var (address, mask) = state.Network(); ValidateBundle(); WindowsPePath();
        var sockets = new List<UdpClient>();
        try
        {
            foreach (int port in new[] { 67, 69, 4011 })
            {
                var socket = new UdpClient(AddressFamily.InterNetwork);
                try { socket.Client.ExclusiveAddressUse = true; socket.Client.Bind(new IPEndPoint(address, port)); socket.EnableBroadcast = true; sockets.Add(socket); }
                catch { socket.Dispose(); throw; }
            }
            var a = address.GetAddressBytes(); var m = mask.GetAddressBytes();
            string subnet = new IPAddress(a.Select((b, i) => (byte)(b & m[i])).ToArray()) + "/" + m.Sum(b => System.Numerics.BitOperations.PopCount(b));
            string sharePassword = state.Secrets.SharePassword.Length > 0 ? state.Secrets.SharePassword : Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)) + "a!";
            state.SaveSecrets(state.Secrets with { SharePassword = sharePassword });
            await PowerShellRunner.Run<System.Text.Json.JsonElement>("Configure-Network.ps1", new { state.Settings.AdapterId, state.Settings.Address,
                state.Settings.Storage, Subnet = subnet, SharePassword = sharePassword }, ct);
            // Runtime script is a fixed template; panel data never becomes boot commands.
            string baseUrl = "http://" + address + ":8089/boot/";
            File.WriteAllText(Path.Combine(StationState.Root, "autoexec.ipxe"), "#!ipxe\n" +
                $"kernel {baseUrl}wimboot\ninitrd {baseUrl}BCD BCD\ninitrd {baseUrl}boot.sdi boot.sdi\ninitrd {baseUrl}boot.wim boot.wim\n" +
                $"initrd {baseUrl}station.json station.json\nboot\n");
            AtomicState.Write(Path.Combine(StationState.Root, "boot-station.json"), new { Server = "https://" + address + ":8449/",
                CertificateSha256 = state.CertificateSha256, BootstrapToken = state.Secrets.BootstrapToken });
            lock (_gate)
            {
                _stop = new CancellationTokenSource(); _listeners = sockets; state.Enable(true);
                _ = Proxy(sockets[0], false, _stop.Token); _ = Tftp(sockets[1], _stop.Token); _ = Proxy(sockets[2], true, _stop.Token);
            }
        }
        catch { foreach (var socket in sockets) socket.Dispose(); throw; }
    }
    public void Deactivate()
    {
        DeploymentPolicy.Require(!state.Queue.Snapshot().Jobs.Any(j => DeploymentPolicy.Active(j.State)), "Hay instalaciones activas. Espera antes de desactivar la estación.");
        Stop(); state.Enable(false);
    }
    private void Stop() { lock (_gate) { _stop?.Cancel(); foreach (var socket in _listeners) socket.Dispose(); _listeners = []; _stop?.Dispose(); _stop = null; } }
    private static Dictionary<byte, byte[]> Options(byte[] packet)
    {
        var options = new Dictionary<byte, byte[]>(); int index = 240;
        while (index < packet.Length)
        {
            byte code = packet[index++]; if (code == 255) break; if (code == 0) continue;
            if (index >= packet.Length) break; int size = packet[index++]; if (index + size > packet.Length) break;
            options[code] = packet.AsSpan(index, size).ToArray(); index += size;
        }
        return options;
    }
    private async Task Proxy(UdpClient socket, bool bootRequest, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var request = await socket.ReceiveAsync(ct); var packet = request.Buffer;
                if (packet.Length < 240 || packet.Length > 1500 || packet[0] != 1 || packet[1] != 1 || packet[2] != 6 ||
                    BinaryPrimitives.ReadUInt32BigEndian(packet.AsSpan(236)) != 0x63825363) continue;
                if (!request.RemoteEndPoint.Address.Equals(IPAddress.Any) && !state.InSubnet(request.RemoteEndPoint.Address)) continue;
                var options = Options(packet);
                if (!options.TryGetValue(60, out var vendor) || !Encoding.ASCII.GetString(vendor).StartsWith("PXEClient") ||
                    !options.TryGetValue(93, out var arch) || arch.Length < 2 || BinaryPrimitives.ReadUInt16BigEndian(arch) is not (7 or 9) ||
                    !options.TryGetValue(53, out var type) || type.Length != 1 || type[0] != (bootRequest ? 3 : 1)) continue;
                string mac = Convert.ToHexString(packet.AsSpan(28, 6));
                lock (_gate) { if (_observed.Count < 1000 || _observed.ContainsKey(mac)) _observed[mac] = new(request.RemoteEndPoint.Address.ToString(), mac, DeploymentState.Booting); }
                byte[] address = IPAddress.Parse(state.Settings.Address!).GetAddressBytes();
                using var response = new MemoryStream(); var header = new byte[240]; packet.AsSpan(0, 44).CopyTo(header);
                header[0] = 2; header[3] = 0; Array.Clear(header, 16, 4); address.CopyTo(header, 20); // yiaddr stays zero: never allocate an address.
                Encoding.ASCII.GetBytes("ipxe-shim.efi").CopyTo(header, 108); packet.AsSpan(236, 4).CopyTo(header.AsSpan(236)); response.Write(header);
                void Option(byte code, byte[] value) { response.WriteByte(code); response.WriteByte((byte)value.Length); response.Write(value); }
                Option(53, [(byte)(bootRequest ? 5 : 2)]); Option(54, address); Option(60, Encoding.ASCII.GetBytes("PXEClient"));
                Option(66, Encoding.ASCII.GetBytes(state.Settings.Address!)); Option(67, Encoding.ASCII.GetBytes("ipxe-shim.efi"));
                // PXE discovery control: use the supplied boot server; no multicast discovery.
                Option(43, [6, 1, 8, 255]); response.WriteByte(255);
                var endpoint = bootRequest ? request.RemoteEndPoint : new IPEndPoint(IPAddress.Broadcast, 68);
                await socket.SendAsync(response.ToArray(), endpoint, ct);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException) { }
        catch (SocketException) { state.Enable(false); }
    }
    private async Task Tftp(UdpClient listener, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var request = await listener.ReceiveAsync(ct);
                if (!state.InSubnet(request.RemoteEndPoint.Address) || request.Buffer.Length is < 8 or > 1024 || request.Buffer[0] != 0 || request.Buffer[1] != 1) continue;
                string[] fields = Encoding.ASCII.GetString(request.Buffer, 2, request.Buffer.Length - 2).Split('\0');
                string name = fields[0]; if (!Files.Contains(name, StringComparer.Ordinal) || fields.Length < 2 || !fields[1].Equals("octet", StringComparison.OrdinalIgnoreCase)) continue;
                // WinPE and wimboot are HTTP resources; TFTP only serves the small firmware chain.
                if (name is "boot.wim" or "wimboot" or "boot.sdi" or "BCD") continue;
                if (!_transfers.Wait(0)) continue;
                _ = Transfer(name, fields, request.RemoteEndPoint, ct);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException) { }
        catch (SocketException) { state.Enable(false); }
    }
    private async Task Transfer(string name, string[] fields, IPEndPoint client, CancellationToken ct)
    {
        try
        {
            using var socket = new UdpClient(new IPEndPoint(IPAddress.Parse(state.Settings.Address!), 0)); socket.Connect(client);
            string path = name == "autoexec.ipxe" ? Path.Combine(StationState.Root, name) : Path.Combine(BootRoot, name);
            using var input = File.OpenRead(path); int blockSize = 512; var options = new List<string>();
            for (int i = 2; i + 1 < fields.Length; i += 2)
            {
                if (fields[i] == "blksize" && int.TryParse(fields[i + 1], out int size)) { blockSize = Math.Clamp(size, 512, 1400); options.AddRange(["blksize", blockSize.ToString()]); }
                if (fields[i] == "tsize") options.AddRange(["tsize", input.Length.ToString()]);
            }
            if (options.Count > 0)
            {
                byte[] oack = [0, 6, .. Encoding.ASCII.GetBytes(string.Join('\0', options) + '\0')];
                if (!await SendAndAck(socket, oack, 0, ct)) return;
            }
            ushort block = 1; byte[] buffer = new byte[blockSize]; int read;
            do
            {
                read = await input.ReadAsync(buffer, ct); byte[] packet = new byte[read + 4]; packet[1] = 3;
                BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(2), block); buffer.AsSpan(0, read).CopyTo(packet.AsSpan(4));
                if (!await SendAndAck(socket, packet, block, ct)) return; block++;
            } while (read == blockSize);
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException) { }
        finally { _transfers.Release(); }
    }
    private static async Task<bool> SendAndAck(UdpClient socket, byte[] packet, ushort block, CancellationToken ct)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            await socket.SendAsync(packet, ct); using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(3));
            try { var ack = await socket.ReceiveAsync(timeout.Token); if (ack.Buffer.Length == 4 && ack.Buffer[0] == 0 && ack.Buffer[1] == 4 && BinaryPrimitives.ReadUInt16BigEndian(ack.Buffer.AsSpan(2)) == block) return true; }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
        }
        return false;
    }
    public void Dispose() => Stop();
}
