using System.Text.Json;
using KioskClinicaPC.Equipment;

namespace Kiosk.Server.Services;

internal static class InstallerMetadataFiles
{
    public static FileStream OpenManifest(string path)
    {
        SetupComponentCache.SafePath(path);
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
    }

    public static void RequireAvailable(string path, long size)
    {
        SetupComponentCache.SafePath(path);
        if (new FileInfo(path).Length != size)
            throw new InvalidDataException("El tamaño del archivo no coincide con su manifiesto.");
    }

    public static bool IsReadError(Exception ex) => ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException;
}
