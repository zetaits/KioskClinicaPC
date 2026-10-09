using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using KioskClinicaPC.Equipment;

namespace Kiosk.EquipmentSetup;
internal static class WorkerPipe
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint pid);
    internal static Task<EquipmentEvent> Start(EquipmentRequest request, Action<EquipmentEvent> progress, CancellationToken ct) =>
        Start(request, progress, ct, name =>
        {
            // The unelevated UI remains the original user's process, including when UAC uses another account.
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
            start.ArgumentList.Add("--worker"); start.ArgumentList.Add(name);
            return start;
        });
    internal static async Task<EquipmentEvent> Start(EquipmentRequest request, Action<EquipmentEvent> progress, CancellationToken ct,
        Func<string, ProcessStartInfo> createWorker)
    {
        string name = "ClinicaPC.Equipment." + Guid.NewGuid().ToString("N");
        using var pipe = CreateServer(name);
        var start = createWorker(name);
        using var process = Process.Start(start) ?? throw EquipmentDiagnostics.IOError("No se pudo iniciar el trabajador elevado.");
        using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct); connectTimeout.CancelAfter(TimeSpan.FromMinutes(2));
        Task connected = pipe.WaitForConnectionAsync(connectTimeout.Token);
        Task exited = process.WaitForExitAsync();
        if (await Task.WhenAny(connected, exited) == exited) throw EquipmentDiagnostics.IOError("El trabajador se cerró antes de conectar.");
        await connected;
        if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out uint pid) || pid != process.Id) throw EquipmentDiagnostics.IOError("El trabajador conectado no corresponde a esta operación.");
        using var reader = new StreamReader(pipe, Encoding.UTF8, false, 65536, true);
        using var writer = new PipeEventWriter(pipe);
        await writer.WriteLineAsync(JsonSerializer.Serialize(request, Payload.Json));
        using var cancellation = ct.Register(() => { try { lock (writer) writer.WriteLine("cancel"); } catch (IOException) { } catch (ObjectDisposedException) { } });
        EquipmentEvent? result = null;
        while (await reader.ReadLineAsync() is { } line)
        {
            if (line.Length > 1024 * 1024) throw EquipmentDiagnostics.IOError("Evento del trabajador demasiado grande.");
            var value = JsonSerializer.Deserialize<EquipmentEvent>(line, Payload.Json) ?? throw EquipmentDiagnostics.IOError("Evento incompatible.");
            if (value.Kind is "result" or "review") result = value;
            else progress(value);
        }
        await exited;
        if (result == null || process.ExitCode != result.ExitCode)
            throw EquipmentDiagnostics.IOError($"El trabajador se cerró sin un resultado verificable; salida del proceso {process.ExitCode}; código del resultado {result?.ExitCode?.ToString() ?? "ausente"}. Puede quedar una instalación activa.");
        return result;
    }
    internal static NamedPipeServerStream CreateServer(string name)
    {
        var security = new PipeSecurity(); security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
        security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User!, PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), PipeAccessRights.ReadWrite, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.ReadWrite, AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 65536, 65536, security);
    }
    internal static async Task<int> Work(string name)
    {
        MachineState.RequireElevated();
        using var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);
        await pipe.ConnectAsync(30000);
        return await WorkConnected(pipe, Coordinator.Run);
    }
    internal static async Task<int> WorkConnected(PipeStream pipe,
        Func<EquipmentRequest, Action<EquipmentEvent>, CancellationToken, Task<EquipmentEvent>> run)
    {
        using var reader = new StreamReader(pipe, Encoding.UTF8, false, 65536, true);
        using var writer = new PipeEventWriter(pipe);
        var line = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(45)) ?? throw new IOException();
        if (line.Length > 1024 * 1024) return 64;
        var request = JsonSerializer.Deserialize<EquipmentRequest>(line, Payload.Json) ?? throw new IOException();
        EquipmentPolicy.ValidateRequest(request);
        using var cancel = new CancellationTokenSource();
        using var listenCancel = new CancellationTokenSource();
        var listener = Task.Run(async () =>
        {
            try
            {
                while (await reader.ReadLineAsync(listenCancel.Token) is { } command)
                    if (command == "cancel") cancel.Cancel(); else { cancel.Cancel(); break; }
                if (!listenCancel.IsCancellationRequested) cancel.Cancel();
            }
            catch (OperationCanceledException) { }
            catch (IOException) { cancel.Cancel(); }
        });
        void Send(EquipmentEvent value)
        {
            try { lock (writer) writer.WriteLine(JsonSerializer.Serialize(value, Payload.Json)); }
            catch (IOException) { cancel.Cancel(); }
        }
        try
        {
            var result = await run(request, Send, cancel.Token);
            Send(result);
            return result.ExitCode ?? 1;
        }
        finally { listenCancel.Cancel(); await listener; }
    }
}

internal sealed class PipeEventWriter : StreamWriter
{
    private readonly PipeStream _pipe;
    internal PipeEventWriter(PipeStream pipe) : base(pipe, new UTF8Encoding(false), 65536, true)
    {
        _pipe = pipe;
        AutoFlush = true; // Every protocol line is flushed while the channel is connected.
    }
    protected override void Dispose(bool disposing)
    {
        try { base.Dispose(disposing); }
        // EOF marks the duplex pipe broken. An empty cleanup flush must not replace the
        // validated result or the original exception. Writes during execution still throw.
        catch (IOException) when (disposing && !_pipe.IsConnected) { }
    }
}
