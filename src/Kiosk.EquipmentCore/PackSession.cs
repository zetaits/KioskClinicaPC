using System.Diagnostics;
using System.IO;
using System.Text.Json;
using KioskClinicaPC.Core.Sync;
using KioskClinicaPC.Equipment;

namespace Kiosk.EquipmentSetup;
internal sealed class PackSession(string work, Action<string> diagnostic) : IEquipmentPackSession
{
    private Func<CancellationToken, Task<ProcessStartInfo>>? _startFactory;
    internal PackSession(string work, Action<string> diagnostic, Func<CancellationToken, Task<ProcessStartInfo>> startFactory) : this(work, diagnostic) => _startFactory = startFactory;
    private Process? _process;
    private Task? _reader, _errors;
    private readonly TaskCompletionSource<bool> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Action<EquipmentEvent>? _progress;
    public bool RebootRequired { get; private set; }
    public PackRun? LastRun { get; private set; }
    public async Task<bool> Preflight(PackCatalog snapshot, bool resume, Action<EquipmentEvent> progress, CancellationToken ct, bool allowPartial = false)
    {
        _progress = progress;
        ProcessStartInfo start;
        if (_startFactory != null) start = await _startFactory(ct);
        else
        {
            string exe = await Payload.ExtractWorker(work, progress, ct);
            start = new ProcessStartInfo(exe) { WorkingDirectory = Path.GetDirectoryName(exe)! };
            start.ArgumentList.Add("equipment-worker");
        }
        start.UseShellExecute = false; start.CreateNoWindow = true;
        start.RedirectStandardInput = true; start.RedirectStandardOutput = true; start.RedirectStandardError = true;
        // Do not inherit provisioning/publication/signing credentials from a CI or administrative shell.
        foreach (string key in start.Environment.Keys.Where(k => k.StartsWith("KIOSK_", StringComparison.OrdinalIgnoreCase)).ToList()) start.Environment.Remove(key);
        _process = Process.Start(start) ?? throw new IOException("No se pudo iniciar el trabajador.");
        _reader = Read();
        _errors = Task.Run(async () => { while (await _process.StandardError.ReadLineAsync() is { } line) diagnostic(line); });
        await _process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new PackWorkerStart(snapshot, resume, allowPartial), Payload.Json));
        using var registration = ct.Register(Cancel);
        return await _ready.Task.WaitAsync(ct);
    }
    private async Task Read()
    {
        bool resultSeen = false;
        try
        {
            while (await _process!.StandardOutput.ReadLineAsync() is { } line)
            {
                if (line.Length > 1024 * 1024) throw new InvalidDataException();
                var value = JsonSerializer.Deserialize<EquipmentEvent>(line, Payload.Json) ?? throw new InvalidDataException();
                if (value.Run is not null) LastRun = value.Run;
                if (value.Kind == "result")
                {
                    RebootRequired = value.RebootRequired; _finished.TrySetResult(value.ExitCode == 0); resultSeen = true;
                    if (value.ExitCode != 0) _ready.TrySetException(new NativeStateException(value.Message)); else _ready.TrySetResult(false);
                    _progress?.Invoke(value with { Kind = "pack-result", ExitCode = null });
                }
                else { _progress?.Invoke(value); if (value.Kind == "preflight-ready") _ready.TrySetResult(true); if (value.Kind == "preflight-failed") _ready.TrySetResult(false); }
            }
            if (!resultSeen) throw new IOException("El trabajador se cerró sin resultado. Comprueba el estado antes de reintentar.");
        }
        catch (Exception ex) { diagnostic($"Worker protocol: {ex.GetType().Name}; {ex.HResult:X8}"); _ready.TrySetException(new IOException("El trabajador se cerró inesperadamente.")); _finished.TrySetException(new IOException("El trabajador se cerró inesperadamente.")); Cancel(); }
    }
    private void Cancel()
    {
        try { lock (this) _process?.StandardInput.WriteLine("cancel"); } catch (IOException) { } catch (InvalidOperationException) { }
    }
    public async Task<bool> Install(Action<EquipmentEvent> progress, CancellationToken ct)
    {
        _progress = progress;
        ct.ThrowIfCancellationRequested();
        await _process!.StandardInput.WriteLineAsync("install");
        using var registration = ct.Register(Cancel);
        bool complete = await _finished.Task;
        await _process.WaitForExitAsync();
        return complete && _process.ExitCode == 0;
    }
    public async ValueTask DisposeAsync()
    {
        if (_process == null) return;
        Cancel();
        // Never kill active installers, or remove their working files, even after cancellation/disconnection.
        await _process.WaitForExitAsync();
        if (_reader != null) await _reader;
        if (_errors != null) await _errors;
        _process.Dispose();
    }
}
