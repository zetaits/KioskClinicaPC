using System.Text.RegularExpressions;
namespace KioskClinicaPC.Equipment;

public sealed record SetupEdition(string Edition, string FileName, long SizeBytes, string Sha256);
public sealed record SetupRelease(int SchemaVersion, string InstallerKind, int CatalogApiVersion, int ComponentProtocolVersion,
    string Version, string AssistantVersion, string WorkerVersion, string KioskVersion, string SourceCommit,
    string ServerUrl, DateTime CreatedAtUtc, List<SetupEdition> Editions, List<SetupComponent> Components)
{
    public bool Compatible => SchemaVersion == 3 && InstallerKind == "equipment-wpf" && CatalogApiVersion == 3 && ComponentProtocolVersion == 1 &&
        Regex.IsMatch(Version ?? "", "^[0-9]+\\.[0-9]+\\.[0-9]+$") && System.Version.TryParse(Version, out _) && Version == AssistantVersion &&
        Regex.IsMatch(SourceCommit ?? "", "^[a-f0-9]{40}$") &&
        Uri.TryCreate(ServerUrl, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0 &&
        Editions is { Count: 2 } && Editions.All(e => e != null) && Editions.Select(e => e.Edition).Order().SequenceEqual(new[] { "complete", "online" }) &&
        Editions.All(e => e.FileName == $"Setup-EquipoClinicaPC-{Version}{(e.Edition == "complete" ? "-Completo" : "")}.exe" &&
            e.SizeBytes is > 0 and <= 512L * 1024 * 1024 && Regex.IsMatch(e.Sha256 ?? "", "^[a-f0-9]{64}$")) &&
        Components is { Count: 2 } && Components.All(c => c != null) && Components.Select(c => c.Kind).Order().SequenceEqual(new[] { "kiosk", "worker" }) &&
        Components.All(c => c.Compatible && c.Version == (c.Kind == "worker" ? WorkerVersion : KioskVersion)) &&
        Editions.Sum(e => e.SizeBytes) + Components.Sum(c => c.SizeBytes) <= 1024L * 1024 * 1024;
}
