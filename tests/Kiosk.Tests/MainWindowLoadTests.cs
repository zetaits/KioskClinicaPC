using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using KioskClinicaPC.Core;
using KioskClinicaPC.Models;
using KioskClinicaPC.Services;
using KioskClinicaPC.ViewModels;
using Xunit;

namespace KioskClinicaPC.Tests;

public sealed class MainWindowLoadTests
{
    [Fact]
    public async Task Main_window_and_its_templates_load_after_price_configuration_without_starting_the_kiosk()
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            App? app = null;
            try
            {
                // Initialize resources only: no Run/Show, OnStartup, keyboard hook or desktop protection.
                app = new App(); app.InitializeComponent();
                var services = new MemoryServices();
                var vm = new MainViewModel(services, services, services) { DisplayConfig = new AppConfig { Price = "799" } };
                var spec = new SpecItem { Id = "cpu", Label = "Procesador", Value = "Intel Core", IconData = "M 0,0 L 10,10" };
                vm.Specs.Add(spec); vm.ActiveSpec = spec; vm.SelectedSpec = spec;
                using var sync = new SyncClient(null, null);
                using var fleet = new FleetClient(null, null, "ui-test", "ui-test", "unused-settings.json");
                var window = new MainWindow(vm, sync, services, fleet);
                Assert.Same(vm, window.DataContext);
                Assert.Equal("799", vm.DisplayConfig.Price);
                Assert.False(window.IsVisible);
                LoadTemplates(window, new HashSet<FrameworkTemplate>());
                completed.SetResult();
            }
            catch (Exception error) { completed.SetException(error); }
            finally { app?.Shutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.IsBackground = true; thread.Start();
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    private static void LoadTemplates(DependencyObject root, HashSet<FrameworkTemplate> seen)
    {
        if (root is ItemsControl { ItemTemplate: { } template } && seen.Add(template))
            LoadTemplates(template.LoadContent(), seen);
        foreach (object child in LogicalTreeHelper.GetChildren(root))
            if (child is DependencyObject dependency) LoadTemplates(dependency, seen);
    }

    private sealed class MemoryServices : IHardwareService, IConfigRepository, IDialogService, IAssetSyncService
    {
        public Task<AppConfig> GetHardwareInfoAsync() => Task.FromResult(new AppConfig());
        public Task<ConfigLoadResult> LoadConfigAsync() => Task.FromResult(new ConfigLoadResult(new AppConfig(), false, false));
        public Task<AppConfig> LoadLastHardwareAsync() => Task.FromResult(new AppConfig());
        public void SaveConfig(AppConfig config) { }
        public void SaveHardware(AppConfig hardware) { }
        public void Warn(string message, string title) => throw new InvalidOperationException("UI smoke test must not open dialogs.");
        public bool Confirm(string message, string title) => throw new InvalidOperationException("UI smoke test must not open dialogs.");
        public Task<bool> SyncAsync(CancellationToken ct = default) => Task.FromResult(false);
    }
}
