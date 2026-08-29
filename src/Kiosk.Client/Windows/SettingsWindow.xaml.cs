using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using System.IO;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using KioskClinicaPC.Core;
using KioskClinicaPC;
using KioskClinicaPC.Services;
using Newtonsoft.Json;
using Serilog;

namespace KioskClinicaPC.Windows
{
    public partial class SettingsWindow : Window
    {
        private AppConfig _savedConfig = new AppConfig();
        private readonly AppConfig _detectedSpecs;
        private KioskSettings _settings = new KioskSettings();

        // Con servidor de contenido, lo compartido (slides/textos/marketing) lo manda el panel: la edición
        // local de esos campos se oculta para no editar algo que el siguiente merge del servidor pisaría.
        private bool _serverManaged;

        // Slides del Attract editables (añadir/eliminar/editar). Se ligan al ItemsControl por nombre.
        private readonly ObservableCollection<AttractSlide> _attractSlides = new ObservableCollection<AttractSlide>();        // De ocasión
        private readonly ObservableCollection<AttractSlide> _attractSlidesNew = new ObservableCollection<AttractSlide>();     // Nuevo

        /// <summary>Indica a MainWindow que debe entrar en modo edición al cerrar.</summary>
        public bool LaunchEditMode { get; private set; }

        public SettingsWindow(AppConfig detectedSpecs)
        {
            InitializeComponent();
            _detectedSpecs = detectedSpecs ?? new AppConfig();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Mouse.OverrideCursor = null;
            LoadConfig();
            LoadSettings();
        }

        private void LoadConfig()
        {
            try
            {
                if (File.Exists(App.ConfigFilePath))
                {
                    string json = File.ReadAllText(App.ConfigFilePath);
                    _savedConfig = JsonConvert.DeserializeObject<AppConfig>(json) ?? new AppConfig();
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "KioskConfig.json dañado al abrir ajustes.");
                KioskDialog.Alert(this, "Configuración",
                    "El archivo de configuración está dañado y no se pudo leer. Se muestran valores por defecto; al guardar se sobrescribirá.",
                    danger: true);
            }

            if (_savedConfig == null)
                _savedConfig = new AppConfig();

            PriceTextBox.Text = _savedConfig.Price;
            DiscountedPriceTextBox.Text = _savedConfig.DiscountedPrice;

            CpuTextBox.Text = ConfigMerger.Display(_savedConfig.Cpu, _detectedSpecs.Cpu);
            CoresTextBox.Text = ConfigMerger.Display(_savedConfig.Cores, _detectedSpecs.Cores);
            RamTextBox.Text = ConfigMerger.Display(_savedConfig.Ram, _detectedSpecs.Ram);
            GpuTextBox.Text = ConfigMerger.Display(_savedConfig.Gpu, _detectedSpecs.Gpu);
            StorageTextBox.Text = ConfigMerger.Display(_savedConfig.Storage, _detectedSpecs.Storage);
            ScreenTextBox.Text = ConfigMerger.Display(_savedConfig.Screen, _detectedSpecs.Screen);
            // OS normalizado (sin "(NOMBRE-PC)"), igual que en el modo edición, para que el override
            // se compare y guarde con el mismo criterio en ambos sitios.
            OsTextBox.Text = ConfigMerger.Display(_savedConfig.Os, ConfigMerger.NormalizeOs(_detectedSpecs.Os));
            BatteryTextBox.Text = _savedConfig.Battery;
            WifiTextBox.Text = _savedConfig.Wifi;
            CameraTextBox.Text = _savedConfig.Camera;
            PortsTextBox.Text = _savedConfig.Ports;
            SkuTextBox.Text = _savedConfig.Sku;

            // Detalle técnico (StatStrip): override manual o lo detectado por WMI.
            RamDetailTextBox.Text = ConfigMerger.Display(_savedConfig.RamDetail, _detectedSpecs.RamDetail);
            StorageDetailTextBox.Text = ConfigMerger.Display(_savedConfig.StorageDetail, _detectedSpecs.StorageDetail);
            ScreenDetailTextBox.Text = ConfigMerger.Display(_savedConfig.ScreenDetail, _detectedSpecs.ScreenDetail);
            BatteryDetailTextBox.Text = ConfigMerger.Display(_savedConfig.BatteryDetail, _detectedSpecs.BatteryDetail);
            GpuDetailTextBox.Text = ConfigMerger.Display(_savedConfig.GpuDetail, _detectedSpecs.GpuDetail);
            WifiDetailTextBox.Text = ConfigMerger.Display(_savedConfig.WifiDetail, _detectedSpecs.WifiDetail);
            CameraDetailTextBox.Text = ConfigMerger.Display(_savedConfig.CameraDetail, _detectedSpecs.CameraDetail);
            PortsDetailTextBox.Text = ConfigMerger.Display(_savedConfig.PortsDetail, _detectedSpecs.PortsDetail);
            OsDetailTextBox.Text = ConfigMerger.Display(_savedConfig.OsDetail, _detectedSpecs.OsDetail);

            // Estado del equipo: determina la garantía mostrada en la ficha.
            if (Warranty.IsNew(_savedConfig.Condition)) NewRadio.IsChecked = true;
            else UsedRadio.IsChecked = true;

            // Slides del Attract: copia editable por estado (clona para no mutar _savedConfig hasta Guardar).
            _attractSlides.Clear();
            foreach (var s in _savedConfig.AttractSlides ?? new List<AttractSlide>())
                _attractSlides.Add(new AttractSlide { Eyebrow = s.Eyebrow, Title1 = s.Title1, Title2 = s.Title2, Subtitle = s.Subtitle });
            SlidesItemsControl.ItemsSource = _attractSlides;

            _attractSlidesNew.Clear();
            foreach (var s in _savedConfig.AttractSlidesNew ?? new List<AttractSlide>())
                _attractSlidesNew.Add(new AttractSlide { Eyebrow = s.Eyebrow, Title1 = s.Title1, Title2 = s.Title2, Subtitle = s.Subtitle });
            SlidesNewItemsControl.ItemsSource = _attractSlidesNew;
        }

