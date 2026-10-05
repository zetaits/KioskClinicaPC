using System.Windows;
using System.Windows.Threading;
using Kiosk.EquipmentSetup;
using KioskClinicaPC.Core.Sync;
using KioskClinicaPC.Equipment;
using Xunit;

namespace Kiosk.EquipmentSetup.Tests;
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
        Assert.Equal("Equipo preparado", window.ResultTitle.Text); Assert.Equal(0, registration);
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
        Assert.Equal("Preparación incompleta", window.ResultTitle.Text); window.Close();
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
    [InlineData("/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /COMPONENTS=kiosk", false, true)]
    [InlineData("/VERYSILENT /SUPPRESSMSGBOXES /NORESTART", true, false)]
    [InlineData("/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /COMPONENTS=pack,kiosk /RESUME", true, true)]
    public void Silent_mode_accepts_only_documented_components(string command, bool pack, bool kiosk)
    {
        var request = Program.ParseSilent(command.Split(' ')); Assert.NotNull(request);
        Assert.Equal(pack, request.Pack); Assert.Equal(kiosk, request.Kiosk);
    }
    [Theory]
    [InlineData("/VERYSILENT")]
    [InlineData("/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /COMPONENTS=")]
    [InlineData("/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /COMPONENTS=pack /URL=https://evil.invalid")]
    [InlineData("/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /COMPONENTS=kiosk,kiosk")]
    public void Invalid_silent_arguments_are_rejected(string command) => Assert.Null(Program.ParseSilent(command.Split(' ')));
}
