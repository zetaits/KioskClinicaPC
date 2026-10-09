using System.Net;
using System.Net.Sockets;
using KioskClinicaPC.Equipment;
using Xunit;

namespace Kiosk.Server.Tests;

public sealed class EquipmentDiagnosticsTests
{
    [Fact]
    public void Nested_network_failure_keeps_status_native_cause_and_safe_reason_without_secrets()
    {
        var socket = new SocketException((int)SocketError.HostNotFound);
        var http = new HttpRequestException("https://user:secret@panel.invalid/?key=secret", socket, HttpStatusCode.ServiceUnavailable);
        http.Data["X-Setup-Key"] = "header-secret";
        var error = new ComponentPreparationException("request-secret", EquipmentDiagnostics.IOError("Descarga fallida tras tres intentos.", http));
        string log = EquipmentDiagnostics.Describe(error);
        Assert.Contains("ComponentPreparationException", log);
        Assert.Contains("caused by:", log);
        Assert.Contains("Descarga fallida tras tres intentos", log);
        Assert.Contains("HTTP status 503", log);
        Assert.Contains("socket HostNotFound", log);
        Assert.DoesNotContain("secret", log);
        Assert.DoesNotContain("panel.invalid", log);
        Assert.DoesNotContain("X-Setup-Key", log);
    }

    [Fact]
    public void Unmarked_exception_messages_and_data_are_not_dumped()
    {
        var error = new InvalidDataException("config-secret");
        error.Data["EquipmentFailureReason"] = "forged-secret";
        Assert.DoesNotContain("secret", EquipmentDiagnostics.Describe(error));
    }

    [Fact]
    public void Progress_events_include_the_percentage_in_the_persisted_line()
    {
        Assert.Equal("prepare: Descarga de Kiosk; progress 43%",
            EquipmentDiagnostics.FormatEvent(new("prepare", "Descarga de Kiosk", Percent: 43)));
        Assert.Equal("prepare-stage: Kiosk; fase: verificación de versión",
            EquipmentDiagnostics.FormatEvent(new("prepare-stage", "Kiosk; fase: verificación de versión")));
    }
}
