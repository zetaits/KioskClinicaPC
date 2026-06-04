using System;
using System.IO;
using System.Windows;
using KioskClinicaPC.Core; 
using KioskClinicaPC.Windows; 
using Newtonsoft.Json; 
using Serilog;

namespace KioskClinicaPC
{
    public partial class App : Application
    {
        public static readonly string AppDataFolderName = "KioskClinicaPC";
        public static readonly string AppDataFolderPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
            AppDataFolderName
        );
        
        public static readonly string ConfigFilePath = Path.Combine(AppDataFolderPath, "KioskConfig.json");
        public static readonly string HardwareFilePath = Path.Combine(AppDataFolderPath, "KioskHardware.json");
        public static readonly string SettingsFilePath = Path.Combine(AppDataFolderPath, "KioskSettings.json");
        public static readonly string LogFilePath = Path.Combine(AppDataFolderPath, "logs", "log.txt");

        // Imágenes EMPAQUETADAS junto al .exe (Assets\Brands, Assets\SpecImages). El instalable las trae.
        public static readonly string BundledBrandsFolderPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Brands");
        public static readonly string BundledSpecImagesFolderPath = Path.Combine(AppContext.BaseDirectory, "Assets", "SpecImages");

        // Override opcional en %LOCALAPPDATA% (cambiar imágenes sin recompilar). Tiene prioridad si existe.
        public static readonly string BrandsFolderPath = Path.Combine(AppDataFolderPath, "Brands");
        public static readonly string SpecImagesFolderPath = Path.Combine(AppDataFolderPath, "SpecImages");

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            Directory.CreateDirectory(AppDataFolderPath);
            Directory.CreateDirectory(Path.Combine(AppDataFolderPath, "logs"));
            Directory.CreateDirectory(BrandsFolderPath);
            Directory.CreateDirectory(SpecImagesFolderPath);

            Log.Logger = new LoggerConfiguration()
                .WriteTo.File(LogFilePath, rollingInterval: RollingInterval.Day)
                .CreateLogger();

            Log.Information("Aplicación iniciada.");

            // Registra los manejadores ANTES de Protect(): si Protect() o el sembrado de
            // ajustes lanzan, Release() debe ejecutarse igualmente para no dejar el escritorio
            // bloqueado (taskbar oculta + Task Manager deshabilitado).
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;
            // Excepciones fatales fuera del hilo de UI: restaura el escritorio antes de morir.
            AppDomain.CurrentDomain.UnhandledException += (_, __) => KioskManager.Release();

            // Auto-cura: si una sesión anterior murió sin restaurar (kill, BSOD, corte de luz),
            // la taskbar quedó oculta y DisableTaskMgr puesto. Limpia ese estado heredado ANTES
            // de re-proteger, para que cada arranque parta de un estado conocido.
            KioskManager.Release();
            KioskManager.Protect();

            // Asegura que exista KioskSettings.json con contraseña sembrada.
            var settings = KioskSettings.Load(SettingsFilePath);
            if (settings.EnsurePasswordSeeded())
            {
                settings.Save(SettingsFilePath);
                Log.Information("KioskSettings creado con contraseña por defecto.");
            }

            if (!File.Exists(ConfigFilePath))
            {
                var configDialog = new FirstRunConfigWindow();
                bool? result = configDialog.ShowDialog();

                if (result == true)
                {
                    var config = configDialog.ConfigData;
                    string json = JsonConvert.SerializeObject(config, Formatting.Indented);
                    JsonStore.WriteAtomic(ConfigFilePath, json);
                    Log.Information("Configuración inicial creada.");
                }
                else
                {
                    Log.Warning("Configuración inicial cancelada. Cerrando aplicación.");
                    Application.Current.Shutdown();
                    return; 
                }
            }
            
            var mainWindow = new MainWindow();
            Application.Current.MainWindow = mainWindow; 
            mainWindow.Show();
        }

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            Log.Fatal(e.Exception, "Se ha producido una excepción no controlada.");
            KioskManager.Release();
            e.Handled = true;
            MessageBox.Show("Se ha producido un error inesperado. Por favor, contacte con el soporte técnico.", "Error Crítico", MessageBoxButton.OK, MessageBoxImage.Error);
            Application.Current.Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            KioskManager.Release();
            Log.Information("Aplicación cerrada con código {ExitCode}.", e.ApplicationExitCode);
            Log.CloseAndFlush();
            base.OnExit(e);
        }
    }
}