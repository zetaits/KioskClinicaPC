using System.Text.Json;

namespace Kiosk.Deployment;

// Used ONLY over the ACL-protected local pipe. Never submit these records to panel endpoints.
public static class DeploymentPipe { public const string Name = "ClinicaPC.Deployment.v1"; }
public sealed record LocalDeploymentRequest(string Operation, JsonElement Payload);
public sealed record LocalDeploymentResponse(bool Success, JsonElement? Data, string? Error = null);
public sealed record LocalEnrollment(string Server, string Code, string Name);
public sealed record LocalNetwork(string AdapterId, string Address, string Mask, string Storage, int Capacity);
public sealed record LocalConfirmation(DeploymentConfirmation Confirmation, Dictionary<string, string> Passwords);
public sealed record LocalRecovery(string JobId, bool PhysicallyReviewed);
public sealed record EthernetAdapter(string Id, string Name, string Address, string Mask);
public sealed record NetworkDevice(string Address, string? Mac, DeploymentState State);
public sealed record LocalStationView(bool Enrolled, bool PanelConnected, bool Enabled, string? AdapterId, string? Storage,
    bool PasswordConfigured, DeploymentQueueStateView Queue, DeploymentConfiguration? Configuration, List<EthernetAdapter> Adapters,
    List<NetworkDevice> Devices);
public sealed record DeploymentQueueStateView(int Capacity, List<BootSession> Sessions, List<DeploymentImage> Images,
    List<DeploymentBatch> Batches, List<DeploymentJob> Jobs);
