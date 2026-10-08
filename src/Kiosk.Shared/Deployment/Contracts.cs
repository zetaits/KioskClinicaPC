using System.Text.Json.Serialization;
using KioskClinicaPC.Core.Sync;

namespace Kiosk.Deployment;

// These v1 contracts deliberately carry no account passwords, execution paths or shell commands.
public static class DeploymentProtocol { public const int Version = 1; }
public enum DeploymentState { Detected, Booting, Ready, Queued, Installing, PostInstall, Completed, Attention, Cancelled }
public sealed record DeploymentDisk(int Number, string UniqueId, string Model, string Serial, long SizeBytes, string BusType, bool Internal);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DeploymentHardware(string Manufacturer, string Model, string Serial, string Uuid,
    string Mac, bool Uefi, bool Tpm2, long MemoryBytes, List<DeploymentDisk> Disks);
public sealed record BootSession(string Id, string DisplayId, DateTimeOffset SeenAtUtc, DeploymentHardware Hardware,
    DeploymentState State = DeploymentState.Ready, string? PendingUsername = null, long OptionsRevision = 0);
public sealed record WindowsEdition(int Index, string Name, string EditionId);
public sealed record DeploymentImage(string Id, string Name, string Architecture, int Build, string Language,
    List<WindowsEdition> Editions, bool Verified, DateTimeOffset ImportedAtUtc);
public sealed record DeploymentStation(string Id, string Name, DateTimeOffset? LastSeenUtc, bool Revoked,
    List<DeploymentImage> Images, List<BootSession> Sessions, int Capacity = 3);
public sealed record DeploymentProfile(string Id, long Revision, string Name, string? ImageId, int? EditionIndex,
    string Language, string Username, PackCatalog Applications, bool Kiosk, PackDefinition? ApplicationDefinition = null);
public sealed record DeploymentBatch(string Id, DateTimeOffset ConfirmedAtUtc, List<string> JobIds);
public sealed record DeploymentJob(string Id, string BatchId, string SessionId, string HardwareFingerprint,
    DeploymentDisk Disk, DeploymentProfile Profile, string Username, long OptionsRevision, DeploymentState State,
    DateTimeOffset CreatedAtUtc, bool DestructiveStarted = false, long LastSequence = 0, string Phase = "En cola",
    int? Percent = null, bool WindowsVerified = false, bool AccountVerified = false, bool ComponentsVerified = false,
    bool RebootRequired = false, bool PanelAccepted = false, string WindowsEditionId = "", int WindowsBuild = 0, string? DriverSha256 = null,
    PackRun? ApplicationResult = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DeploymentProgress(string JobId, long Sequence, DeploymentState State, string Phase, int? Percent,
    bool WindowsVerified = false, bool AccountVerified = false, bool ComponentsVerified = false, bool RebootRequired = false,
    PackRun? ApplicationResult = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DeploymentInventory(int ProtocolVersion, int Capacity, List<DeploymentImage> Images,
    List<BootSession> Sessions, List<DeploymentBatch> Batches, List<DeploymentJob> Jobs, int ComponentPolicyVersion = 1);
public sealed record DeploymentConfiguration(int ProtocolVersion, List<DeploymentProfile> Profiles, List<BootSession> PendingOptions,
    PackCatalog Catalog, PackDefinition? Definition = null, int ComponentPolicyVersion = 1);
public sealed record DeploymentPanelSnapshot(List<DeploymentStation> Stations, List<DeploymentProfile> Profiles,
    Dictionary<string, List<DeploymentBatch>> Batches, Dictionary<string, List<DeploymentJob>> Jobs);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EnrollmentRequest(int ProtocolVersion, string Code, string Name);
public sealed record EnrollmentCode(string Code, DateTimeOffset ExpiresAtUtc);
public sealed record EnrollmentResponse(int ProtocolVersion, string StationId, string Credential);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PendingUsernameRequest(string Username, long ExpectedRevision);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProfileRequest(string? Id, long ExpectedRevision, string Name, string? ImageId, int? EditionIndex,
    string Language, string Username, List<string> ApplicationIds, bool Kiosk);
public sealed record DeploymentSelection(string SessionId, string DiskId, string Username, long OptionsRevision);
public sealed record DeploymentConfirmation(string Id, string ProfileId, long ProfileRevision, List<DeploymentSelection> Targets);
public sealed record DeploymentRelease(int SchemaVersion, int ProtocolVersion, string InstallerKind, string Version,
    string FileName, long SizeBytes, string Sha256, string SourceCommit, string AdkVersion,
    Dictionary<string, string> BootHashes, bool RealValidationPassed);