        private void AddSlide_Click(object sender, RoutedEventArgs e)
        {
            _attractSlides.Add(new AttractSlide { Eyebrow = "OCASIÓN", Title1 = "TITULAR", Title2 = "SECUNDARIO", Subtitle = "Subtítulo" });
        }

        private void AddSlideNew_Click(object sender, RoutedEventArgs e)
        {
            _attractSlidesNew.Add(new AttractSlide { Eyebrow = "NUEVO", Title1 = "TITULAR", Title2 = "SECUNDARIO", Subtitle = "Subtítulo" });
        }

        // Muestra en tiempo real solo el set de textos del estado seleccionado.
        private void ConditionRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (NewSlidesPanel == null || UsedSlidesPanel == null) return; // aún cargando la vista
            if (_serverManaged) return; // slides gestionados por el servidor: paneles ocultos, no re-mostrar
            bool isNew = NewRadio.IsChecked == true;
            NewSlidesPanel.Visibility = isNew ? Visibility.Visible : Visibility.Collapsed;
            UsedSlidesPanel.Visibility = isNew ? Visibility.Collapsed : Visibility.Visible;
        }

        private void RemoveSlide_Click(object sender, RoutedEventArgs e)
        {
            // El botón puede pertenecer a cualquiera de los dos sets; quita del que lo contenga.
            if (sender is FrameworkElement fe && fe.Tag is AttractSlide slide)
            {
                if (!_attractSlides.Remove(slide))
                    _attractSlidesNew.Remove(slide);
            }
        }

        // Descarta slides completamente vacíos y clona el resto conservando el orden.
        private static List<AttractSlide> CleanSlides(IEnumerable<AttractSlide> slides) => slides
            .Where(s => !(string.IsNullOrWhiteSpace(s.Eyebrow) && string.IsNullOrWhiteSpace(s.Title1)
                          && string.IsNullOrWhiteSpace(s.Title2) && string.IsNullOrWhiteSpace(s.Subtitle)))
            .Select(s => new AttractSlide { Eyebrow = s.Eyebrow, Title1 = s.Title1, Title2 = s.Title2, Subtitle = s.Subtitle })
            .ToList();

