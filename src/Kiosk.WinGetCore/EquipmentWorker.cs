using System.IO;
using System.Text.Json;
using KioskClinicaPC.Core.Sync;
using KioskClinicaPC.Equipment;

namespace Kiosk.SetupHelper;

/// <summary>Private duplex protocol; the coordinator supplies a previously authorized snapshot without credentials.</summary>
internal static class EquipmentWorker
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly object OutputGate = new();
    private static void Emit(EquipmentEvent value)
    {
        lock (OutputGate) Console.WriteLine(JsonSerializer.Serialize(value, Json));
    }
    public static async Task<int> Run()
    {
        using var cancel = new CancellationTokenSource();
        var proceed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            LocalState.Prepare();
            await using var lease = SetupLease.Pack(PackState.StateRoot);
            string line = await Console.In.ReadLineAsync() ?? throw new InvalidDataException();
            if (line.Length > 1024 * 1024) throw new InvalidDataException();
            var start = JsonSerializer.Deserialize<PackWorkerStart>(line, Json) ?? throw new InvalidDataException();
            EquipmentCatalogClient.Validate(start.Snapshot);
            if (start.Snapshot.Applications.Count == 0) throw new InvalidDataException();
            var run = PackResumePolicy.Create(start.Snapshot, PackState.ReadLastRun(), start.Resume);
            string logs = Path.Combine(PackState.StateRoot, "logs", "pack-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
            Directory.CreateDirectory(logs);
            _ = Task.Run(async () =>
            {
                try
                {
                    while (await Console.In.ReadLineAsync() is { } command)
                    {
                        if (command == "cancel") { cancel.Cancel(); proceed.TrySetResult(false); }
                        else if (command == "install") proceed.TrySetResult(true);
                        else { cancel.Cancel(); proceed.TrySetResult(false); }
                    }
                }
                catch (IOException) { }
                finally { cancel.Cancel(); proceed.TrySetResult(false); }
            });
            var engine = await Bootstrap.CreateEngine(message => Emit(new("phase", message)), cancel.Token);
            if (start.Snapshot.Definition is not null)
            {
                Emit(new("phase", "Actualizando el catÃ¡logo oficial WinGet y resolviendo versiones compatiblesâ€¦"));
                await engine.PrepareLatest(cancel.Token);
            }
            bool ready = false;
            bool complete = await PackExecution.Run(run, engine, logs,
                changed => Emit(new("applications", "Estado de las aplicaciones", changed)), PackState.Save, false, cancel.Token,
                async () => { ready = true; Emit(new("preflight-ready", run.Items.Any(x => x.State == PackItemState.Failed)
                    ? "Se instalarÃ¡n las aplicaciones disponibles. Las que fallaron quedan pendientes." : "Todas las aplicaciones comprobadas.", run)); return await proceed.Task; }, start.AllowPartial);
            if (!ready) Emit(new("preflight-failed", "Hay aplicaciones que no superan la comprobaciÃ³n previa.", run));
            Emit(new("result", run.Summary, run, complete ? 0 : 2, RebootRequired: run.RebootRequired));
            return complete ? 0 : 2;
        }
        catch (Exception ex)
        {
            // Diagnostics deliberately exclude exception text/stack and request data.
            Console.Error.WriteLine($"Pack worker: {ex.GetType().Name}; HRESULT {ex.HResult:X8}");
            Emit(new("result", ex is NativeStateException ? ex.Message : ex is IOException ? "Otra instalaciÃ³n puede estar activa. Revisa los diagnÃ³sticos." : "No se pudo completar el trabajador WinGet.", ExitCode: ex is OperationCanceledException ? 2 : 1));
            return ex is OperationCanceledException ? 2 : 1;
        }
    }
}
