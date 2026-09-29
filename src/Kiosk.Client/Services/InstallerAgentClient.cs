using System;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using KioskClinicaPC.Core.Sync;

namespace KioskClinicaPC.Services
{
    internal static class InstallerAgentClient
    {
        private const string PipeName = "KioskClinicaPC.InstallerAgent.v1";
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        public static Task<InstallerAgentResponse> HealthAsync(string? serverUrl) =>
            SendAsync(new InstallerAgentRequest { Operation = "health", ServerUrl = serverUrl });

        public static Task<InstallerAgentResponse> InstallAsync(string? serverUrl, string? apiKey, string deviceId, string jobId, string token) =>
            SendAsync(new InstallerAgentRequest
            {
                Operation = "install", ServerUrl = serverUrl, ApiKey = apiKey,
                DeviceId = deviceId, JobId = jobId, Token = token
            });

        public static Task<InstallerAgentResponse> StageKioskUpdateAsync(string? serverUrl, string? apiKey,
            string deviceId, KioskUpdateAssignment assignment) => SendAsync(new InstallerAgentRequest
            {
                Operation = "stage-kiosk-update", ServerUrl = serverUrl, ApiKey = apiKey,
                DeviceId = deviceId, UpdateAssignment = assignment
            });

        public static Task<InstallerAgentResponse> UninstallKioskAsync(string userDataDirectory,
            string? serverUrl = null, string? apiKey = null, string? deviceId = null,
            string? jobId = null, string? token = null) =>
            SendAsync(new InstallerAgentRequest
            {
                Operation = "uninstall-kiosk", KioskProcessId = Environment.ProcessId,
                UserDataDirectory = userDataDirectory, ServerUrl = serverUrl, ApiKey = apiKey,
                DeviceId = deviceId, JobId = jobId, Token = token
            });

        private static async Task<InstallerAgentResponse> SendAsync(InstallerAgentRequest request)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                await pipe.ConnectAsync(timeout.Token);
                using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
                using var reader = new StreamReader(pipe, leaveOpen: true);
                await writer.WriteLineAsync(JsonSerializer.Serialize(request, Json));
                string? line = await reader.ReadLineAsync(timeout.Token);
                return JsonSerializer.Deserialize<InstallerAgentResponse>(line ?? "", Json) ?? new() { Error = "Respuesta vacía del agente." };
            }
            catch (Exception ex)
            {
                return new InstallerAgentResponse { Accepted = false, Error = "El agente de instalación no está disponible: " + ex.Message };
            }
        }
    }
}