        private void LoadSettings()
        {
            _settings = KioskSettings.Load(App.SettingsFilePath);
            InactivityTextBox.Text = _settings.InactivitySeconds.ToString(CultureInfo.InvariantCulture);
            AutoScanTextBox.Text = _settings.AutoScanSeconds.ToString(CultureInfo.InvariantCulture);
            SlideIntervalTextBox.Text = _settings.SlideIntervalSeconds.ToString(CultureInfo.InvariantCulture);
            ServerUrlTextBox.Text = _settings.ServerUrl;
            ServerApiKeyTextBox.Text = _settings.ServerApiKey;
            DeviceNameTextBox.Text = _settings.DeviceName;

            _serverManaged = !string.IsNullOrWhiteSpace(_settings.ServerUrl);
            ApplyServerManagedUi();
        }

        /// <summary>Oculta la edición de contenido compartido (slides + modo edición libre) cuando el kiosko
        /// recibe ese contenido del servidor. El precio/specs/condición (por-máquina) siguen editables.</summary>
        private void ApplyServerManagedUi()
        {
            if (!_serverManaged) return;

            UsedSlidesPanel.Visibility = Visibility.Collapsed;
            NewSlidesPanel.Visibility = Visibility.Collapsed;
            SlidesServerNote.Visibility = Visibility.Visible;

            EditModeButton.Visibility = Visibility.Collapsed;
            EditModeServerNote.Visibility = Visibility.Visible;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!TrySaveSettings()) return;

                _savedConfig.Price = PriceTextBox.Text;
                _savedConfig.DiscountedPrice = DiscountedPriceTextBox.Text;

                // Mismas reglas que el modo edición (ConfigMerger): antes esto divergía (OS sin
                // normalizar y placeholders sin filtrar) según se guardara aquí o en edición.
                _savedConfig.Cpu = ConfigMerger.Override(CpuTextBox.Text, _detectedSpecs.Cpu);
                _savedConfig.Cores = ConfigMerger.Override(CoresTextBox.Text, _detectedSpecs.Cores);
                _savedConfig.Ram = ConfigMerger.Override(RamTextBox.Text, _detectedSpecs.Ram);
                _savedConfig.Gpu = ConfigMerger.Override(GpuTextBox.Text, _detectedSpecs.Gpu);
                _savedConfig.Storage = ConfigMerger.Override(StorageTextBox.Text, _detectedSpecs.Storage);
                _savedConfig.Screen = ConfigMerger.Override(ScreenTextBox.Text, _detectedSpecs.Screen);
                _savedConfig.Os = ConfigMerger.Override(OsTextBox.Text, ConfigMerger.NormalizeOs(_detectedSpecs.Os));
                _savedConfig.Battery = ConfigMerger.NoPlaceholder(BatteryTextBox.Text);
                _savedConfig.Wifi = ConfigMerger.NoPlaceholder(WifiTextBox.Text);
                _savedConfig.Camera = ConfigMerger.NoPlaceholder(CameraTextBox.Text);
                _savedConfig.Ports = ConfigMerger.NoPlaceholder(PortsTextBox.Text);
                _savedConfig.Sku = string.IsNullOrWhiteSpace(SkuTextBox.Text) ? null : SkuTextBox.Text;

                // Detalle técnico: guardar override solo si difiere de lo detectado.
                _savedConfig.RamDetail = ConfigMerger.Override(RamDetailTextBox.Text, _detectedSpecs.RamDetail);
                _savedConfig.StorageDetail = ConfigMerger.Override(StorageDetailTextBox.Text, _detectedSpecs.StorageDetail);
                _savedConfig.ScreenDetail = ConfigMerger.Override(ScreenDetailTextBox.Text, _detectedSpecs.ScreenDetail);
                _savedConfig.BatteryDetail = ConfigMerger.Override(BatteryDetailTextBox.Text, _detectedSpecs.BatteryDetail);
                _savedConfig.GpuDetail = ConfigMerger.Override(GpuDetailTextBox.Text, _detectedSpecs.GpuDetail);
                _savedConfig.WifiDetail = ConfigMerger.Override(WifiDetailTextBox.Text, _detectedSpecs.WifiDetail);
                _savedConfig.CameraDetail = ConfigMerger.Override(CameraDetailTextBox.Text, _detectedSpecs.CameraDetail);
                _savedConfig.PortsDetail = ConfigMerger.Override(PortsDetailTextBox.Text, _detectedSpecs.PortsDetail);
                _savedConfig.OsDetail = ConfigMerger.Override(OsDetailTextBox.Text, _detectedSpecs.OsDetail);

