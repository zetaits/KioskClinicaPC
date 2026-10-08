using System.Text.Json;
using KioskClinicaPC.Core.Sync;
namespace Kiosk.SetupHelper;

internal static class PackState
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    internal static readonly string StateRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ClinicaPC", "Setup");
    internal static PackRun? ReadPending() => ReadLastRun() is { Complete: false, Items.Count: > 0 } run ? run : null;
    internal static PackRun? ReadLastRun()
    {
        string path = Path.Combine(StateRoot, "last-run.json");
        KioskClinicaPC.Equipment.SetupComponentCache.SafePath(path);
        return File.Exists(path) ? JsonSerializer.Deserialize<PackRun>(File.ReadAllText(path), Json) : null;
    }
    internal static void Save(PackRun run)
    {
        string path = Path.Combine(StateRoot, "last-run.json"), tmp = path + ".tmp";
        KioskClinicaPC.Equipment.SetupComponentCache.SafePath(path);
        KioskClinicaPC.Equipment.SetupComponentCache.SafePath(tmp);
        File.WriteAllText(tmp, JsonSerializer.Serialize(run, Json)); File.Move(tmp, path, true);
    }
}
