using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using KioskClinicaPC.Core.Sync;
using YamlDotNet.Core;

namespace Kiosk.SetupHelper;

/// <summary>Isolates package data failures, never catalogue discovery/COM activation failures.</summary>
internal static class CatalogExportReader
{
    private const int MaxAttempts = 3;
    internal static int FailureLimit(int packageCount) => Math.Min(100, packageCount / 100);

    internal static async Task<List<WingetIndexEntry>> ReadAsync(int packageCount,
        Func<int, WingetIndexEntry> readPackage, Action<string>? info = null,
        Func<TimeSpan, Task>? delay = null)
    {
        if (packageCount < 1000) throw new InvalidDataException("Catalogue discovery is incomplete (minimum 1000 packages).");
        delay ??= duration => Task.Delay(duration);
        var entries = new List<WingetIndexEntry>();
        int skipped = 0;
        int failureLimit = FailureLimit(packageCount);
        info?.Invoke($"Package read failure budget: {failureLimit}/{packageCount} (at most 1%, capped at 100).");
        for (int index = 0; index < packageCount; index++)
        {
            for (int attempt = 1; ; attempt++)
            {
                WingetIndexEntry entry;
                try
                {
                    // The entire read, including default version, eligibility and publisher, is inside this guard.
                    entry = readPackage(index);
                    if (string.IsNullOrWhiteSpace(entry.Name) || string.IsNullOrWhiteSpace(entry.Version) ||
                        !Regex.IsMatch(entry.Id, "^[A-Za-z0-9][A-Za-z0-9._+-]{1,199}$"))
                        throw new InvalidDataException("Package has no valid identifier, name or pinned version.");
                }
                catch (Exception ex) when (IsPackageFailure(ex))
                {
                    if (IsTransient(ex) && attempt < MaxAttempts)
                    {
                        info?.Invoke($"Retry package {index + 1}/{packageCount}, attempt {attempt}/{MaxAttempts}: {ExportDiagnostics.Describe(ex)}");
                        await delay(TimeSpan.FromSeconds(attempt));
                        continue;
                    }
                    skipped++;
                    info?.Invoke($"Excluded package {index + 1}/{packageCount} after {attempt} attempt(s): {ExportDiagnostics.Describe(ex)}\n{ex}");
                    if (skipped > failureLimit)
                        throw new InvalidDataException($"Catalogue is degraded: {skipped} package read failures exceed the limit of {failureLimit}. Keeping the previous index.", ex);
                    break;
                }
                entries.Add(entry);
                break;
            }
            if ((index + 1) % 100 == 0)
                info?.Invoke($"Progress: {index + 1}/{packageCount}; entries: {entries.Count}; eligible: {entries.Count(x => x.Eligible)}; excluded read failures: {skipped}.");
        }
        info?.Invoke($"Catalogue totals: {entries.Count} entries; {entries.Count(x => x.Eligible)} eligible; {skipped} excluded read failures.");
        if (entries.Count < 1000 || entries.Count(x => x.Eligible) < 100 ||
            entries.Select(x => x.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != entries.Count)
            throw new InvalidDataException("Catalogue is incomplete, ambiguous or has fewer than 100 eligible packages. Keeping the previous index.");
        return entries;
    }

    private static int? HttpStatus(Exception exception)
    {
        if (exception is HttpRequestException http) return (int?)http.StatusCode;
        uint code = unchecked((uint)exception.HResult);
        return exception is COMException && (code & 0xFFFF0000u) == 0x80190000u
            ? (int)(code & 0xFFFFu) : null;
    }

    internal static bool IsTransient(Exception exception)
    {
        int? status = HttpStatus(exception);
        if (status is 408 or 429 or >= 500 and <= 599) return true;
        if (exception is HttpRequestException { StatusCode: null } or TimeoutException) return true;
        // Microsoft winerror.h: WinINet timeout, DNS failure, cannot connect and connection aborted.
        return exception is COMException && unchecked((uint)exception.HResult) is
            0x80072EE2u or 0x80072EE7u or 0x80072EFDu or 0x80072EFEu;
    }

    private static bool IsPackageFailure(Exception exception)
    {
        int? status = HttpStatus(exception);
        // Authentication/authorization, cancellation, COM compatibility and unexpected code failures abort globally.
        if (status is 401 or 403) return false;
        return IsTransient(exception) || status is >= 400 and <= 499 ||
            exception is InvalidDataException or FormatException or YamlException;
    }
}
