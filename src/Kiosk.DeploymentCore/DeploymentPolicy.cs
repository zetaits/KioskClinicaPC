using System.Security.Cryptography;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Kiosk.Deployment;

public static partial class DeploymentPolicy
{
    public static void Require([DoesNotReturnIf(false)] bool condition, string message)
    { if (!condition) throw new InvalidDataException(message); }
    [GeneratedRegex("^[a-fA-F0-9]{64}$")] private static partial Regex HashPattern();
    [GeneratedRegex("^[a-f0-9]{32}$")] private static partial Regex IdPattern();
    public static bool IsHash(string? value) => value is not null && HashPattern().IsMatch(value);
    public static bool IsId(string? value) => value is not null && IdPattern().IsMatch(value);
    public static void Username(string value)
    {
        Require(!string.IsNullOrWhiteSpace(value) && value.Length <= 20 && value == value.Trim() &&
            !value.EndsWith('.') && !value.Any(c => char.IsControl(c) || "\"/\\[]:;|=,+*?<>@".Contains(c)) &&
            !new[] { "Administrator", "Administrador", "Guest", "Invitado", "DefaultAccount", "WDAGUtilityAccount", "SYSTEM" }
                .Contains(value, StringComparer.OrdinalIgnoreCase), "Nombre de usuario local no válido (máximo 20 caracteres).");
    }
    public static void Image(DeploymentImage image)
    {
        Require(IsHash(image.Id) && image.Verified && image.Architecture == "x64" && image.Build >= 26100 &&
            image.Language == "es-ES" && image.Editions.Count is > 0 and <= 32 &&
            image.Editions.All(e => e.Index > 0 && e.EditionId is "Core" or "Professional") &&
            image.Editions.Select(e => e.Index).Distinct().Count() == image.Editions.Count,
            "Se requiere una imagen verificada Windows 11 Home/Pro x64 24H2 o posterior en español.");
    }
    public static bool Candidate(DeploymentDisk disk) => disk.Internal && disk.Number >= 0 &&
        !string.IsNullOrWhiteSpace(disk.UniqueId) && disk.SizeBytes >= 64L * 1024 * 1024 * 1024 &&
        disk.BusType is "NVMe" or "SATA" or "SAS" or "ATA" or "RAID" or "SCSI";
    public static string Fingerprint(DeploymentHardware hardware) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(hardware with { Disks = hardware.Disks.OrderBy(d => d.Number).ToList() }))));
    public static bool Ambiguous(BootSession session, IEnumerable<BootSession> sessions) =>
        string.IsNullOrWhiteSpace(session.Hardware.Uuid) || !Guid.TryParse(session.Hardware.Uuid, out var uuid) || uuid == Guid.Empty ||
        uuid == Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff") || sessions.Any(s => s.Id != session.Id &&
            s.State is not (DeploymentState.Completed or DeploymentState.Cancelled) &&
            (s.Hardware.Uuid.Equals(session.Hardware.Uuid, StringComparison.OrdinalIgnoreCase) ||
             (!string.IsNullOrWhiteSpace(session.Hardware.Serial) && s.Hardware.Serial.Equals(session.Hardware.Serial, StringComparison.OrdinalIgnoreCase)) ||
             s.Hardware.Mac.Equals(session.Hardware.Mac, StringComparison.OrdinalIgnoreCase)));
    public static bool Active(DeploymentState state) => state is DeploymentState.Installing or DeploymentState.PostInstall;
    public static bool Terminal(DeploymentState state) => state is DeploymentState.Completed or DeploymentState.Attention or DeploymentState.Cancelled;
    public static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
}
