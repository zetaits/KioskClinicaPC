using System.Windows;
using KioskClinicaPC.Core.Sync;
namespace Kiosk.SetupHelper;
internal static class Program
{
    [STAThread]
    public static int Main(string[] args) => LegacyCommands.Run(args, (request, output) =>
    {
        var app = new Application(); var window = new ProgressWindow(request, output);
        app.Run(window); return window.ExitCode;
    });
    internal static Task<int> Execute(Ini request, string output, Action<PackRun>? changed, bool preflight, CancellationToken ct, PackRun? resume = null) =>
        LegacyCommands.Execute(request, output, changed, preflight, ct, resume);
}
