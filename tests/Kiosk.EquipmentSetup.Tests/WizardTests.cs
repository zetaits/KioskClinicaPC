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
        Assert.Equal(1, loads); Assert.Equal(1, registration); Assert.False(window.LaunchCheck.IsChecked);
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
        Assert.Equal("Preparación parcial", window.ResultTitle.Text); window.Close();
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
        await window.ContinueAvailable();
        Assert.Equal(2, attempts); Assert.Equal("Preparación parcial", window.ResultTitle.Text);
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
