using System;
using System.IO;
using System.Threading;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using KioskClinicaPC.Core;
using KioskClinicaPC.Services;
using KioskClinicaPC.ViewModels;
using KioskClinicaPC.Windows;
using Newtonsoft.Json;
using Serilog;

namespace KioskClinicaPC
{
    public partial class App : Application
    {
        /// <summary>Contenedor DI raíz. Construye el grafo de servicios (hardware, persistencia,
        /// diálogos), el ViewModel y la ventana principal en un único punto.</summary>
        private IServiceProvider? _services;

        // Mantiene viva la referencia: si el GC la recoge, el mutex se libera y la guardia falla.
        private static Mutex? _singleInstanceMutex;
        // Solo restauramos el escritorio al salir si esta instancia llegó a protegerlo. Evita que
        // una segunda instancia (que se autocierra) desactive la protección de la primera en OnExit.
        private bool _protected = false;

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

            // Instancia única: dos kioscos a la vez pelean por Topmost y dejan el estado de
            // bloqueo inconsistente (uno protege, el otro libera). Si ya hay una, cierra esta
            // SIN tocar la protección (no hemos protegido aún → _protected sigue false).
            _singleInstanceMutex = new Mutex(true, @"Global\KioskClinicaPC_SingleInstance", out bool createdNew);
            if (!createdNew)
            {
                // Esta instancia NO es dueña del mutex: liberar/Dispose sin ReleaseMutex y anular
                // para que OnExit no intente liberarlo (lanzaría por no ser propietaria).
                _singleInstanceMutex.Dispose();
                _singleInstanceMutex = null;
                Log.Warning("Ya hay una instancia del kiosko en ejecución. Cerrando esta.");
                Shutdown();
                return;
            }

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
            _protected = true;

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

                if (result == true && configDialog.ConfigData is { } config)
                {
                    config.SchemaVersion = AppConfig.CurrentSchemaVersion; // nace en el esquema actual
                    string json = JsonConvert.SerializeObject(config, Formatting.Indented);
                    JsonStore.WriteAtomic(ConfigFilePath, json);
                    Log.Information("Configuración inicial creada.");
                }
                else
                {
                    // Antes se cerraba la app: en un kiosko autostart desatendido, un cancel dejaba
                    // pantalla negra. Siembra configuración por defecto y arranca igualmente
                    // (MainViewModel completa marketing/slides; el hardware se detecta solo).
                    Log.Warning("Configuración inicial cancelada; se siembra configuración por defecto.");
                    var fallback = new AppConfig { SchemaVersion = AppConfig.CurrentSchemaVersion };
                    JsonStore.WriteAtomic(ConfigFilePath, JsonConvert.SerializeObject(fallback, Formatting.Indented));
                }
            }
            
            _services = BuildServiceProvider();
            var mainWindow = _services.GetRequiredService<MainWindow>();
            Application.Current.MainWindow = mainWindow;
            mainWindow.Show();
        }

        /// <summary>Registra el grafo de dependencias. Un solo sitio para cablear implementaciones;
        /// sustituir una (p.ej. un repo de test) ya no exige tocar el ViewModel ni la ventana.</summary>
        private static IServiceProvider BuildServiceProvider()
        {
            var services = new ServiceCollection();

            services.AddSingleton<IHardwareService, HardwareDiscoveryService>();
            // Factoría explícita: JsonConfigRepository tiene un ctor (string, string) que el contenedor
            // no sabría resolver; forzamos el sin-parámetros (rutas de App).
            services.AddSingleton<IConfigRepository>(_ => new JsonConfigRepository());
            services.AddSingleton<IDialogService, MessageBoxDialogService>();
            services.AddSingleton<MainViewModel>();
            services.AddSingleton<MainWindow>();

            return services.BuildServiceProvider();
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
            // Solo libera si esta instancia protegió (una segunda instancia que se autocierra no
            // debe desactivar la protección de la primera).
            if (_protected) KioskManager.Release();
            try { _singleInstanceMutex?.ReleaseMutex(); } catch { /* no propietaria/abandonada */ }
            _singleInstanceMutex?.Dispose();
            (_services as IDisposable)?.Dispose();
            Log.Information("Aplicación cerrada con código {ExitCode}.", e.ApplicationExitCode);
            Log.CloseAndFlush();
            base.OnExit(e);
        }
    }
}