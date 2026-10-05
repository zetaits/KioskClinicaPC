using System.IO;
using System.Text;

namespace Kiosk.SetupHelper;

/// <summary>Export-only diagnostics; never reads environment variables, credentials or setup requests.</summary>
internal sealed class ExportDiagnostics : IDisposable
{
    private readonly TextWriter _output, _error;
    private readonly TextWriter? _file;
    public string CurrentPhase { get; private set; } = "Starting exporter";

    public ExportDiagnostics(string indexPath) : this(Console.Out, Console.Error,
        new StreamWriter(indexPath + ".diagnostics.log", false, new UTF8Encoding(false)) { AutoFlush = true }) { }

    internal ExportDiagnostics(TextWriter output, TextWriter error, TextWriter? file = null)
        => (_output, _error, _file) = (output, error, file);

    public void Phase(string phase)
    {
        CurrentPhase = phase;
        Info("PHASE: " + phase);
    }

    public void Info(string message)
    {
        string line = $"[{DateTime.UtcNow:O}] {message}";
        _output.WriteLine(line);
        _output.Flush();
        _file?.WriteLine(line);
        _file?.Flush();
    }

    public void Failure(Exception exception)
    {
        var report = new StringBuilder().AppendLine($"Export failed during: {CurrentPhase}");
        int depth = 0;
        for (Exception? error = exception; error != null; error = error.InnerException)
            report.AppendLine($"Exception[{depth++}]: {Describe(error)}");
        report.AppendLine("Full exception and stack trace:").AppendLine(exception.ToString());
        _error.WriteLine(report);
        _error.Flush();
        // Preserve the original failure even if the filesystem itself caused it.
        try { _file?.WriteLine(report); _file?.Flush(); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public static string Describe(Exception exception) =>
        $"{exception.GetType().FullName}; HRESULT=0x{exception.HResult:X8}; " +
        (string.IsNullOrWhiteSpace(exception.Message) ? "(empty exception message)" : exception.Message);

    public void Dispose() => _file?.Dispose();
}
