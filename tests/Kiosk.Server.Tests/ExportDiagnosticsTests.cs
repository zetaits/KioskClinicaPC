using System.Runtime.InteropServices;
using Kiosk.SetupHelper;
using Xunit;

namespace Kiosk.Server.Tests;

public sealed class ExportDiagnosticsTests
{
    [Fact]
    public void EmptyComMessageStillReportsPhaseTypeAndHResult()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        using var file = new StringWriter();
        using var diagnostics = new ExportDiagnostics(output, error, file);
        diagnostics.Phase("Connecting to official winget source");
        diagnostics.Failure(new COMException("", unchecked((int)0x80004005)));
        Assert.Contains("Connecting to official winget source", error.ToString());
        Assert.Contains("System.Runtime.InteropServices.COMException", error.ToString());
        Assert.Contains("HRESULT=0x80004005", error.ToString());
        Assert.Contains("empty exception message", error.ToString());
        Assert.Contains("Full exception and stack trace", file.ToString());
    }

    [Fact]
    public void ReportsInnerExceptionAndStackTrace()
    {
        Exception failure;
        try { ThrowNativeFailure(); throw new InvalidOperationException("Unexpected success"); }
        catch (COMException ex) { failure = new InvalidOperationException("Enumeration failed", ex); }
        using var output = new StringWriter();
        using var error = new StringWriter();
        using var diagnostics = new ExportDiagnostics(output, error);
        diagnostics.Failure(failure);
        Assert.Contains("Enumeration failed", error.ToString());
        Assert.Contains("Exception[1]", error.ToString());
        Assert.Contains("HRESULT=0x80004002", error.ToString());
        Assert.Contains(nameof(ThrowNativeFailure), error.ToString());
    }

    [Fact]
    public void ProgressIsWrittenToConsoleAndPersistentLog()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        using var file = new StringWriter();
        using var diagnostics = new ExportDiagnostics(output, error, file);
        diagnostics.Phase("Checking catalogue completeness");
        diagnostics.Info("Catalogue totals: 1200 entries; 300 eligible.");
        Assert.Equal(output.ToString(), file.ToString());
        Assert.Equal("Checking catalogue completeness", diagnostics.CurrentPhase);
        Assert.Contains("1200 entries; 300 eligible", file.ToString());
        Assert.Empty(error.ToString());
    }

    private static void ThrowNativeFailure() => throw new COMException("Native interface unavailable", unchecked((int)0x80004002));
}
