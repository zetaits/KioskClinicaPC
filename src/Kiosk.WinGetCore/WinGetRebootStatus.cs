using System.IO;
using System.Runtime.Versioning;
using System.Security;
using Microsoft.Win32;

namespace Kiosk.SetupHelper;

internal static class WinGetRebootStatus
{
    internal const string Servicing = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending";
    internal const string WindowsUpdate = @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired";
    internal const string SessionManager = @"SYSTEM\CurrentControlSet\Control\Session Manager";

    // A manifest's additional success codes can suppress WinGet's reboot flag.
    // Windows may also have pending operations from an earlier installation.
    internal static bool Required(bool winGetRequired, uint installerCode, bool systemPending)
        => winGetRequired || installerCode is 3010 or 1641 || systemPending;

    internal static bool Read(Func<string, bool> keyExists, Func<string, object?> sessionValue)
        => keyExists(Servicing) || keyExists(WindowsUpdate) ||
            HasPendingFiles(sessionValue("PendingFileRenameOperations")) ||
            HasPendingFiles(sessionValue("PendingFileRenameOperations2"));

    private static bool HasPendingFiles(object? value)
        => value is string[] files && files.Any(file => !string.IsNullOrWhiteSpace(file));

    [SupportedOSPlatform("windows")]
    internal static bool ReadSystemPending(Action<string>? diagnostic = null)
    {
        try { return ReadRegistry(diagnostic); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        { diagnostic?.Invoke($"Reboot check unavailable: {ex.GetType().Name}; HRESULT {ex.HResult:X8}"); return false; }
    }

    [SupportedOSPlatform("windows")]
    private static bool ReadRegistry(Action<string>? diagnostic)
    {
        // Always read the machine's 64-bit view, regardless of the caller's process.
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        bool Exists(string path)
        {
            try { using var key = machine.OpenSubKey(path); return key is not null; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            { diagnostic?.Invoke($"Reboot check unavailable: {ex.GetType().Name}; HRESULT {ex.HResult:X8}"); return false; }
        }
        object? Value(string name)
        {
            try { using var key = machine.OpenSubKey(SessionManager); return key?.GetValue(name); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            { diagnostic?.Invoke($"Reboot check unavailable: {ex.GetType().Name}; HRESULT {ex.HResult:X8}"); return null; }
        }
        return Read(Exists, Value);
    }
}
