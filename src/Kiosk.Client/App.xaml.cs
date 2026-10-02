using System;
using System.IO;
using System.Threading;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using KioskClinicaPC.Core;
using KioskClinicaPC.Core.Platform;
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
        public static readonly string ProvisioningFilePath = Path.Combine(AppContext.BaseDirectory, "KioskProvisioning.json");

        // Imágenes EMPAQUETADAS junto al .exe (Assets\Brands, Assets\SpecImages). El instalable las trae.
        public static readonly string BundledBrandsFolderPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Brands");
        public static readonly string BundledSpecImagesFolderPath = Path.Combine(AppContext.BaseDirectory, "Assets", "SpecImages");
        public static readonly string BundledThemeAssetsFolderPath = Path.Combine(AppContext.BaseDirectory, "Assets", "ThemeAssets");

        // Override opcional en %LOCALAPPDATA% (cambiar imágenes sin recompilar). Tiene prioridad si existe.
        public static readonly string BrandsFolderPath = Path.Combine(AppDataFolderPath, "Brands");
        public static readonly string SpecImagesFolderPath = Path.Combine(AppDataFolderPath, "SpecImages");
        public static readonly string RemoteAssetsFolderPath = Path.Combine(AppDataFolderPath, "RemoteAssets");
        public static readonly string RemoteBrandsFolderPath = Path.Combine(RemoteAssetsFolderPath, "Brands");
        public static readonly string RemoteSpecImagesFolderPath = Path.Combine(RemoteAssetsFolderPath, "SpecImages");
        public static readonly string RemoteThemeAssetsFolderPath = Path.Combine(RemoteAssetsFolderPath, "ThemeAssets");

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            // El instalador lo ejecuta como usuario interactivo antes de un reinicio pendiente.
            // Solo registra HKCU\Run: no abre ventanas ni activa la protección del kiosko.
            if (e.Args.Any(a => a.Equals("--register-autostart-only", StringComparison.OrdinalIgnoreCase)))
            {
                AutostartRegistration.Register();
                Shutdown();
                return;
            }
            Directory.CreateDirectory(AppDataFolderPath);
            Directory.CreateDirectory(Path.Combine(AppDataFolderPath, "logs"));
            Directory.CreateDirectory(BrandsFolderPath);
            Directory.CreateDirectory(SpecImagesFolderPath);
            Directory.CreateDirectory(RemoteBrandsFolderPath);
            Directory.CreateDirectory(RemoteSpecImagesFolderPath);
            Directory.CreateDirectory(RemoteThemeAssetsFolderPath);

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

            // Carga el perfil y aplica el aprovisionamiento del instalador antes de pedir la contraseña.
            // Ya no existe una clave compartida: perfiles nuevos y anteriores a la política actual deben
            // elegir una propia. Si el encargado cancela, OnExit libera la protección del escritorio.
            var settings = KioskSettings.Load(SettingsFilePath);
            bool seeded = settings.ApplyProvisioningIfMissingServer(ProvisioningFilePath);
            if (settings.RequiresPasswordSetup())
            {
                var passwordSetup = new PasswordSetupWindow(settings);
                if (passwordSetup.ShowDialog() != true)
                {
                    Log.Warning("Configuración de contraseña cancelada; el kiosko no se iniciará.");
                    Shutdown();
                    return;
                }
                seeded = true;
            }
            seeded |= settings.EnsureDeviceIdentitySeeded();
            if (seeded)
            {
                settings.Save(SettingsFilePath);
                Log.Information("KioskSettings actualizado (aprovisionamiento / política de contraseña / identidad de flota).");
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
            
            _services = BuildServiceProvider(settings);
            var mainWindow = _services.GetRequiredService<MainWindow>();
            Application.Current.MainWindow = mainWindow;
            mainWindow.Show();
        }

        /// <summary>Registra el grafo de dependencias. Un solo sitio para cablear implementaciones;
        /// sustituir una (p.ej. un repo de test) ya no exige tocar el ViewModel ni la ventana.</summary>
        private static IServiceProvider BuildServiceProvider(KioskSettings settings)
        {
            var services = new ServiceCollection();

            services.AddSingleton<IHardwareService, HardwareDiscoveryService>();
            // Repositorio de config: si hay servidor configurado, lee de él con caché/fallback local;
            // si no (ServerUrl vacío), es exactamente el JSON local de siempre. Factoría explícita
            // porque el contenedor no sabría resolver los ctores con parámetros (rutas de App).
            services.AddSingleton<IConfigRepository>(_ => new RemoteConfigRepository(
                settings.ServerUrl, settings.ServerApiKey, ConfigFilePath, HardwareFilePath));
            // Sincronización del bucle de atracción: si hay servidor, sigue el reloj maestro; si no,
            // queda deshabilitado y el kiosko rota los slides él solo (comportamiento previo).
            services.AddSingleton<ISyncClient>(_ => new SyncClient(settings.ServerUrl, settings.ServerApiKey));
            services.AddSingleton<IAssetSyncService>(_ => new AssetSyncService(
                settings.ServerUrl, settings.ServerApiKey, RemoteAssetsFolderPath));
            // Agente de flota: reporta estado al panel y ejecuta sus órdenes. No-op sin ServerUrl.
            services.AddSingleton(_ => new FleetClient(
                settings.ServerUrl, settings.ServerApiKey,
                settings.DeviceId ?? "", settings.DeviceName ?? Environment.MachineName, SettingsFilePath));
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
