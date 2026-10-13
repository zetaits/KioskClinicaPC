using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using Kiosk.EquipmentSetup;
using KioskClinicaPC.Core.Sync;
using KioskClinicaPC.Equipment;
using Xunit;

namespace Kiosk.EquipmentSetup.Tests;
[Collection("Equipment resources")]
public sealed class WizardTests
{
    private static PackCatalog Catalog => new(7, [new(new string('a', 32), "Vendor.App", "Aplicación del panel", "3.2", true), new(new string('b', 32), "Vendor.Other", "Opcional", "2.0", false)]);
    private static Task Sta(Func<Task> test)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(async () =>
            {
                try { await test(); done.SetResult(); } catch (Exception ex) { done.SetException(ex); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA); thread.IsBackground = true; thread.Start();
        return done.Task;
    }
    [Fact]
    public Task Concurrent_pack_progress_shows_download_install_and_verification_without_reordering_the_list() => Sta(async () =>
    {
        var completed = new TaskCompletionSource<EquipmentEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = new PackRun { Items =
        [
            new() { Application = Catalog.Applications[0], State = PackItemState.Installing, Phase = PackItemPhase.Downloading, Message = "Descargando 5 / 10 MB" },
            new() { Application = Catalog.Applications[1], State = PackItemState.Installing, Phase = PackItemPhase.Installing, Message = "Instalando…" },
            new() { Application = new("third", "Vendor.Third", "Tercera", "1"), State = PackItemState.VerificationPending,
                Phase = PackItemPhase.Verifying, NativeInstallSucceeded = true, Message = "Comprobando versión" }
        ] };
        var window = new MainWindow(_ => Task.FromResult(Catalog), (_, progress, _) =>
        {
            progress(new("applications", "Progreso", run.Copy())); return completed.Task;
        }, () => Task.CompletedTask);
        window.ShowStep(1); await window.LoadCatalog(); window.Confirm();
        Task installing = window.Install();
        Assert.Contains("1 descargando", window.StatusLabel.Text); Assert.Contains("1 instalando", window.StatusLabel.Text);
        Assert.Contains("1 verificando", window.StatusLabel.Text); Assert.Contains("5 / 10 MB", window.Activity.Text);
        Assert.True(window.Activity.Text.IndexOf("Aplicación del panel", StringComparison.Ordinal) < window.Activity.Text.IndexOf("Opcional", StringComparison.Ordinal));
        foreach (var item in run.Items.Take(2)) { item.State = PackItemState.Succeeded; item.Phase = null; }
        run.Items[2].Phase = null; run.Items[2].RequiresRebootBeforeRetry = true;
        completed.SetResult(new("result", "Pendiente de verificación", run, ExitCode: 2)); await installing;
        Assert.Contains("Verificación pendiente", window.ResultDetails.Text); Assert.False(window.LaunchCheck.IsChecked);
        Assert.Equal("Preparación parcial", window.ResultTitle.Text);
        Assert.Equal("INCOMPLETO", window.ResultStateBadge.Text);
        Assert.Equal("2", window.ResultVerifiedCount.Text);
        Assert.Equal("1", window.ResultPendingCount.Text);
        Assert.Equal("0", window.ResultFailedCount.Text);
        Assert.Equal("Tercera", Assert.IsType<List<MainWindow.ResultApplicationRow>>(window.ResultApplications.ItemsSource)[0].Name);
        window.Close();
    });
    [Fact]
    public Task Loading_is_immediate_and_async_selection_is_sent_with_pinned_versions() => Sta(async () =>
    {
        var loaded = new TaskCompletionSource<PackCatalog>(); EquipmentRequest? sent = null; int registration = 0;
        var window = new MainWindow(_ => loaded.Task, (request, progress, _) =>
        {
            sent = request; progress(new("phase", "Verificando…"));
            return Task.FromResult(new EquipmentEvent("result", "Preparado", ExitCode: 0));
        }, () => { registration++; return Task.CompletedTask; });
        Assert.True(window.PackCheck.IsChecked); Assert.False(window.KioskCheck.IsChecked);
        window.ShowStep(1); Task loading = window.LoadCatalog();
        Assert.Contains("Cargando", window.CatalogLabel.Text); Assert.False(window.NextButton.IsEnabled);
        loaded.SetResult(Catalog); await loading;
        var rows = Assert.IsType<List<MainWindow.ApplicationRow>>(window.ApplicationsList.ItemsSource);
        Assert.True(rows[0].Selected); Assert.False(rows[1].Selected);
        rows[0].Selected = false; rows[1].Selected = true;
        window.Confirm(); await window.Install();
        Assert.NotNull(sent); Assert.Equal(7, sent.CatalogRevision); Assert.Equal(new EquipmentSelection(new string('b', 32), "2.0"), Assert.Single(sent.Applications));
        Assert.Equal("Preparación completa", window.ResultTitle.Text); Assert.Equal(0, registration);
        Assert.Equal("COMPLETADO", window.ResultStateBadge.Text);
        Assert.Equal(Visibility.Collapsed, window.ResultStats.Visibility);
        window.Close();
    });
    [Fact]
    public Task Completed_pack_shows_verified_count_without_repeating_the_status() => Sta(async () =>
    {
        var run = new PackRun { Items = [new()
        {
            Application = Catalog.Applications[0], State = PackItemState.Succeeded,
            InstalledVersion = "3.2", Message = "Instalada y verificada."
        }] };
        var window = new MainWindow(_ => Task.FromResult(Catalog), (_, _, _) =>
            Task.FromResult(new EquipmentEvent("result", "Todas las aplicaciones instaladas y verificadas.", run, ExitCode: 0)),
            () => Task.CompletedTask);
        window.ShowStep(1); await window.LoadCatalog(); window.Confirm(); await window.Install();
        Assert.Equal("COMPLETADO", window.ResultStateBadge.Text);
        Assert.Equal("1", window.ResultVerifiedCount.Text);
        Assert.Equal("0", window.ResultPendingCount.Text);
        Assert.Equal("0", window.ResultFailedCount.Text);
        var row = Assert.Single(Assert.IsType<List<MainWindow.ResultApplicationRow>>(window.ResultApplications.ItemsSource));
        Assert.Equal("Verificada", row.Status);
        Assert.Equal("Versión 3.2", row.Detail);
        Assert.DoesNotContain("Instalada y verificada: Instalada", window.ResultDetails.Text);
        window.Close();
    });
    [Fact]
    public Task Latest_pack_defaults_to_partial_install_and_sends_ids_without_frozen_versions() => Sta(async () =>
    {
        var catalog = PackDefinition.FromCatalog(Catalog).ForExecution(); EquipmentRequest? sent = null;
        var run = new PackRun { Definition = catalog.Definition, Items = [new() { Application = Catalog.Applications[0], State = PackItemState.Failed, Message = "No disponible" }] };
        var window = new MainWindow(_ => Task.FromResult(catalog), (request, _, _) =>
        {
            sent = request; return Task.FromResult(new EquipmentEvent("result", "Pendiente", Run: run, ExitCode: 2));
        }, () => Task.CompletedTask);
        window.ShowStep(1); await window.LoadCatalog(); window.Confirm();
        Assert.True(window.PartialPackCheck.IsChecked); await window.Install();
        Assert.NotNull(sent); Assert.True(sent.ResolveLatest); Assert.True(sent.AllowPartialPack);
        Assert.Empty(Assert.Single(sent.Applications).Version);
        Assert.Contains("3.2", window.ResultDetails.Text); Assert.Contains("No disponible", window.ResultDetails.Text);
        Assert.Equal("ERROR", window.ResultStateBadge.Text);
        Assert.Equal("1", window.ResultFailedCount.Text);
        window.Close();
    });
    [Fact]
    public Task Empty_pack_is_explicit_and_kiosk_only_skips_catalogue_and_preserves_original_user_registration() => Sta(async () =>
    {
        int loads = 0, registration = 0;
        var window = new MainWindow(_ => { loads++; return Task.FromResult(new PackCatalog(1, [])); }, (request, _, _) =>
        {
            Assert.False(request.Pack); Assert.True(request.Kiosk); Assert.Empty(request.Applications);
            return Task.FromResult(new EquipmentEvent("result", "Preparado", ExitCode: 0, KioskVerified: true));
        }, () => { registration++; return Task.CompletedTask; });
        window.ShowStep(1); await window.LoadCatalog();
        Assert.Contains("vacío", window.CatalogLabel.Text); Assert.False(window.NextButton.IsEnabled);
        window.ShowStep(0); window.PackCheck.IsChecked = false; Assert.False(window.NextButton.IsEnabled);
        window.KioskCheck.IsChecked = true; Assert.True(window.NextButton.IsEnabled);
        window.Confirm(); await window.Install();
        Assert.Equal(1, loads); Assert.Equal(1, registration); Assert.True(window.LaunchCheck.IsChecked);
        Assert.Equal("Finalizar y abrir Kiosk", window.NextButton.Content);
        window.Close();
    });
    [Fact]
    public Task Cancellation_waits_for_worker_and_closing_cannot_kill_it() => Sta(async () =>
    {
        var finished = new TaskCompletionSource<EquipmentEvent>(); CancellationToken token = default;
        var window = new MainWindow(_ => Task.FromResult(Catalog), (_, _, ct) => { token = ct; return finished.Task; }, () => Task.CompletedTask);
        window.ShowStep(1); await window.LoadCatalog(); window.Confirm();
        Task install = window.Install(); window.Close();
        Assert.True(token.IsCancellationRequested); Assert.False(install.IsCompleted);
        finished.SetResult(new("result", "Cancelado", ExitCode: 2)); await install;
        Assert.Equal("Preparación detenida", window.ResultTitle.Text); window.Close();
    });
    [Fact]
    public Task Changed_catalogue_returns_to_review_without_registering_autostart() => Sta(async () =>
    {
        int loads = 0;
        var window = new MainWindow(_ => { loads++; return Task.FromResult(Catalog); }, (_, _, _) => Task.FromResult(new EquipmentEvent("review", "El pack cambió", ExitCode: 1)), () => throw new Exception("No debe registrar"));
        window.ShowStep(1); await window.LoadCatalog(); window.Confirm(); await window.Install();
        Assert.Equal(2, loads); Assert.Equal(Visibility.Visible, window.ApplicationsPanel.Visibility);
        Assert.Contains("cambió", window.CatalogLabel.Text); window.Close();
    });
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task Successful_kiosk_offers_launch_by_default_and_respects_unchecking(bool launch) => Sta(async () =>
    {
        int launches = 0;
        var window = new MainWindow(_ => throw new Exception("Solo Kiosk no consulta el pack"),
            (_, _, _) => Task.FromResult(new EquipmentEvent("result", "Instalado", ExitCode: 0, KioskVerified: true)),
            () => Task.CompletedTask, () => launches++);
        window.PackCheck.IsChecked = false; window.KioskCheck.IsChecked = true;
        window.Confirm(); await window.Install();
        Assert.True(window.LaunchCheck.IsChecked);
        Assert.Equal(Visibility.Visible, window.LaunchCheck.Visibility);
        Assert.Equal(0, launches);
        window.LaunchCheck.IsChecked = launch;
        Assert.Equal(launch ? "Finalizar y abrir Kiosk" : "Finalizar", window.NextButton.Content);
        window.Finish();
        Assert.Equal(launch ? 1 : 0, launches);
    });
    [Fact]
    public Task Combined_run_cannot_launch_kiosk_while_the_pack_worker_is_still_running() => Sta(async () =>
    {
        var completed = new TaskCompletionSource<EquipmentEvent>(); var calls = new List<string>();
        var window = new MainWindow(_ => Task.FromResult(Catalog), (request, progress, _) =>
        {
            Assert.True(request.Kiosk); Assert.True(request.Pack);
            calls.Add("worker-started");
            progress(new("phase", "Kiosk instalado; instalando pack"));
            return completed.Task;
        }, () => { calls.Add("autostart"); return Task.CompletedTask; }, () => calls.Add("launch"));
        window.KioskCheck.IsChecked = true;
        window.ShowStep(1); await window.LoadCatalog(); window.Confirm();
        Task installing = window.Install();
        Assert.False(installing.IsCompleted);
        Assert.Equal(Visibility.Collapsed, window.LaunchCheck.Visibility);
        window.LaunchCheck.IsChecked = true; window.Finish();
        Assert.Equal(new[] { "worker-started" }, calls);
        calls.Add("pack-finished");
        completed.SetResult(new("result", "Kiosk y pack verificados", ExitCode: 0, KioskVerified: true));
        await installing;
        Assert.True(window.LaunchCheck.IsChecked);
        Assert.Equal(new[] { "worker-started", "pack-finished", "autostart" }, calls);
        window.Finish();
        Assert.Equal(new[] { "worker-started", "pack-finished", "autostart", "launch" }, calls);
    });
    [Theory]
    [InlineData(true, 2, true, false)]
    [InlineData(true, 0, true, true)]
    [InlineData(true, 1, false, false)]
    [InlineData(false, 0, false, false)]
    public Task Incomplete_kiosk_reboot_failed_and_pack_only_runs_do_not_offer_or_launch_kiosk(bool selected, int exitCode, bool verified, bool kioskReboot) => Sta(async () =>
    {
        int launches = 0;
        var window = new MainWindow(_ => Task.FromResult(Catalog), (_, _, _) =>
            Task.FromResult(new EquipmentEvent("result", "Resultado", ExitCode: exitCode, KioskVerified: verified, RebootRequired: kioskReboot, KioskRebootRequired: kioskReboot)),
            () => Task.CompletedTask, () => launches++);
        window.KioskCheck.IsChecked = selected;
        window.ShowStep(1); await window.LoadCatalog(); window.Confirm(); await window.Install();
        Assert.Equal(Visibility.Collapsed, window.LaunchCheck.Visibility);
        Assert.False(window.LaunchCheck.IsChecked);
        if (exitCode == 0 && kioskReboot) Assert.Equal("REINICIO PENDIENTE", window.ResultStateBadge.Text);
        window.LaunchCheck.IsChecked = true; window.Finish();
        Assert.Equal(0, launches);
    });
    [Fact]
    public Task Verified_pack_with_pending_windows_reboot_shows_notice_without_application_failures() => Sta(async () =>
    {
        var run = new PackRun { Items = [new()
        {
            Application = Catalog.Applications[0], State = PackItemState.AlreadyInstalled,
            InstalledVersion = "3.2", RebootRequired = true
        }] };
        var window = new MainWindow(_ => Task.FromResult(Catalog), (_, progress, _) =>
        {
            progress(new("applications", "Verificadas", run));
            return Task.FromResult(new EquipmentEvent("result", run.Summary, run, ExitCode: 0, RebootRequired: run.RebootRequired));
        }, () => Task.CompletedTask);
        window.ShowStep(1); await window.LoadCatalog(); window.Confirm(); await window.Install();
        Assert.Equal("Preparación completa", window.ResultTitle.Text);
        Assert.Contains("reiniciar Windows ahora o más tarde", window.ResultLabel.Text);
        Assert.Equal(Visibility.Visible, window.RestartNowButton.Visibility);
        Assert.Equal("1", window.ResultVerifiedCount.Text);
        Assert.Equal("0", window.ResultPendingCount.Text);
        Assert.Equal("0", window.ResultFailedCount.Text);
        Assert.Equal(Visibility.Collapsed, window.LaunchCheck.Visibility);
        window.Close();
    });
    [Fact]
    public Task Pack_reboot_does_not_block_verified_kiosk_and_restart_requires_an_explicit_choice() => Sta(async () =>
    {
        int launches = 0, restarts = 0;
        var window = new MainWindow(_ => Task.FromResult(Catalog), (_, _, _) =>
            Task.FromResult(new EquipmentEvent("result", "Kiosk y pack verificados", ExitCode: 0,
                KioskVerified: true, RebootRequired: true)),
            () => Task.CompletedTask, () => launches++, () => restarts++);
        window.KioskCheck.IsChecked = true;
        window.ShowStep(1); await window.LoadCatalog(); window.Confirm(); await window.Install();
        Assert.Equal("Preparación completa", window.ResultTitle.Text);
        Assert.Equal(Visibility.Visible, window.LaunchCheck.Visibility);
        Assert.True(window.LaunchCheck.IsChecked);
        Assert.Equal(Visibility.Visible, window.RestartNowButton.Visibility);
        Assert.Equal(0, restarts);
        window.RestartNow(false);
        Assert.Equal(0, restarts);
        window.Finish();
        Assert.Equal(1, launches);
        Assert.Equal(0, restarts);
    });
    [Fact]
    public Task Restart_button_invokes_restart_only_after_confirmation() => Sta(async () =>
    {
        int restarts = 0;
        var window = new MainWindow(_ => Task.FromResult(Catalog), (_, _, _) =>
            Task.FromResult(new EquipmentEvent("result", "Verificado", ExitCode: 0, RebootRequired: true)),
            () => Task.CompletedTask, restart: () => restarts++);
        window.ShowStep(1); await window.LoadCatalog(); window.Confirm(); await window.Install();
        window.RestartNow(false);
        Assert.Equal(0, restarts);
        window.RestartNow(true);
        window.RestartNow(true);
        Assert.Equal(1, restarts);
        window.Close();
    });
    [Fact]
    public Task Cancelled_run_does_not_offer_launch_even_if_the_worker_returns_success() => Sta(async () =>
    {
        var completed = new TaskCompletionSource<EquipmentEvent>(); int launches = 0;
        var window = new MainWindow(_ => Task.FromResult(Catalog), (_, _, _) => completed.Task,
            () => Task.CompletedTask, () => launches++);
        window.PackCheck.IsChecked = false; window.KioskCheck.IsChecked = true; window.Confirm();
        Task installing = window.Install(); window.Close();
        completed.SetResult(new("result", "Instalado antes de cancelar", ExitCode: 0, KioskVerified: true));
        await installing;
        Assert.False(window.LaunchCheck.IsChecked);
        Assert.Equal(Visibility.Collapsed, window.LaunchCheck.Visibility);
        window.Finish(); Assert.Equal(0, launches);
    });
    [Fact]
    public Task Launch_failure_keeps_the_installation_result_and_allows_finishing_without_launch() => Sta(async () =>
    {
        var window = new MainWindow(_ => throw new Exception("No pack"), (_, _, _) =>
            Task.FromResult(new EquipmentEvent("result", "Instalado", ExitCode: 0, KioskVerified: true)),
            () => Task.CompletedTask, () => throw new Win32Exception("Launch failed"));
        window.PackCheck.IsChecked = false; window.KioskCheck.IsChecked = true; window.Confirm(); await window.Install();
        window.Finish();
        Assert.Contains("Kiosk está instalado", window.ResultLabel.Text);
        Assert.Contains("no se pudo abrir", window.ResultLabel.Text);
        Assert.False(window.LaunchCheck.IsChecked);
        Assert.Equal("Finalizar", window.NextButton.Content);
        window.Finish();
    });
    [Fact]
    public Task Failed_preflight_offers_available_apps_and_rechecks_original_selection() => Sta(async () =>
    {
        int attempts = 0;
        var run = new PackRun { Items = Catalog.Applications.Select(a => new PackItemResult
            { Application = a, State = a.SelectedByDefault ? PackItemState.Pending : PackItemState.Failed, Message = a.SelectedByDefault ? "Disponible" : "Versión no disponible" }).ToList() };
        var window = new MainWindow(_ => Task.FromResult(Catalog), (request, progress, _) =>
        {
            attempts++; Assert.Equal(2, request.Applications.Count);
            Assert.Equal(attempts == 2, request.AllowPartialPack);
            progress(new("applications", "Comprobado", run));
            return Task.FromResult(new EquipmentEvent("result", "Queda una aplicación pendiente", ExitCode: attempts == 1 ? 1 : 2));
        }, () => throw new Exception("No debe registrar Kiosk"));
        window.ShowStep(1); await window.LoadCatalog();
        var rows = Assert.IsType<List<MainWindow.ApplicationRow>>(window.ApplicationsList.ItemsSource); rows[1].Selected = true;
        window.Confirm(); window.PartialPackCheck.IsChecked = false; await window.Install();
        Assert.Equal(Visibility.Visible, window.InstallAvailableButton.Visibility);
        Assert.Equal("Preparación detenida", window.ResultTitle.Text);
        await window.ContinueAvailable();
        Assert.Equal(2, attempts); Assert.Equal("Preparación pendiente", window.ResultTitle.Text);
        Assert.Contains("Versión no disponible", window.ResultDetails.Text);
        Assert.Equal(Visibility.Collapsed, window.InstallAvailableButton.Visibility); window.Close();
    });
    [Fact]
    public Task Uncertain_native_state_never_offers_partial_install_button() => Sta(async () =>
    {
        var run = new PackRun { Items = [new() { Application = Catalog.Applications[0], State = PackItemState.Pending },
            new() { Application = Catalog.Applications[1], State = PackItemState.Failed, RequiresRebootBeforeRetry = true }] };
        var window = new MainWindow(_ => Task.FromResult(Catalog), (_, progress, _) =>
        {
            progress(new("applications", "Comprobado", run));
            return Task.FromResult(new EquipmentEvent("result", "Instalador activo", ExitCode: 1));
        }, () => Task.CompletedTask);
        window.ShowStep(1); await window.LoadCatalog(); window.Confirm(); await window.Install();
        Assert.Equal(Visibility.Collapsed, window.InstallAvailableButton.Visibility); window.Close();
    });
    [Theory]
    [InlineData("/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /COMPONENTS=kiosk", false, true)]
    [InlineData("/VERYSILENT /SUPPRESSMSGBOXES /NORESTART", true, false)]
    [InlineData("/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /COMPONENTS=pack,kiosk /RESUME", true, true)]
    public void Silent_mode_accepts_only_documented_components(string command, bool pack, bool kiosk)
    {
        var request = Program.ParseSilent(command.Split(' ')); Assert.NotNull(request);
        Assert.Equal(pack, request.Pack); Assert.Equal(kiosk, request.Kiosk);
        Assert.True(request.AllowPartialPack); Assert.True(request.ResolveLatest);
    }
    [Theory]
    [InlineData("/VERYSILENT")]
    [InlineData("/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /COMPONENTS=")]
    [InlineData("/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /COMPONENTS=pack /URL=https://evil.invalid")]
    [InlineData("/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /COMPONENTS=kiosk,kiosk")]
    public void Invalid_silent_arguments_are_rejected(string command) => Assert.Null(Program.ParseSilent(command.Split(' ')));
}
