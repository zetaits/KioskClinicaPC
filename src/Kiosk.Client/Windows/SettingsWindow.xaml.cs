using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using System.IO;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
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
                _savedConfig.AttractSlides = CleanSlides(_attractSlides);
                _savedConfig.AttractSlidesNew = CleanSlides(_attractSlidesNew);

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

        private void EditModeButton_Click(object sender, RoutedEventArgs e)
        {
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
            RunShutdown("/r /t 0");
        }

        private void ShutdownPc_Click(object sender, RoutedEventArgs e)
        {
            if (!KioskDialog.Confirm(this, "Apagar PC", "¿Apagar el equipo ahora?", "Apagar", danger: true)) return;
            RunShutdown("/s /t 0");
        }

        private void RunShutdown(string args)
        {
            try
            {
                // Ruta completa (no "shutdown" por PATH): evita que un shutdown.exe plantado en el
                // PATH se ejecute en su lugar.
                string shutdownExe = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.System), "shutdown.exe");
                Process.Start(new ProcessStartInfo(shutdownExe, args) { CreateNoWindow = true, UseShellExecute = false });
                (this.Owner as MainWindow)?.ShutdownKiosk();
            }
            catch (Exception ex)
            {
                KioskDialog.Alert(this, "Error", $"No se pudo ejecutar la operación: {ex.Message}", danger: true);
            }
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

        private void UninstallButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                const string registryKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

                if (!KioskDialog.Confirm(this, "Desinstalar", "Se eliminará la configuración y el inicio automático, y la app se cerrará. ¿Continuar?", "Desinstalar", danger: true))
                    return;

                // Si la app está instalada vía Inno, hay un unins000.exe junto al ejecutable.
                // Lanzarlo = desinstalación real del sistema (borra Program Files, accesos directos,
                // entrada de Agregar/quitar programas, y vía [Code] del .iss: autostart + config).
                // La app corre como asInvoker y no puede borrar Program Files por sí misma.
                string? exeDir = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName);
                string? uninstaller = exeDir != null ? Path.Combine(exeDir, "unins000.exe") : null;
                if (uninstaller != null && File.Exists(uninstaller))
                {
                    KioskManager.Release(); // restaura taskbar/Task Manager antes de soltar el control
                    Log.CloseAndFlush();
                    Process.Start(new ProcessStartInfo(uninstaller) { UseShellExecute = true });
                    // Cierra el kiosko para liberar los archivos que el desinstalador va a borrar.
                    if (this.Owner is MainWindow installedKiosk)
                        installedKiosk.ShutdownKiosk();
                    return;
                }

                // Fallback (build portable / desarrollo, sin instalador): limpieza manual.
                string? currentExeName = Path.GetFileName(Assembly.GetEntryAssembly()?.Location);
                if (string.IsNullOrEmpty(currentExeName))
                {
                    KioskDialog.Alert(this, "Error", "No se pudo determinar el nombre del ejecutable actual.", danger: true);
                    return;
                }

                int deletedKeysCount = 0;
                var keysToDelete = new List<string>();
                bool configFolderDeleted = false;

                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(registryKeyPath, true))
                {
                    if (key != null)
                    {
                        foreach (string valueName in key.GetValueNames())
                        {
                            string? path = key.GetValue(valueName) as string;
                            if (!string.IsNullOrEmpty(path))
                            {
                                string cleanPath = path.Trim('\"');
                                if (Path.GetFileName(cleanPath).Equals(currentExeName, StringComparison.OrdinalIgnoreCase))
                                {
                                    keysToDelete.Add(valueName);
                                }
                            }
                        }
                        foreach (string keyName in keysToDelete)
                        {
                            key.DeleteValue(keyName);
                            deletedKeysCount++;
                        }
                    }
                }

                if (Directory.Exists(App.AppDataFolderPath))
                {
                    KioskManager.Release(); // Restore system before deleting files
                    Log.CloseAndFlush(); // Release the Serilog log file handle before deleting the folder
                    Directory.Delete(App.AppDataFolderPath, true);
                    configFolderDeleted = true;
                }

                if (deletedKeysCount > 0 || configFolderDeleted)
                {
                    string message = "Desinstalación completada.\n";
                    if (deletedKeysCount > 0) message += $"- Se eliminaron {deletedKeysCount} entrada(s) de inicio automático.\n";
                    if (configFolderDeleted) message += "- Se eliminó la carpeta de configuración.\n";
                    message += "La aplicación se cerrará ahora.";

                    KioskDialog.Alert(this, "Desinstalación completada", message);

                    if (this.Owner is MainWindow mainWindow)
                    {
                        mainWindow.ShutdownKiosk();
                    }
                }
                else
                {
                    KioskDialog.Alert(this, "Información", "No se encontraron rastros de la aplicación (inicio automático o configuración).");
                }
            }
            catch (Exception ex)
            {
                KioskDialog.Alert(this, "Error", $"Error al desinstalar: {ex.Message}", danger: true);
            }
        }
    }
}
