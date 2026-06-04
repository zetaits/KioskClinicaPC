using System;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using System.IO;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Collections.Generic;
using KioskClinicaPC.Core;
using KioskClinicaPC;
using Newtonsoft.Json;

namespace KioskClinicaPC.Windows
{
    public partial class SettingsWindow : Window
    {
        private AppConfig _savedConfig;
        private readonly AppConfig _detectedSpecs;
        private KioskSettings _settings = new KioskSettings();

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
                    _savedConfig = JsonConvert.DeserializeObject<AppConfig>(json);
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
        }

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
                string exe = Process.GetCurrentProcess().MainModule?.FileName;
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
                Process.Start(new ProcessStartInfo("shutdown", args) { CreateNoWindow = true, UseShellExecute = false });
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

        private void UninstallButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                const string registryKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

                if (!KioskDialog.Confirm(this, "Desinstalar", "Se eliminará la configuración y el inicio automático, y la app se cerrará. ¿Continuar?", "Desinstalar", danger: true))
                    return;

                string currentExeName = Path.GetFileName(Assembly.GetEntryAssembly().Location);
                if (string.IsNullOrEmpty(currentExeName))
                {
                    KioskDialog.Alert(this, "Error", "No se pudo determinar el nombre del ejecutable actual.", danger: true);
                    return;
                }

                int deletedKeysCount = 0;
                var keysToDelete = new List<string>();
                bool configFolderDeleted = false;

                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(registryKeyPath, true))
                {
                    if (key != null)
                    {
                        foreach (string valueName in key.GetValueNames())
                        {
                            string path = key.GetValue(valueName) as string;
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
