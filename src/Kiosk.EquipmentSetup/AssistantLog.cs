using System.IO;
using KioskClinicaPC.Equipment;

namespace Kiosk.EquipmentSetup;

internal static class AssistantLog
{
    internal static string? Write(string phase, Exception error, string? directory = null)
    {
        try
        {
            directory ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KioskClinicaPC", "Setup", "logs");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "assistant-" + DateTime.UtcNow.ToString("yyyyMMdd") + ".log");
            File.AppendAllText(path, $"{DateTime.UtcNow:O} {phase}: {EquipmentDiagnostics.Describe(error)}{Environment.NewLine}");
            return path;
        }
        catch { return null; } // Logging must never replace an installation result.
    }
}
