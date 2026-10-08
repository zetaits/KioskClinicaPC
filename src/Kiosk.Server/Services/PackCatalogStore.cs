using System.Text.Json;
using System.Text.RegularExpressions;
using KioskClinicaPC.Core.Sync;

namespace Kiosk.Server.Services;

/// <summary>Independent from uploaded remote installers and fleet records. All writes are atomic.</summary>
public sealed class PackCatalogStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly object _gate = new();
    private readonly string _packPath, _indexPath, _definitionPath;
    private PackCatalog _pack;
    private PackDefinition _definition;
    private WingetIndex _index;
    public PackCatalogStore(string root)
    {
        Directory.CreateDirectory(root);
        _packPath = Path.Combine(root, "pack-applications.json");
        _indexPath = Path.Combine(root, "winget-index.json");
        _definitionPath = Path.Combine(root, "pack-definition-v3.json");
        _pack = Read<PackCatalog>(_packPath) ?? new(0, []);
        _index = Read<WingetIndex>(_indexPath) ?? new(DateTime.MinValue, []);
        _definition = Read<PackDefinition>(_definitionPath) ?? Migrate();
        KioskClinicaPC.Equipment.EquipmentCatalogClient.Validate(_definition);
        RefreshLegacyProjection();
    }
    private PackDefinition Migrate()
    {
        var definition = PackDefinition.FromCatalog(_pack);
        KioskClinicaPC.Equipment.EquipmentCatalogClient.Validate(definition);
        if (File.Exists(_packPath) && !File.Exists(_packPath + ".before-v3.bak")) File.Copy(_packPath, _packPath + ".before-v3.bak", overwrite: false);
        Write(_definitionPath, definition);
        return definition;
    }
    // Corrupt existing data is deliberately not silently replaced.
    private static T? Read<T>(string path) => File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json)
        ?? throw new InvalidDataException($"Datos vacíos: {Path.GetFileName(path)}") : default;
    private static void Write<T>(string path, T value)
    {
        string tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(tmp, JsonSerializer.Serialize(value, Json)); File.Move(tmp, path, true); }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }
    public PackCatalog Snapshot() { lock (_gate) return new(_pack.Revision, [.. _pack.Applications.OrderBy(x => x.Order).ThenBy(x => x.DisplayName)]); }
    public PackDefinition Definition() { lock (_gate) return new(_definition.Revision, [.. _definition.Applications.OrderBy(x => x.Order).ThenBy(x => x.DisplayName)]); }
    public DateTime IndexUpdatedAtUtc { get { lock (_gate) return _index.GeneratedAtUtc; } }
    public IReadOnlyList<WingetIndexEntry> Search(string query)
    {
        query = query.Trim();
        lock (_gate) return _index.Applications.Where(x => query.Length >= 2 &&
            (x.Id.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase) || x.Name.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(x => x.Id.Equals(query, StringComparison.OrdinalIgnoreCase)).ThenBy(x => x.Name).Take(40).ToList();
    }
    public void Import(WingetIndex index)
    {
        if (index.GeneratedAtUtc <= DateTime.UtcNow.AddDays(-7) || index.GeneratedAtUtc > DateTime.UtcNow.AddMinutes(15) ||
            index.Applications.Count is < 1 or > 100000 || index.Applications.Any(x => !ValidId(x.Id) || string.IsNullOrWhiteSpace(x.Version) || string.IsNullOrWhiteSpace(x.Name)) ||
            index.Applications.Select(x => x.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != index.Applications.Count)
            throw new InvalidDataException("Índice WinGet incompleto o no válido.");
        lock (_gate)
        {
            if (index.GeneratedAtUtc <= _index.GeneratedAtUtc) throw new InvalidDataException("El índice recibido no es más reciente.");
            Write(_indexPath, index); _index = index;
            RefreshLegacyProjection();
        }
    }
    private static bool ValidId(string id) => Regex.IsMatch(id, "^[A-Za-z0-9][A-Za-z0-9._+-]{1,199}$");
    private void Commit(List<PackApplication> apps)
    {
        var definition = PackDefinition.FromCatalog(new(_definition.Revision + 1, apps));
        Write(_definitionPath, definition); _definition = definition;
        var next = new PackCatalog(_pack.Revision + 1, apps);
        Write(_packPath, next); _pack = next;
    }
    // Rebuildable v2 projection has its own revision. v3 is authoritative for membership/order.
    private void RefreshLegacyProjection()
    {
        var apps = _definition.Applications.Select(a =>
        {
            var entry = _index.Applications.Find(x => x.Id.Equals(a.WingetId, StringComparison.OrdinalIgnoreCase) && x.Eligible);
            var old = _pack.Applications.Find(x => x.Id == a.Id);
            return new PackApplication(a.Id, a.WingetId, a.DisplayName, entry?.Version ?? old?.PinnedVersion ?? "unknown", a.SelectedByDefault, a.Order);
        }).ToList();
        if (_pack.Applications.SequenceEqual(apps)) return;
        var next = new PackCatalog(_pack.Revision + 1, apps);
        Write(_packPath, next); _pack = next;
    }
    public void Add(string wingetId)
    {
        lock (_gate)
        {
            var entry = _index.Applications.SingleOrDefault(x => x.Id.Equals(wingetId, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidDataException("Aplicación no encontrada en el índice.");
            if (!entry.Eligible) throw new InvalidDataException(entry.Reason ?? "No admite instalación silenciosa machine-wide.");
            if (_definition.Applications.Any(x => x.WingetId.Equals(entry.Id, StringComparison.OrdinalIgnoreCase))) return;
            Commit([.. _pack.Applications, new(Guid.NewGuid().ToString("N"), entry.Id, entry.Name, entry.Version, true, _pack.Applications.Count)]);
        }
    }
    public void Remove(string id) { lock (_gate) Commit(_pack.Applications.Where(x => x.Id != id).ToList()); }
    public void Configure(string id, bool selected, int order)
    {
        if (order is < 0 or > 10000) throw new InvalidDataException("Orden fuera de rango.");
        lock (_gate) Commit(_pack.Applications.Select(x => x.Id == id ? x with { SelectedByDefault = selected, Order = order } : x).ToList());
    }
    public IReadOnlyList<string> UpdateVersions()
    {
        lock (_gate)
        {
            if (_index.GeneratedAtUtc < DateTime.UtcNow.AddDays(-7)) throw new InvalidDataException("El índice lleva más de siete días sin actualizarse. Renueva el índice antes de fijar versiones nuevas.");
            RefreshLegacyProjection();
            return _definition.Applications.Where(app => !_index.Applications.Any(entry =>
                entry.Id.Equals(app.WingetId, StringComparison.OrdinalIgnoreCase) && entry.Eligible)).Select(app => app.DisplayName).ToList();
        }
    }
}
