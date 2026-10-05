using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Kiosk.Deployment;

namespace Kiosk.DeploymentManager;

internal static class LocalServiceClient
{
    private static readonly JsonSerializerOptions Json = new(AtomicState.Json) { WriteIndented = false };
    internal static async Task<T> Send<T>(string operation, object payload)
    {
        using var pipe = new NamedPipeClientStream(".", DeploymentPipe.Name, PipeDirection.InOut,
            PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);
        using var timeout = new CancellationTokenSource(TimeSpan.FromHours(2));
        try { await pipe.ConnectAsync(5000, timeout.Token); }
        catch (TimeoutException) { throw new IOException("El servicio no responde. Comprueba que «ClinicaPCDeployment» está instalado e iniciado."); }
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 65536, true) { AutoFlush = true };
        using var reader = new StreamReader(pipe, Encoding.UTF8, false, 65536, true);
        await writer.WriteLineAsync(JsonSerializer.Serialize(new LocalDeploymentRequest(operation, JsonSerializer.SerializeToElement(payload, Json)), Json));
        var response = JsonSerializer.Deserialize<LocalDeploymentResponse>(await reader.ReadLineAsync(timeout.Token) ?? throw new IOException("El servicio cerró la operación."), Json)
            ?? throw new IOException("Respuesta incompatible del servicio.");
        if (!response.Success) throw new InvalidDataException(response.Error);
        return response.Data!.Value.Deserialize<T>(Json)!;
    }
    internal static Task<JsonElement> Send(string operation, object payload) => Send<JsonElement>(operation, payload);
    internal static Task<LocalStationView> Snapshot() => Send<LocalStationView>("snapshot", new { });
}
