using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Kiosk.Deployment;
using Microsoft.Win32;

namespace Kiosk.DeploymentManager;

public sealed record DiskRow(DeploymentDisk Disk)
{ public string Label => $"{Disk.Number}: {Disk.Model} · {Disk.SizeBytes / (1024L * 1024 * 1024)} GB · {Disk.Serial}"; }
public sealed class TargetRow(BootSession session, string username) : INotifyPropertyChanged
{
    public BootSession Session { get; } = session;
    public string Label => Session.DisplayId + " · " + Session.Hardware.Model;
    public string Serial => Session.Hardware.Serial;
    public bool Ready => Session.State == DeploymentState.Ready;
    public string StateLabel => Session.State switch { DeploymentState.Ready => "Listo para instalar", DeploymentState.Queued => "En cola", DeploymentState.Installing => "Instalando", DeploymentState.PostInstall => "Preparando escritorio", DeploymentState.Completed => "Finalizado", DeploymentState.Attention => "Requiere atención", _ => Session.State.ToString() };
    public List<DiskRow> Disks { get; } = session.Hardware.Disks.Where(DeploymentPolicy.Candidate).Select(d => new DiskRow(d)).ToList();
    private DiskRow? _disk;
    private bool _selected;
    private string _username = username;
    public DiskRow? Disk { get => _disk; set { _disk = value; Changed(); } }
    public bool Selected { get => _selected; set { _selected = value && Ready; Changed(); } }
    public string Username { get => _username; set { _username = value; Changed(); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
public partial class MainWindow : Window
{
    private bool _busy, _reviewing, _bindingRefresh;
    private LocalStationView? _view;
    private List<TargetRow> _targets = [];
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromSeconds(5) };
    public MainWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => { await Refresh(); if (_view is null || !_view.Enabled) Tabs.SelectedItem = ConfigurationTab; _refresh.Start(); };
        _refresh.Tick += async (_, _) => { if (!_busy && !_reviewing) await Refresh(); };
        Closed += (_, _) => _refresh.Stop(); // Closing this ordinary window leaves the Windows service running.
    }
    private async Task<bool> Work(Func<Task> action, string message)
    {
        if (_busy) return false; _busy = true; BusyBar.Visibility = Visibility.Visible; StatusLabel.Text = message;
        try { await action(); await Refresh(); StatusLabel.Text = "Operación completada."; return true; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException or ArgumentException)
        { StatusLabel.Text = ex is IOException or InvalidDataException ? ex.Message : "Revisa los datos y los permisos, y vuelve a intentarlo."; return false; }
        finally { _busy = false; BusyBar.Visibility = Visibility.Collapsed; }
    }
    private async Task Refresh()
    {
        try
        {
            _view = await LocalServiceClient.Snapshot();
            _bindingRefresh = true;
            ConnectionLabel.Text = (_view.PanelConnected ? "Panel conectado" : "Panel sin conexión") + " · " + (_view.Enabled ? "PXE activo" : "PXE desactivado") + " · Contraseña " + (_view.PasswordConfigured ? "configurada" : "pendiente");
            var profileId = (ProfileBox.SelectedItem as DeploymentProfile)?.Id;
            ProfileBox.ItemsSource = _view.Configuration?.Profiles;
            ProfileBox.SelectedItem = _view.Configuration?.Profiles.FirstOrDefault(p => p.Id == profileId) ?? _view.Configuration?.Profiles.FirstOrDefault();
            var old = _targets.ToDictionary(t => t.Session.Id);
            _targets = _view.Queue.Sessions.Select(session =>
            {
                old.TryGetValue(session.Id, out var prior);
                var row = new TargetRow(session, prior is not null && prior.Session.OptionsRevision == session.OptionsRevision
                    ? prior.Username : session.PendingUsername ?? (ProfileBox.SelectedItem as DeploymentProfile)?.Username ?? "Usuario");
                row.Selected = prior?.Selected == true;
                row.Disk = row.Disks.FirstOrDefault(d => d.Disk.UniqueId == prior?.Disk?.Disk.UniqueId) ?? (row.Disks.Count == 1 ? row.Disks[0] : null);
                return row;
            }).ToList();
            TargetsGrid.ItemsSource = _targets; JobsGrid.ItemsSource = _view.Queue.Jobs.OrderByDescending(j => j.CreatedAtUtc);
            ImagesList.ItemsSource = _view.Queue.Images;
            DetectedList.ItemsSource = _view.Devices.Select(d => d.Address + " · " + (d.State == DeploymentState.Booting ? "Preparando arranque PXE · " + d.Mac : "Detectado en red"));
            var selected = AdapterBox.SelectedItem as EthernetAdapter;
            AdapterBox.ItemsSource = _view.Adapters;
            AdapterBox.SelectedItem = _view.Adapters.FirstOrDefault(a => a.Id == selected?.Id) ?? _view.Adapters.FirstOrDefault(a => a.Id == _view.AdapterId) ?? _view.Adapters.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(StorageBox.Text)) StorageBox.Text = _view.Storage ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ClinicaPC", "Deployment", "library");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { StatusLabel.Text = ex.Message; }
        finally { _bindingRefresh = false; }
    }
    private async void EnrollClick(object sender, RoutedEventArgs e) => await Work(async () =>
    { await LocalServiceClient.Send("enroll", new LocalEnrollment(ServerBox.Text, CodeBox.Text.Trim(), StationNameBox.Text)); CodeBox.Clear(); }, "Vinculando estación…");
    private void StorageClick(object sender, RoutedEventArgs e)
    { var dialog = new OpenFolderDialog { Title = "Carpeta local para imágenes" }; if (dialog.ShowDialog(this) == true) StorageBox.Text = dialog.FolderName; }
    private async void NetworkClick(object sender, RoutedEventArgs e) => await Work(async () =>
    {
        var adapter = AdapterBox.SelectedItem as EthernetAdapter ?? throw new InvalidDataException("Selecciona una interfaz Ethernet conectada.");
        if (!int.TryParse(CapacityBox.Text, out int capacity)) throw new InvalidDataException("Indica una capacidad entre 1 y 8.");
        await LocalServiceClient.Send("network", new LocalNetwork(adapter.Id, adapter.Address, adapter.Mask, StorageBox.Text, capacity));
    }, "Guardando red y almacenamiento…");
    private async void PasswordClick(object sender, RoutedEventArgs e) => await Work(async () =>
    { await LocalServiceClient.Send("password", DefaultPasswordBox.Password); DefaultPasswordBox.Clear(); }, "Protegiendo contraseña local…");
    private async void ImportClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "ISO de Windows|*.iso", Title = "Importar ISO oficial de Windows 11" };
        if (dialog.ShowDialog(this) == true) await Work(async () => await LocalServiceClient.Send("import", dialog.FileName), "Verificando y extrayendo ISO. Esta operación puede tardar varios minutos…");
    }
    private async void DriversClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Carpeta de controladores INF firmados" };
        if (dialog.ShowDialog(this) == true) await Work(async () => await LocalServiceClient.Send("drivers", dialog.FolderName), "Validando controladores firmados…");
    }
    private async void ActivateClick(object sender, RoutedEventArgs e) => await Work(async () => await LocalServiceClient.Send("activate", new { }), "Comprobando red, recursos y puertos; activando PXE…");
    private async void DeactivateClick(object sender, RoutedEventArgs e) => await Work(async () => await LocalServiceClient.Send("deactivate", new { }), "Desactivando PXE…");
    private async void SyncClick(object sender, RoutedEventArgs e) => await Work(async () => await LocalServiceClient.Send("sync", new { }), "Recibiendo perfiles y opciones pendientes…");
    private async void ScanClick(object sender, RoutedEventArgs e) => await Work(async () => await LocalServiceClient.Send("scan", new { }), "Explorando solo la subred seleccionada…");
    private void ProfileChanged(object sender, SelectionChangedEventArgs e)
    { if (!_bindingRefresh && ProfileBox.SelectedItem is DeploymentProfile profile) foreach (var row in _targets.Where(t => t.Ready && t.Session.PendingUsername is null)) row.Username = profile.Username; }
    private async void ReviewClick(object sender, RoutedEventArgs e)
    {
        if (_busy || _reviewing) return;
        TargetsGrid.CommitEdit(DataGridEditingUnit.Cell, true); TargetsGrid.CommitEdit(DataGridEditingUnit.Row, true);
        if (!await Work(async () => await LocalServiceClient.Send("sync", new { }), "Comprobando las últimas opciones antes de revisar…")) return;
        var targets = _targets.Where(t => t.Selected && t.Ready).ToList();
        var profile = ProfileBox.SelectedItem as DeploymentProfile;
        if (targets.Count == 0 || profile?.ImageId is null || profile.EditionIndex is null) { StatusLabel.Text = "Selecciona equipos listos y un perfil con imagen y edición."; return; }
        if (targets.Any(t => t.Disk is null)) { StatusLabel.Text = "Selecciona un disco interno para cada equipo. Con varios discos la elección es obligatoria."; return; }
        if (!_view!.Queue.Images.Any(i => i.Id == profile.ImageId)) { StatusLabel.Text = "Importa la imagen de este perfil en la estación antes de continuar."; return; }
        _reviewing = true;
        try
        {
            var review = new ReviewWindow(targets, profile, _view!.Queue.Images.SingleOrDefault(i => i.Id == profile.ImageId)) { Owner = this };
            if (review.ShowDialog() != true) return;
            var request = new DeploymentConfirmation(Guid.NewGuid().ToString("N"), profile.Id, profile.Revision,
                targets.Select(t => new DeploymentSelection(t.Session.Id, t.Disk!.Disk.UniqueId, t.Username, t.Session.OptionsRevision)).ToList());
            await Work(async () => await LocalServiceClient.Send("confirm", new LocalConfirmation(request, review.Passwords)), "Confirmando opciones e iniciando la cola…");
            Tabs.SelectedIndex = 2;
        }
        finally { _reviewing = false; }
    }
    private async void CancelClick(object sender, RoutedEventArgs e)
    { if (JobsGrid.SelectedItem is DeploymentJob job) await Work(async () => await LocalServiceClient.Send("cancel", job.Id), "Cancelando trabajo en cola…"); }
    private async void ResumeClick(object sender, RoutedEventArgs e)
    {
        if (JobsGrid.SelectedItem is not DeploymentJob job) return;
        if (MessageBox.Show(this, "Comprueba físicamente que Windows y la cuenta están disponibles y que no hay instaladores nativos activos. ¿Autorizas reanudar la preparación de este trabajo?", "Revisión de recuperación", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        if (await Work(async () => await LocalServiceClient.Send("resume-components", new LocalRecovery(job.Id, true)), "Autorizando recuperación de aplicaciones sin reinstalar Windows…"))
            StatusLabel.Text = "En el destino, ejecuta Kiosk.DeploymentPostInstall.exe --resume desde C:\\ProgramData\\ClinicaPC\\DeploymentJob con el usuario creado como administrador.";
    }
    private async void CloseFailureClick(object sender, RoutedEventArgs e)
    {
        if (JobsGrid.SelectedItem is not DeploymentJob job) return;
        if (MessageBox.Show(this, "Comprueba físicamente que no queda ningún instalador activo en este destino. ¿Cierras esta incidencia? El historial se conserva y no se repetirá el borrado.", "Cerrar incidencia revisada", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        await Work(async () => await LocalServiceClient.Send("close-failure", new LocalRecovery(job.Id, true)), "Cerrando incidencia revisada…");
    }
    private async void ExportClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "Diagnóstico JSON|*.json", FileName = "clinicapc-despliegue-diagnostico.json" };
        if (dialog.ShowDialog(this) != true) return;
        await Work(async () => { var data = await LocalServiceClient.Send("diagnostics", new { }); await File.WriteAllTextAsync(dialog.FileName, JsonSerializer.Serialize(data, AtomicState.Json)); }, "Exportando diagnóstico sin credenciales…");
    }
}
