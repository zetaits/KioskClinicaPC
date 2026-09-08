using KioskClinicaPC.Core.Config;
using Newtonsoft.Json;

namespace Kiosk.Server.Services;

/// <summary>
/// Almacén de eventos temporales (events.json en el directorio de datos). CRUD desde el panel; el
/// <see cref="ContentResolver"/> lo consulta para saber qué evento está vigente al servir la config.
/// </summary>
public sealed class EventStore
{
    private readonly string _path;
    private readonly object _gate = new();
    public event Action? Changed;

    public EventStore(string dataDir)
    {
        Directory.CreateDirectory(dataDir);
        _path = Path.Combine(dataDir, "events.json");
        if (!File.Exists(_path)) WriteAll(new List<KioskEvent>());
    }

    public List<KioskEvent> All()
    {
        lock (_gate)
        {
            try { return JsonConvert.DeserializeObject<List<KioskEvent>>(File.ReadAllText(_path)) ?? new(); }
            catch { return new(); }
        }
    }

    public KioskEvent? Get(string id) => All().FirstOrDefault(e => e.Id == id);

    public bool ReferencesThemeAsset(string fileName) => All().Any(ev =>
        ThemeSlots(ev.Theme).Any(slot => slot.Mode == ThemeSlotMode.Library &&
            string.Equals(slot.AssetKey, fileName, StringComparison.OrdinalIgnoreCase)));

    /// <summary>Inserta o actualiza por Id.</summary>
    public void Save(KioskEvent ev)
    {
        lock (_gate)
        {
            var list = All();
            int i = list.FindIndex(e => e.Id == ev.Id);
            if (i >= 0) list[i] = ev; else list.Add(ev);
            WriteAll(list);
        }
        Changed?.Invoke();
    }

    public void Delete(string id)
    {
        lock (_gate)
        {
            var list = All();
            list.RemoveAll(e => e.Id == id);
            WriteAll(list);
        }
        Changed?.Invoke();
    }

    private void WriteAll(List<KioskEvent> list)
    {
        string tmp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(tmp, JsonConvert.SerializeObject(list, Formatting.Indented));
            File.Move(tmp, _path, overwrite: true);
        }
        finally { try { if (File.Exists(tmp)) File.Delete(tmp); } catch { } }
    }

    private static IEnumerable<ThemeSlotOverride> ThemeSlots(EventThemeSelection? theme)
    {
        if (theme?.Scenes == null) yield break;
        foreach (ThemeSceneOverride scene in new[]
                 { theme.Scenes.Attract, theme.Scenes.Scan, theme.Scenes.Overview, theme.Scenes.Detail })
        {
            if (scene?.Primary != null) yield return scene.Primary;
            if (scene?.Secondary != null) yield return scene.Secondary;
        }
    }
}
