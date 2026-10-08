namespace KioskClinicaPC.Core.Sync;

public sealed record PackVersionCandidate(string Version, string Channel);
public sealed class CatalogDriftException(string message) : IOException(message);

/// <summary>Uses WinGet's comparer, never lexical or System.Version ordering.</summary>
public static class PackVersionPolicy
{
    public static IReadOnlyList<PackVersionCandidate> Order(IEnumerable<PackVersionCandidate> candidates, string channel,
        Func<PackVersionCandidate, PackVersionCandidate, int?> compare)
    {
        var versions = candidates.Where(v => v.Channel == channel).ToList();
        if (versions.Any(v => string.IsNullOrWhiteSpace(v.Version) || v.Version.Equals("unknown", StringComparison.OrdinalIgnoreCase)) || versions.Distinct().Count() != versions.Count)
            throw new InvalidDataException("Versiones ambiguas en el canal predeterminado de WinGet.");
        versions.Sort((a, b) => -(compare(a, b) ?? throw new InvalidDataException("WinGet no puede ordenar las versiones del paquete.")));
        return versions;
    }
    public static async Task<PackVersionCandidate> Select(IEnumerable<PackVersionCandidate> candidates, string channel,
        Func<PackVersionCandidate, PackVersionCandidate, int?> compare,
        Func<PackVersionCandidate, Task<bool>> compatible, CancellationToken ct)
    {
        foreach (var candidate in Order(candidates, channel, compare))
        {
            ct.ThrowIfCancellationRequested();
            if (await compatible(candidate)) return candidate;
        }
        throw new InvalidDataException("No hay una versión compatible con instalación silenciosa para todo el equipo.");
    }
}

/// <summary>One initial refresh and at most one additional refresh for manifest/catalog drift. No installer retry.</summary>
public sealed class PackCatalogSession(Func<CancellationToken, Task> refresh)
{
    private bool _prepared, _extraRefreshUsed;
    private Exception? _failure;
    public async Task Prepare(CancellationToken ct)
    {
        if (_prepared) return;
        _prepared = true;
        try { await refresh(ct); }
        catch (Exception ex) when (ex is IOException or System.Runtime.InteropServices.COMException) { _failure = ex; }
    }
    public async Task Check(Func<Task> check, CancellationToken ct)
    {
        if (!_prepared) throw new InvalidOperationException("Actualiza el catálogo antes de resolver versiones.");
        if (_failure is not null) throw new IOException("No se pudo actualizar el origen WinGet. Reintenta con conexión al catálogo oficial.", _failure);
        try { await check(); }
        catch (CatalogDriftException) when (!_extraRefreshUsed)
        {
            _extraRefreshUsed = true;
            try { await refresh(ct); }
            catch (Exception ex) when (ex is IOException or System.Runtime.InteropServices.COMException) { _failure = ex; throw; }
            await check();
        }
    }
}
