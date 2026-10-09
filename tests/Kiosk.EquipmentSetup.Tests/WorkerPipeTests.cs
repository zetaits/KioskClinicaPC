using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Kiosk.EquipmentSetup;
using KioskClinicaPC.Equipment;
using Xunit;

namespace Kiosk.EquipmentSetup.Tests;

public sealed class WorkerPipeTests
{
    private static ProcessStartInfo FakeWorker(string name, string? result, int exitCode)
    {
        string script = "$pipe=[IO.Pipes.NamedPipeClientStream]::new('.', '" + name + "', [IO.Pipes.PipeDirection]::InOut); " +
            "$pipe.Connect(10000); $reader=[IO.StreamReader]::new($pipe); " +
            "$writer=[IO.StreamWriter]::new($pipe,[Text.UTF8Encoding]::new($false)); $writer.AutoFlush=$true; " +
            "$reader.ReadLine() | Out-Null; $writer.WriteLine('{\"kind\":\"phase\",\"message\":\"Installed\"}'); " +
            (result is null ? "" : "$writer.WriteLine('" + result + "'); ") +
            "$pipe.Dispose(); exit " + exitCode;
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"))
            { UseShellExecute = false, CreateNoWindow = true };
        foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) }) start.ArgumentList.Add(argument);
        return start;
    }
    [Fact]
    public async Task Result_from_a_real_child_process_is_verified_after_the_pipe_closes()
    {
        var events = new List<EquipmentEvent>();
        var result = await WorkerPipe.Start(new(false, true, 0, [], false), events.Add, default,
            name => FakeWorker(name, "{\"kind\":\"result\",\"message\":\"Verified\",\"exitCode\":0,\"kioskVerified\":true}", 0)).WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(0, result.ExitCode); Assert.True(result.KioskVerified);
        Assert.Contains(events, e => e.Kind == "phase");
    }
    [Theory]
    [InlineData(null, 0)]
    [InlineData("{\"kind\":\"result\",\"message\":\"Mismatch\",\"exitCode\":0}", 1)]
    [InlineData("{\"kind\":\"result\",\"message\":\"Missing code\"}", 0)]
    public async Task Closed_pipe_does_not_turn_missing_or_mismatched_results_into_success(string? result, int exitCode)
    {
        var error = await Assert.ThrowsAsync<IOException>(() => WorkerPipe.Start(new(false, true, 0, [], false), _ => { }, default,
            name => FakeWorker(name, result, exitCode)).WaitAsync(TimeSpan.FromSeconds(20)));
        Assert.Contains("sin un resultado verificable", EquipmentDiagnostics.Describe(error));
        Assert.Contains("salida del proceso " + exitCode, EquipmentDiagnostics.Describe(error));
    }
    [Fact]
    public async Task Verified_partial_result_remains_partial_after_closing_the_channel()
    {
        var result = await WorkerPipe.Start(new(true, true, 0, [], false), _ => { }, default,
            name => FakeWorker(name, "{\"kind\":\"result\",\"message\":\"Partial\",\"exitCode\":2,\"kioskVerified\":true}", 2)).WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(2, result.ExitCode);
    }
    [Fact]
    public async Task Successful_worker_cancels_its_listener_and_closes_without_losing_the_result()
    {
        string name = "ClinicaPC.Equipment." + Guid.NewGuid().ToString("N");
        using var server = WorkerPipe.CreateServer(name);
        Task connected = server.WaitForConnectionAsync();
        Task<int> worker = Task.Run(async () =>
        {
            using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
            await client.ConnectAsync(10000);
            return await WorkerPipe.WorkConnected(client, (_, report, _) =>
            {
                report(new("phase", "Kiosk instalado y verificado."));
                return Task.FromResult(new EquipmentEvent("result", "Verificado", ExitCode: 0, KioskVerified: true));
            });
        });
        await connected.WaitAsync(TimeSpan.FromSeconds(15));
        using var reader = new StreamReader(server, Encoding.UTF8, false, 65536, true);
        using var writer = new PipeEventWriter(server);
        await writer.WriteLineAsync(JsonSerializer.Serialize(new EquipmentRequest(false, true, 0, [], false), Payload.Json));
        var events = new List<EquipmentEvent>();
        async Task Read()
        {
            while (await reader.ReadLineAsync() is { } line)
                events.Add(JsonSerializer.Deserialize<EquipmentEvent>(line, Payload.Json)!);
        }
        await Task.WhenAll(worker, Read()).WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(0, await worker);
        Assert.Contains(events, e => e.Kind == "result" && e.ExitCode == 0 && e.KioskVerified);
    }
}