                _savedConfig.Condition = NewRadio.IsChecked == true ? Warranty.New : Warranty.Used;

                // Slides del Attract (un set por estado): descarta los vacíos; conserva el resto en orden.
                // Con servidor, los slides son compartidos (paneles ocultos): no los toca el guardado local.
                if (!_serverManaged)
                {
                    _savedConfig.AttractSlides = CleanSlides(_attractSlides);
                    _savedConfig.AttractSlidesNew = CleanSlides(_attractSlidesNew);
                }

                string json = JsonConvert.SerializeObject(_savedConfig, Formatting.Indented);
                JsonStore.WriteAtomic(App.ConfigFilePath, json);

                this.DialogResult = true;
                this.Close();
            }
            catch (Exception ex)
            {
                KioskDialog.Alert(this, "Error", $"No se pudo guardar la configuración.\n{ex.Message}", danger: true);
            }
        }

        private bool TrySaveSettings()
        {
            _settings.InactivitySeconds = Math.Max(5, ParseInt(InactivityTextBox.Text, _settings.InactivitySeconds));
            _settings.AutoScanSeconds = Math.Max(3, ParseInt(AutoScanTextBox.Text, _settings.AutoScanSeconds));
            _settings.SlideIntervalSeconds = Math.Max(1, ParseDouble(SlideIntervalTextBox.Text, _settings.SlideIntervalSeconds));

            // Servidor de contenido: en blanco = modo local. El cambio se aplica al reiniciar la app
            // (el repositorio/sync se construyen en el arranque a partir de estos valores).
            _settings.ServerUrl = Blank(ServerUrlTextBox.Text);
            _settings.ServerApiKey = Blank(ServerApiKeyTextBox.Text);
            // El nombre nunca queda vacío: si se borra, vuelve al hostname. El cambio local se aplica al
            // reiniciar la app (el agente de flota lee la identidad en el arranque).
            _settings.DeviceName = Blank(DeviceNameTextBox.Text) ?? Environment.MachineName;

            if (!string.IsNullOrEmpty(NewPasswordBox.Password))
            {
                if (!PasswordService.Verify(CurrentPasswordBox.Password, _settings.PasswordHash))
                {
                    KioskDialog.Alert(this, "Seguridad", "La contraseña actual no es correcta.", danger: true);
                    return false;
                }
                if (NewPasswordBox.Password != ConfirmPasswordBox.Password)
                {
                    KioskDialog.Alert(this, "Seguridad", "La nueva contraseña y su confirmación no coinciden.", danger: true);
                    return false;
                }
                _settings.PasswordHash = PasswordService.Hash(NewPasswordBox.Password);
            }

            _settings.Save(App.SettingsFilePath);
            return true;
        }

        private static int ParseInt(string text, int fallback)
            => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : fallback;

        private static double ParseDouble(string text, double fallback)
            => double.TryParse((text ?? "").Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : fallback;

        private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

        /// <summary>Comprueba que el servidor responde: GET {url}/health. No guarda nada; solo informa.</summary>
        private async void TestConnectionButton_Click(object sender, RoutedEventArgs e)
        {
            string? url = Blank(ServerUrlTextBox.Text);
            if (url == null)
            {
                SetConnStatus("Sin URL: el kiosko funcionará en modo local.", ok: null);
                return;
            }

            TestConnectionButton.IsEnabled = false;
            SetConnStatus("Probando…", ok: null);
            try
            {
                using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                string? apiKey = Blank(ServerApiKeyTextBox.Text);
                if (apiKey != null) http.DefaultRequestHeaders.Add("X-Api-Key", apiKey);

                var resp = await http.GetAsync(url.TrimEnd('/') + "/health");
                if (resp.IsSuccessStatusCode)
                    SetConnStatus("✓ Conectado. El servidor responde.", ok: true);
                else
                    SetConnStatus($"✗ El servidor respondió {(int)resp.StatusCode}.", ok: false);
            }
            catch (Exception ex)
            {
                SetConnStatus($"✗ No se pudo conectar: {ex.Message}", ok: false);
            }
            finally
            {
                TestConnectionButton.IsEnabled = true;
            }
        }

        private void SetConnStatus(string text, bool? ok)
        {
            ConnectionStatusText.Text = text;
            ConnectionStatusText.Foreground = ok switch
            {
                true => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x46, 0xC9, 0x8B)),
                false => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE7, 0x6A, 0x6A)),
                null => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x8A, 0x82, 0xA8)),
            };
        }

        private void EditModeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_serverManaged) return; // botón oculto con servidor; guard por si acaso
            LaunchEditMode = true;
            this.Close();
        }

        private void ExitKiosk_Click(object sender, RoutedEventArgs e)
        {
            if (KioskDialog.Confirm(this, "Salir del kiosko", "¿Salir del kiosko y cerrar la aplicación?", "Salir", danger: true))
                (this.Owner as MainWindow)?.ShutdownKiosk();
        }

        private void RestartApp_Click(object sender, RoutedEventArgs e)
        {
            if (!KioskDialog.Confirm(this, "Reiniciar app", "¿Reiniciar la aplicación?", "Reiniciar")) return;
            try
            {
                string? exe = Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(exe))
                    Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                KioskDialog.Alert(this, "Error", $"No se pudo reiniciar: {ex.Message}", danger: true);
                return;
            }
            (this.Owner as MainWindow)?.ShutdownKiosk();
        }

        private void RestartPc_Click(object sender, RoutedEventArgs e)
        {
            if (!KioskDialog.Confirm(this, "Reiniciar PC", "¿Reiniciar el equipo ahora?", "Reiniciar", danger: true)) return;
            SystemPower.Reboot();
            (this.Owner as MainWindow)?.ShutdownKiosk();
        }

        private void ShutdownPc_Click(object sender, RoutedEventArgs e)
        {
            if (!KioskDialog.Confirm(this, "Apagar PC", "¿Apagar el equipo ahora?", "Apagar", danger: true)) return;
            SystemPower.Shutdown();
            (this.Owner as MainWindow)?.ShutdownKiosk();
        }

        private void RestoreDefaults_Click(object sender, RoutedEventArgs e)
        {
            if (!KioskDialog.Confirm(this, "Restaurar valores", "¿Restaurar todos los textos y valores a los de fábrica?\nSe perderán los cambios guardados.", "Restaurar", danger: true)) return;
            try
            {
                if (File.Exists(App.ConfigFilePath)) File.Delete(App.ConfigFilePath);
                if (File.Exists(App.HardwareFilePath)) File.Delete(App.HardwareFilePath);

                _settings.InactivitySeconds = 90;
                _settings.AutoScanSeconds = 18;
                _settings.SlideIntervalSeconds = 5.2;
                _settings.Save(App.SettingsFilePath);

                this.DialogResult = true;
                this.Close();
            }
            catch (Exception ex)
            {
                KioskDialog.Alert(this, "Error", $"Error al restaurar: {ex.Message}", danger: true);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private async void CheckUpdateButton_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            string? originalContent = btn?.Content as string;
            if (btn != null) { btn.IsEnabled = false; btn.Content = "Comprobando…"; }
            try
            {
                var result = await UpdateService.CheckAndStageAsync();
                switch (result.Outcome)
                {
                    case UpdateService.UpdateOutcome.UpToDate:
                        KioskDialog.Alert(this, "Actualizaciones",
                            $"Ya tienes la última versión instalada (v{UpdateService.CurrentVersion}).");
                        break;
                    case UpdateService.UpdateOutcome.Staged:
                        KioskDialog.Alert(this, "Actualización lista",
                            $"Se descargó la versión {result.LatestVersion} y se aplicará automáticamente esta " +
                            "madrugada (el equipo se reiniciará). Para aplicarla ahora, reinicia el PC.");
                        break;
                    default:
                        KioskDialog.Alert(this, "Actualizaciones",
                            "No se pudo comprobar si hay actualizaciones. Revisa la conexión a internet.", danger: true);
                        break;
                }
            }
            finally
            {
                if (btn != null) { btn.IsEnabled = true; btn.Content = originalContent; }
            }
        }

        private async void UninstallButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!KioskDialog.Confirm(this, "Desinstalar Kiosk",
                    "Se eliminarán la aplicación, su entrada de Windows, el servicio de instalaciones, " +
                    "el inicio automático y la configuración local. ¿Continuar?", "Desinstalar", danger: true))
                    return;

                UninstallButton.IsEnabled = false;
                var response = await InstallerAgentClient.UninstallKioskAsync(App.AppDataFolderPath);
                if (response.Accepted)
                {
                    KioskManager.Release();
                    Log.CloseAndFlush();
                    if (this.Owner is MainWindow installedKiosk)
                        installedKiosk.ShutdownKiosk();
                    return;
                }

                // Compatibilidad con instalaciones anteriores al servicio de mantenimiento.
                if (TryGetRegisteredUninstaller(out string? uninstaller, out bool registrationExists, out string? registrationError))
                {
                    Process.Start(new ProcessStartInfo(uninstaller!)
                    {
                        UseShellExecute = true, Verb = "runas",
                        Arguments = "/SILENT /SUPPRESSMSGBOXES /NORESTART /LOG"
                    });
                    KioskManager.Release();
                    Log.CloseAndFlush();
                    if (this.Owner is MainWindow legacyKiosk) legacyKiosk.ShutdownKiosk();
                    return;
                }

                if (registrationExists)
                {
                    KioskDialog.Alert(this, "Instalación dañada",
                        (registrationError ?? response.Error ?? "No se encontró un desinstalador válido.") +
                        "\n\nReinstala Kiosk para reparar el desinstalador y vuelve a intentarlo.", danger: true);
                    return;
                }

                // Build portable/desarrollo: borra datos, pero no afirma haber desinstalado una app de Windows.
                using (RegistryKey? run = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
                    run?.DeleteValue("KioskHardwareDisplay", false);
                if (Directory.Exists(App.AppDataFolderPath))
                {
                    KioskManager.Release();
                    Log.CloseAndFlush();
                    Directory.Delete(App.AppDataFolderPath, true);
                }
                KioskDialog.Alert(this, "Datos locales eliminados",
                    "Esta copia no estaba registrada como aplicación instalada. Se eliminaron sus datos locales y se cerrará ahora.");
                if (this.Owner is MainWindow portableKiosk) portableKiosk.ShutdownKiosk();
            }
            catch (Exception ex)
            {
                KioskDialog.Alert(this, "Error", $"Error al desinstalar: {ex.Message}", danger: true);
            }
            finally { UninstallButton.IsEnabled = true; }
        }

        private static bool TryGetRegisteredUninstaller(out string? path, out bool registrationExists, out string? error)
        {
            const string keyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{A7E3C9F1-2B4D-4E6A-9C8B-1F0D5E2A6B33}_is1";
            path = null; error = null;
            using RegistryKey hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using RegistryKey? key = hklm.OpenSubKey(keyPath);
            registrationExists = key != null;
            if (key == null) return false;
            string? command = key.GetValue("UninstallString") as string;
            if (string.IsNullOrWhiteSpace(command)) { error = "La entrada de Windows no contiene UninstallString."; return false; }
            command = command.Trim();
            if (command.StartsWith('"'))
            {
                int end = command.IndexOf('"', 1);
                if (end > 1) path = command[1..end];
            }
            else
            {
                int end = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                if (end >= 0) path = command[..(end + 4)];
            }
            string? appRoot = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName);
            if (path == null || appRoot == null) { error = "UninstallString no es válido."; return false; }
            path = Path.GetFullPath(path);
            string root = Path.GetFullPath(appRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) ||
                !System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(path), @"^unins\d{3}\.exe$", System.Text.RegularExpressions.RegexOptions.IgnoreCase) ||
                !File.Exists(path))
            {
                error = "El desinstalador registrado no existe o no pertenece a esta instalación.";
                return false;
            }
            return true;
        }
    }
}
