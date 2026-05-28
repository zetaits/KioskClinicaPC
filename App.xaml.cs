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
        public static readonly string LogFilePath = Path.Combine(AppDataFolderPath, "logs", "log.txt");

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            Directory.CreateDirectory(AppDataFolderPath);
            Directory.CreateDirectory(Path.Combine(AppDataFolderPath, "logs"));

            Log.Logger = new LoggerConfiguration()
                .WriteTo.File(LogFilePath, rollingInterval: RollingInterval.Day)
                .CreateLogger();

            Log.Information("Aplicación iniciada.");

            KioskManager.Protect();

            this.DispatcherUnhandledException += App_DispatcherUnhandledException;

            if (!File.Exists(ConfigFilePath))
            {
                var configDialog = new FirstRunConfigWindow();
                bool? result = configDialog.ShowDialog();

                if (result == true)
                {
                    var config = configDialog.ConfigData;
                    string json = JsonConvert.SerializeObject(config, Formatting.Indented);
                    File.WriteAllText(ConfigFilePath, json);
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