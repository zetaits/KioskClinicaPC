using System.ComponentModel;
using System.Diagnostics;
using System.Net.Sockets;

namespace KioskClinicaPC.Equipment;

public static class EquipmentDiagnostics
{
    private sealed record KnownReason(string Text);
    private const string ReasonKey = "EquipmentFailureReason";

    // Only supply application-defined reasons, never HTTP bodies, configuration or exception messages.
    public static InvalidDataException InvalidData(string reason, Exception? innerException = null)
    {
        var error = new InvalidDataException(reason, innerException);
        error.Data[ReasonKey] = new KnownReason(reason);
        return error;
    }

    public static IOException IOError(string reason, Exception? innerException = null)
    {
        var error = new IOException(reason, innerException);
        error.Data[ReasonKey] = new KnownReason(reason);
        return error;
    }

    public static string Describe(Exception error)
    {
        var causes = new List<string>();
        for (Exception? current = error; current != null && causes.Count < 8; current = current.InnerException)
        {
            string detail = $"{current.GetType().Name}; HRESULT {current.HResult:X8}";
            if (current.Data[ReasonKey] is KnownReason reason) detail += "; reason: " + reason.Text;
            if (current is HttpRequestException http)
            {
                detail += "; HTTP error " + http.HttpRequestError;
                if (http.StatusCode is { } status) detail += "; HTTP status " + (int)status;
            }
            if (current is SocketException socket) detail += "; socket " + socket.SocketErrorCode;
            if (current is Win32Exception native) detail += "; native error " + native.NativeErrorCode;
            if (current is CatalogException catalog) detail += "; catalog " + catalog.Failure;
            // Method names and source line numbers identify the failing operation without dumping
            // arbitrary exception messages (which may contain URLs, headers or credentials).
            var frames = new StackTrace(current, true).GetFrames().Take(8).Select(frame =>
                $"{frame.GetMethod()?.DeclaringType?.FullName}.{frame.GetMethod()?.Name}:{frame.GetFileLineNumber()}");
            detail += "; at " + string.Join(" > ", frames);
            causes.Add(detail);
        }
        return string.Join(" | caused by: ", causes);
    }

    public static string FormatEvent(EquipmentEvent value) =>
        $"{value.Kind}: {value.Message}" + (value.Percent is { } percent ? $"; progress {percent}%" : "");
}
