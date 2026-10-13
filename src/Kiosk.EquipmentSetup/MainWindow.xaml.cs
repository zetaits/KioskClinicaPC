using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using KioskClinicaPC.Core.Sync;
using KioskClinicaPC.Equipment;

namespace Kiosk.EquipmentSetup;
public partial class MainWindow : Window
{
    public sealed class ApplicationRow(PackApplication application)
    {
        public PackApplication Application { get; } = application;
        public bool Selected { get; set; } = application.SelectedByDefault;
        public string VersionDescription => string.IsNullOrWhiteSpace(Application.PinnedVersion) ? "Última versión compatible al instalar" : "Versión " + Application.PinnedVersion;
    }
    public sealed record ResultApplicationRow(string Name, string Status, string Detail, Brush Tone, Brush Surface);
    private int _step;
    private bool _loading, _running, _closeRequested, _canLaunch;
    private PackCatalog? _catalog;
    private PackRun? _lastRun;
    private List<ApplicationRow> _applications = [];
    private CancellationTokenSource? _cancel;
    private readonly Func<CancellationToken, Task<PackCatalog>> _load;
    private readonly Func<EquipmentRequest, Action<EquipmentEvent>, CancellationToken, Task<EquipmentEvent>> _start;
    private readonly Func<Task> _register;
    private readonly Action _launch;
    private readonly Action _restart;
    public MainWindow() : this(LoadFromPanel, WorkerPipe.Start, KioskPayload.RegisterAutostart) { }
    internal MainWindow(Func<CancellationToken, Task<PackCatalog>> load,
        Func<EquipmentRequest, Action<EquipmentEvent>, CancellationToken, Task<EquipmentEvent>> start, Func<Task> register, Action? launch = null, Action? restart = null)
    {
        Payload.UseAssembly(typeof(MainWindow).Assembly);
        _load = load; _start = start; _register = register;
        _launch = launch ?? (() =>
        {
            using var process = Process.Start(new ProcessStartInfo(KioskPayload.ClientExe) { UseShellExecute = true })
                ?? throw new InvalidOperationException("No se pudo iniciar Kiosk.");
        });
        _restart = restart ?? (() =>
        {
            string shutdown = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "shutdown.exe");
            using var process = Process.Start(new ProcessStartInfo(shutdown, "/r /t 0")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            }) ?? throw new InvalidOperationException("No se pudo iniciar el reinicio de Windows.");
            if (process.WaitForExit(5000) && process.ExitCode != 0)
                throw new InvalidOperationException("Windows rechazó la solicitud de reinicio.");
        });
        InitializeComponent();
        VersionLabel.Text = $"Asistente {Payload.Manifest.AssistantVersion} · Windows x64";
        EditionNotice.Text = Payload.Manifest.Edition == "online"
            ? "Edición online: descarga los componentes seleccionados. Necesita internet."
            : "Completo para USB: Kiosk se puede instalar sin conexión. El pack de aplicaciones necesita internet.";
        ShowStep(0);
    }
    private static async Task<PackCatalog> LoadFromPanel(CancellationToken ct)
    {
        using var http = Coordinator.CreateHttp();
        return await new EquipmentCatalogClient(http, Payload.Configuration).Load(ct);
    }
    internal void ShowStep(int step)
    {
        _step = step;
        ComponentsPanel.Visibility = step == 0 ? Visibility.Visible : Visibility.Collapsed;
        ApplicationsPanel.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
        ExecutionPanel.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
        ResultPanel.Visibility = step == 3 ? Visibility.Visible : Visibility.Collapsed;
        StepLabel.Text = new[] { "1 · Componentes", "2 · Aplicaciones", "3 · Comprobación e instalación", "4 · Resultado" }[step];
        BackButton.Visibility = step is 1 or 2 ? Visibility.Visible : Visibility.Collapsed;
        BackButton.Content = "Atrás";
        BackButton.IsEnabled = !_running;
        RetryButton.Visibility = Visibility.Collapsed;
        InstallAvailableButton.Visibility = Visibility.Collapsed;
        RestartNowButton.Visibility = Visibility.Collapsed;
        NextButton.Content = step == 2 ? "Instalar" : step == 3 ? "Finalizar" : "Continuar";
        UpdateFinishButton();
        NextButton.IsEnabled = CanContinue();
        CancelButton.Visibility = step == 3 ? Visibility.Collapsed : Visibility.Visible;
        CancelButton.IsEnabled = true;
        if (step == 0) StatusLabel.Text = "Selecciona al menos un componente.";
    }
    private bool CanContinue() => !_loading && !_running && (_step switch
    {
        0 => PackCheck?.IsChecked == true || KioskCheck?.IsChecked == true,
        1 => _catalog != null && _applications.Any(x => x.Selected),
        _ => true
    });
    private void ComponentsChanged(object sender, RoutedEventArgs e) { if (NextButton != null) NextButton.IsEnabled = CanContinue(); }
    private void ApplicationChanged(object sender, RoutedEventArgs e) => Dispatcher.BeginInvoke(() => NextButton.IsEnabled = CanContinue());
    private async void Next(object sender, RoutedEventArgs e)
    {
        if (_step == 0)
        {
            if (PackCheck.IsChecked == true) { ShowStep(1); await LoadCatalog(); }
            else Confirm();
        }
        else if (_step == 1) Confirm();
        else if (_step == 2) await Install();
        else Finish();
    }
    private void LaunchChanged(object sender, RoutedEventArgs e) => UpdateFinishButton();
    private void UpdateFinishButton()
    {
        if (_step == 3 && NextButton != null)
            NextButton.Content = _canLaunch && LaunchCheck.IsChecked == true ? "Finalizar y abrir Kiosk" : "Finalizar";
    }
    internal void Finish()
    {
        if (_step != 3 || _running || _loading) return;
        if (_canLaunch && LaunchCheck.IsChecked == true)
        {
            try { _launch(); _canLaunch = false; }
            catch
            {
                ResultLabel.Text = "Kiosk está instalado, pero no se pudo abrir. Puedes iniciarlo desde su acceso directo.";
                LaunchCheck.IsChecked = false;
                return;
            }
        }
        Close();
    }
    private void RestartNowClicked(object sender, RoutedEventArgs e)
    {
        bool confirmed = MessageBox.Show(this,
            "Windows cerrará las aplicaciones abiertas. Guarda tu trabajo antes de continuar. ¿Reiniciar ahora?",
            "Reiniciar Windows", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
        RestartNow(confirmed);
    }
    internal void RestartNow(bool confirmed)
    {
        if (!confirmed || _step != 3 || _running || _loading || RestartNowButton.Visibility != Visibility.Visible || !RestartNowButton.IsEnabled) return;
        try { _restart(); RestartNowButton.IsEnabled = false; }
        catch (Exception ex)
        {
            AssistantLog.Write("reinicio solicitado", ex);
            StatusLabel.Text = "No se pudo iniciar el reinicio. Puedes hacerlo desde Windows.";
        }
    }
    internal void Confirm()
    {
        ShowStep(2); Activity.Clear(); Progress.Visibility = Visibility.Collapsed;
        ResumeCheck.IsEnabled = true;
        PartialPackCheck.IsEnabled = true;
        PartialPackCheck.Visibility = PackCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        string components = PackCheck.IsChecked == true ? $"Pack: {_applications.Count(a => a.Selected)} aplicaciones · revisión {_catalog!.Revision}. " : "";
        SummaryLabel.Text = components + (KioskCheck.IsChecked == true ? $"Kiosk {Payload.Manifest.KioskVersion}." : "");
        Activity.Text = "Se prepararán y verificarán todos los componentes seleccionados antes de iniciar la instalación. " +
            (Payload.Manifest.Edition == "online" ? "Se descargarán los que falten en la caché.\n\n" : "Los componentes están incluidos en este asistente.\n\n") +
            (PackCheck.IsChecked == true ? "Se actualizará el origen oficial WinGet y se resolverá la última versión compatible de cada aplicación antes de instalar. Si eliges ambos, Kiosk se instala después de la comprobación y antes de las aplicaciones.\n\nLas aplicaciones disponibles se instalarán aunque alguna falle: las que fallen conservarán su diagnóstico y el pack quedará incompleto.\n\n" : "") +
            "Al reanudar se verificará lo ya instalado y se comprobarán de nuevo los pendientes. Nunca se reiniciará automáticamente.";
        StatusLabel.Text = "Windows pedirá permisos al comenzar.";
    }
    internal async Task LoadCatalog()
    {
        _cancel?.Dispose(); _cancel = new CancellationTokenSource(); var source = _cancel;
        _loading = true; _catalog = null; _applications = []; ApplicationsList.ItemsSource = _applications;
        CatalogLabel.Text = "Cargando aplicaciones del panel…"; StatusLabel.Text = "Consultando el pack…";
        CancelButton.IsEnabled = true;
        NextButton.IsEnabled = false; RetryButton.Visibility = Visibility.Collapsed;
        try
        {
            var catalog = await _load(source.Token);
            if (source.IsCancellationRequested || _step != 1) return;
            _catalog = catalog; _applications = catalog.Applications.OrderBy(a => a.Order).Select(a => new ApplicationRow(a)).ToList();
            ApplicationsList.ItemsSource = _applications;
            CatalogLabel.Text = catalog.Applications.Count == 0 ? "El pack del panel está vacío. Configura Aplicaciones en el panel o vuelve atrás para elegir solo Kiosk." : $"Revisión {catalog.Revision} · selecciona las aplicaciones que quieres instalar.";
            StatusLabel.Text = "Las versiones se comprobarán en este equipo al instalar.";
        }
        catch (OperationCanceledException) { if (_step == 1) { CatalogLabel.Text = "Carga cancelada. Puedes reintentar o volver atrás."; RetryButton.Visibility = Visibility.Visible; } }
        catch (Exception ex)
        {
            if (_step == 1) { CatalogLabel.Text = ex is CatalogException ? ex.Message : "No se pudo cargar el catálogo. Reintenta o vuelve atrás."; RetryButton.Visibility = Visibility.Visible; StatusLabel.Text = "No se ha iniciado ninguna instalación."; }
        }
        finally { _loading = false; NextButton.IsEnabled = CanContinue(); }
    }
    private async void Retry(object sender, RoutedEventArgs e) => await LoadCatalog();
    private void Back(object sender, RoutedEventArgs e)
    {
        _cancel?.Cancel();
        ShowStep(_step == 2 && PackCheck.IsChecked == true ? 1 : 0);
    }
    internal async Task Install()
    {
        _canLaunch = false;
        LaunchCheck.IsChecked = false;
        LaunchCheck.Visibility = Visibility.Collapsed;
        _cancel?.Dispose(); _cancel = new CancellationTokenSource();
        var request = new EquipmentRequest(PackCheck.IsChecked == true, KioskCheck.IsChecked == true, _catalog?.Revision ?? 0,
            PackCheck.IsChecked == true ? _applications.Where(a => a.Selected).Select(a => new EquipmentSelection(a.Application.Id, a.Application.PinnedVersion)).ToList() : [], ResumeCheck.IsChecked == true,
            PackCheck.IsChecked == true && PartialPackCheck.IsChecked == true, _catalog?.Definition is not null);
        ResumeCheck.IsEnabled = false;
        PartialPackCheck.IsEnabled = false;
        _running = true; _lastRun = null; NextButton.IsEnabled = false; BackButton.IsEnabled = false; Activity.Clear();
        Progress.Visibility = Visibility.Visible; Progress.IsIndeterminate = true; StatusLabel.Text = "Comprobando e instalando…";
        EquipmentEvent result;
        try
        {
            result = await _start(request, value => Dispatcher.Invoke(() =>
            {
                Progress.IsIndeterminate = value.Percent == null;
                if (value.Percent is { } percent) Progress.Value = percent;
                if (value.Run != null)
                {
                    _lastRun = value.Run; Activity.Text = string.Join("\n", value.Run.Items.Select(ItemDetails));
                    StatusLabel.Text = value.Run.ActivitySummary();
                }
                else { Activity.AppendText(value.Message + "\n"); Activity.ScrollToEnd(); }
            }), _cancel.Token);
            if (result.KioskVerified)
            {
                try { await _register(); }
                catch (Exception ex)
                {
                    AssistantLog.Write("registro del inicio automático", ex);
                    result = result with { ExitCode = 2, Message = result.Message + " No se pudo verificar el inicio automático para el usuario que abrió el asistente." };
                }
            }
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { result = new("result", "Se canceló la solicitud de permisos. No se inició la instalación.", ExitCode: 1); }
        catch (Exception ex)
        {
            string? log = AssistantLog.Write("resultado del trabajador", ex);
            result = new("result", "No se recibió un resultado verificable del trabajador. Puede quedar un instalador activo. Revisa los diagnósticos antes de reanudar." +
                (log is null ? "" : "\nDiagnóstico de la ventana: " + log), ExitCode: 2);
        }
        _running = false;
        if (result.Run is not null) _lastRun = result.Run;
        if (result.Kind == "review") { ShowStep(1); await LoadCatalog(); CatalogLabel.Text = result.Message; return; }
        ShowStep(3);
        bool stopped = result.ExitCode == 1 || _cancel.IsCancellationRequested ||
            _lastRun?.Items.Any(i => i.RequiresRebootBeforeRetry && !i.NativeInstallSucceeded) == true;
        bool hasVerified = result.KioskVerified || _lastRun?.Items.Any(i => i.Verified) == true;
        bool hasPending = _lastRun?.Items.Any(i => !i.Verified && i.State != PackItemState.Failed) == true;
        bool incomplete = hasVerified || hasPending;
        bool rebootPending = result.ExitCode == 0 && result.RebootRequired;
        string tone = result.ExitCode == 0 && !rebootPending ? "Success" : stopped || incomplete || rebootPending ? "Warning" : "Error";
        Brush color = (Brush)FindResource(tone + "Brush");
        ResultBanner.Background = (Brush)FindResource(tone + "SurfaceBrush");
        ResultBanner.BorderBrush = color;
        ResultIconBorder.Background = color;
        ResultStateBadge.Foreground = color;
        ResultIcon.Text = result.ExitCode == 0 && !rebootPending ? "✓" : stopped || incomplete || rebootPending ? "!" : "×";
        ResultStateBadge.Text = rebootPending ? "REINICIO PENDIENTE" : result.ExitCode == 0 ? "COMPLETADO" :
            stopped ? "DETENIDO" : incomplete ? "INCOMPLETO" : "ERROR";
        ResultTitle.Text = rebootPending && result.KioskRebootRequired ? "Reinicio pendiente" : result.ExitCode == 0 ? "Preparación completa" : stopped ? "Preparación detenida" :
            hasVerified ? "Preparación parcial" : hasPending ? "Preparación pendiente" : "No se pudo completar";
        ResultLabel.Text = result.Message + (result.RebootRequired
            ? result.KioskRebootRequired ? "\nReinicia Windows antes de abrir Kiosk."
                : request.Kiosk && result.KioskVerified && result.ExitCode == 0 ? "\nPuedes reiniciar Windows ahora o más tarde; Kiosk ya puede abrirse."
                : "\nPuedes reiniciar Windows ahora o más tarde para aplicar los cambios pendientes."
            : "");
        ResultDetails.Text = _lastRun == null ? "" : string.Join("\n", _lastRun.Items.Select(ItemDetails));
        ResultDetailsExpander.Visibility = string.IsNullOrWhiteSpace(ResultDetails.Text) ? Visibility.Collapsed : Visibility.Visible;
        ResultDetailsExpander.IsExpanded = false;
        bool hasApplications = _lastRun?.Items.Count > 0;
        ResultStats.Visibility = hasApplications ? Visibility.Visible : Visibility.Collapsed;
        ResultApplicationsPanel.Visibility = hasApplications ? Visibility.Visible : Visibility.Collapsed;
        if (_lastRun is { } completedRun)
        {
            ResultVerifiedCount.Text = completedRun.Items.Count(i => i.Verified).ToString();
            ResultPendingCount.Text = completedRun.Items.Count(i => !i.Verified && i.State != PackItemState.Failed).ToString();
            ResultFailedCount.Text = completedRun.Items.Count(i => i.State == PackItemState.Failed).ToString();
            ResultApplications.ItemsSource = completedRun.Items
                .OrderBy(i => i.Verified ? 2 : i.State == PackItemState.Failed ? 0 : 1)
                .Select(ResultRow).ToList();
        }
        else ResultApplications.ItemsSource = null;
        if (result.ExitCode != 0) { BackButton.Visibility = Visibility.Visible; BackButton.Content = "Revisar"; }
        InstallAvailableButton.Visibility = result.ExitCode == 1 && !request.AllowPartialPack && _lastRun is { } run &&
            run.Items.Any(i => i.State == PackItemState.Failed) && run.Items.Any(i => i.State == PackItemState.Pending || i.Verified) &&
            !run.Items.Any(i => i.RequiresRebootBeforeRetry) ? Visibility.Visible : Visibility.Collapsed;
        // WorkerPipe returns only after the elevated worker has exited. A successful combined
        // run also waits for the pack session and its final callbacks before offering launch.
        _canLaunch = request.Kiosk && result.KioskVerified && result.ExitCode == 0 && !result.KioskRebootRequired &&
            !_cancel.IsCancellationRequested && !_closeRequested;
        RestartNowButton.Visibility = result.RebootRequired ? Visibility.Visible : Visibility.Collapsed;
        RestartNowButton.IsEnabled = true;
        LaunchCheck.Visibility = _canLaunch ? Visibility.Visible : Visibility.Collapsed;
        LaunchCheck.IsChecked = _canLaunch;
        UpdateFinishButton();
        StatusLabel.Text = rebootPending && result.KioskRebootRequired ? "Reinicia Windows antes de abrir Kiosk." : rebootPending ? "Reinicia Windows ahora o más tarde." : result.ExitCode == 0 ? "Todo listo." : stopped ? "Revisa el motivo antes de continuar." :
            incomplete ? "Quedan componentes por revisar." : "Revisa el error antes de reintentar.";
        if (_closeRequested) StatusLabel.Text += " · La operación ha terminado; ya puedes cerrar.";
    }
    private ResultApplicationRow ResultRow(PackItemResult item)
    {
        bool verified = item.Verified;
        bool failed = item.State == PackItemState.Failed;
        string tone = verified ? "Success" : failed ? "Error" : "Warning";
        string status = item.State switch
        {
            PackItemState.Succeeded => "Verificada",
            PackItemState.AlreadyInstalled => "Ya estaba",
            PackItemState.Failed => "Error",
            PackItemState.VerificationPending => "Sin verificar",
            _ => "Pendiente"
        };
        string? version = item.InstalledVersion ?? (string.IsNullOrWhiteSpace(item.Application.PinnedVersion) ? null : item.Application.PinnedVersion);
        string versionText = version is null ? "" : item.InstalledVersion is null ? $"Objetivo: {version}" : $"Versión {version}";
        string message = item.Message?.Trim() ?? "";
        if (verified && (message == "Instalada y verificada." || message == "Ya instalada y verificada.")) message = "";
        if (verified && message == "Ya instalada y verificada (todo el equipo).") message = "Verificada para todo el equipo";
        if (message == "Pendiente") message = "";
        string detail = string.Join(" · ", (verified ? new[] { versionText, message } : new[] { message, versionText })
            .Where(s => !string.IsNullOrEmpty(s)));
        if (string.IsNullOrEmpty(detail)) detail = status;
        return new(item.Application.DisplayName, status, detail,
            (Brush)FindResource(tone + "Brush"), (Brush)FindResource(tone + "SurfaceBrush"));
    }
    private static string ItemDetails(PackItemResult item) => $"{item.Application.DisplayName} · " +
        (string.IsNullOrWhiteSpace(item.Application.PinnedVersion) ? "sin versión resuelta" : $"resuelta {item.Application.PinnedVersion}") +
        (item.InstalledVersion is null ? "" : $" · instalada {item.InstalledVersion}") + $" · {item.ActivityDescription()}";
    private async void InstallAvailable(object sender, RoutedEventArgs e) => await ContinueAvailable();
    internal async Task ContinueAvailable()
    {
        PartialPackCheck.IsChecked = true;
        Confirm();
        await Install();
    }
    private void Cancel(object sender, RoutedEventArgs e)
    {
        if (_running || _loading) { _cancel?.Cancel(); StatusLabel.Text = _running ? "Cancelación solicitada. Esperando al instalador…" : "Cancelando carga…"; CancelButton.IsEnabled = false; }
        else Close();
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_running) { e.Cancel = true; _closeRequested = true; _cancel?.Cancel(); StatusLabel.Text = "Esperando a que el trabajador termine. No se matará al instalador."; }
        else { _cancel?.Cancel(); _cancel?.Dispose(); }
    }
    private void MinimizeWindow(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);
    private void ToggleMaximize(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
        else SystemCommands.MaximizeWindow(this);
    }
    private void CloseWindow(object sender, RoutedEventArgs e) => Close();
    private void WindowStateChanged(object? sender, EventArgs e)
    {
        if (MaximizeGlyph == null) return;
        bool maximized = WindowState == WindowState.Maximized;
        MaximizeGlyph.Data = System.Windows.Media.Geometry.Parse(maximized
            ? "M 4,1 L 11,1 L 11,8 M 1,4 L 8,4 L 8,11 L 1,11 Z"
            : "M 1,1 L 11,1 L 11,11 L 1,11 Z");
        MaximizeButton.ToolTip = maximized ? "Restaurar" : "Maximizar";
        System.Windows.Automation.AutomationProperties.SetName(MaximizeButton, maximized ? "Restaurar" : "Maximizar");
    }
}
