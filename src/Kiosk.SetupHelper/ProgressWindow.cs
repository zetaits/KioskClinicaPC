using System.Windows;
using System.Windows.Controls;
using KioskClinicaPC.Core.Sync;

namespace Kiosk.SetupHelper;

internal sealed class ProgressWindow : Window
{
    private readonly TextBox _details = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly TextBlock _status = new() { Text = "Comprobando selección…", Margin = new Thickness(0, 8, 0, 8), TextWrapping = TextWrapping.Wrap };
    private readonly Button _retry = new() { Content = "Reintentar pendientes", IsEnabled = false, Margin = new Thickness(8) };
    private readonly Button _cancel = new() { Content = "Detener cola", Margin = new Thickness(8) };
    private CancellationTokenSource _cancellation = new();
    private readonly Ini _request;
    private readonly string _output;
    private PackRun? _run;
    private bool _running;
    public int ExitCode { get; private set; } = 2;
    public ProgressWindow(Ini request, string output)
    {
        _request = request; _output = output;
        Title = "Clínica PC · Instalación de aplicaciones"; Width = 740; Height = 520; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var panel = new DockPanel { Margin = new Thickness(20) }; Content = panel;
        DockPanel.SetDock(_status, Dock.Top); panel.Children.Add(_status);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(buttons, Dock.Bottom);
        buttons.Children.Add(_cancel); buttons.Children.Add(_retry); panel.Children.Add(buttons); panel.Children.Add(_details);
        _cancel.Click += (_, _) => { if (_running) { _cancellation.Cancel(); _cancel.IsEnabled = false; } else Close(); };
        _retry.Click += async (_, _) => await Run(_run);
        Closing += (_, e) => { if (_running) { e.Cancel = true; _cancellation.Cancel(); _status.Text = "Deteniendo cola; no cierres Windows mientras siga activo un instalador."; } };
        // Resume choice happens in the installer before Start, never as a blocking dialog during execution.
        Loaded += async (_, _) => await Run(null);
    }
    private async Task Run(PackRun? resume)
    {
        _running = true; _retry.IsEnabled = false; _cancel.Content = "Detener cola"; _cancel.IsEnabled = true;
        _cancellation.Dispose(); _cancellation = new();
        try
        {
            ExitCode = await Task.Run(() => Program.Execute(_request, _output, run => Dispatcher.InvokeAsync(() =>
            {
                _run = run; _details.Text = string.Join("\n\n", run.Items.Select(x => $"{x.Application.DisplayName} ({x.Application.PinnedVersion}) · {StateLabel(x.State)}\n{x.Message}"));
                _status.Text = run.Items.Any(x => x.State is PackItemState.Installing or PackItemState.Checking) ? "Instalación en curso. Puedes dejar el equipo trabajando." : run.Summary;
            }), false, _cancellation.Token, resume));
            _status.Text = _run?.Summary ?? "No se recibió un resultado.";
        }
        catch (Exception ex) { _status.Text = "ERROR: " + ex.Message; Ini.Write(_output, new() { ["Result"] = new() { ["Ok"] = "0", ["Error"] = ex.Message } }); }
        finally { _running = false; _cancel.IsEnabled = true; _cancel.Content = "Cerrar"; _retry.IsEnabled = _run != null && !_run.Complete && !_run.Items.Any(x => x.RequiresRebootBeforeRetry || x.State == PackItemState.VerificationPending); }
    }
    private static string StateLabel(PackItemState state) => state switch
    {
        PackItemState.Pending => "Pendiente", PackItemState.Checking => "Comprobando",
        PackItemState.Installing => "Instalando", PackItemState.Succeeded => "Instalada y verificada",
        PackItemState.AlreadyInstalled => "Ya instalada", PackItemState.Failed => "Error",
        _ => "Verificación pendiente"
    };
}
