using System;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using System.IO;
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

        public SettingsWindow(AppConfig detectedSpecs)
        {
            InitializeComponent();
            _detectedSpecs = detectedSpecs ?? new AppConfig();
        }
        
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Mouse.OverrideCursor = null; 
            LoadConfig();
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
            catch {} 
            
            if (_savedConfig == null)
            {
                _savedConfig = new AppConfig();
            }

            PriceTextBox.Text = _savedConfig.Price;
            DiscountedPriceTextBox.Text = _savedConfig.DiscountedPrice;

            CpuTextBox.Text = !string.IsNullOrWhiteSpace(_savedConfig.Cpu) ? _savedConfig.Cpu : _detectedSpecs.Cpu;
            CoresTextBox.Text = !string.IsNullOrWhiteSpace(_savedConfig.Cores) ? _savedConfig.Cores : _detectedSpecs.Cores;
            RamTextBox.Text = !string.IsNullOrWhiteSpace(_savedConfig.Ram) ? _savedConfig.Ram : _detectedSpecs.Ram;
            GpuTextBox.Text = !string.IsNullOrWhiteSpace(_savedConfig.Gpu) ? _savedConfig.Gpu : _detectedSpecs.Gpu;
            StorageTextBox.Text = !string.IsNullOrWhiteSpace(_savedConfig.Storage) ? _savedConfig.Storage : _detectedSpecs.Storage;
            ScreenTextBox.Text = !string.IsNullOrWhiteSpace(_savedConfig.Screen) ? _savedConfig.Screen : _detectedSpecs.Screen;
            OsTextBox.Text = !string.IsNullOrWhiteSpace(_savedConfig.Os) ? _savedConfig.Os : _detectedSpecs.Os;
            BatteryTextBox.Text = _savedConfig.Battery;
            WifiTextBox.Text = _savedConfig.Wifi;
            CameraTextBox.Text = _savedConfig.Camera;
            PortsTextBox.Text = _savedConfig.Ports;
            SkuTextBox.Text = _savedConfig.Sku;
            ShopAddressTextBox.Text = _savedConfig.ShopAddress;
            ShopServicesTextBox.Text = _savedConfig.ShopServices;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string GetOverride(string manualValue, string detectedValue)
                {
                    if (string.Equals(manualValue, detectedValue, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(manualValue))
                        return null;
                    return manualValue;
                }

                _savedConfig.Price = PriceTextBox.Text;
                _savedConfig.DiscountedPrice = DiscountedPriceTextBox.Text;
                
                _savedConfig.Cpu = GetOverride(CpuTextBox.Text, _detectedSpecs.Cpu);
                _savedConfig.Cores = GetOverride(CoresTextBox.Text, _detectedSpecs.Cores);
                _savedConfig.Ram = GetOverride(RamTextBox.Text, _detectedSpecs.Ram);
                _savedConfig.Gpu = GetOverride(GpuTextBox.Text, _detectedSpecs.Gpu);
                _savedConfig.Storage = GetOverride(StorageTextBox.Text, _detectedSpecs.Storage);
                _savedConfig.Screen = GetOverride(ScreenTextBox.Text, _detectedSpecs.Screen);
                _savedConfig.Os = GetOverride(OsTextBox.Text, _detectedSpecs.Os);
                _savedConfig.Battery = string.IsNullOrWhiteSpace(BatteryTextBox.Text) ? null : BatteryTextBox.Text;
                _savedConfig.Wifi = string.IsNullOrWhiteSpace(WifiTextBox.Text) ? null : WifiTextBox.Text;
                _savedConfig.Camera = string.IsNullOrWhiteSpace(CameraTextBox.Text) ? null : CameraTextBox.Text;
                _savedConfig.Ports = string.IsNullOrWhiteSpace(PortsTextBox.Text) ? null : PortsTextBox.Text;
                _savedConfig.Sku = string.IsNullOrWhiteSpace(SkuTextBox.Text) ? null : SkuTextBox.Text;
                _savedConfig.ShopAddress = string.IsNullOrWhiteSpace(ShopAddressTextBox.Text) ? null : ShopAddressTextBox.Text;
                _savedConfig.ShopServices = string.IsNullOrWhiteSpace(ShopServicesTextBox.Text) ? null : ShopServicesTextBox.Text;

                string json = JsonConvert.SerializeObject(_savedConfig, Formatting.Indented);
                File.WriteAllText(App.ConfigFilePath, json);
                
                this.DialogResult = true;
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al guardar el archivo de configuración:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                
                string currentExeName = Path.GetFileName(Assembly.GetEntryAssembly().Location);
                if (string.IsNullOrEmpty(currentExeName))
                {
                    MessageBox.Show("Error: No se pudo determinar el nombre del ejecutable actual.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                    string message = $"Desinstalación completada.\n";
                    if (deletedKeysCount > 0) message += $"- Se eliminaron {deletedKeysCount} entrada(s) de inicio automático.\n";
                    if (configFolderDeleted) message += $"- Se eliminó la carpeta de configuración.\n";
                    message += "La aplicación se cerrará ahora.";
                    
                    MessageBox.Show(message, "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);

                    if (this.Owner is MainWindow mainWindow)
                    {
                        mainWindow.ShutdownKiosk();
                    }
                }
                else
                {
                    MessageBox.Show("No se encontraron rastros de la aplicación (inicio automático o configuración).", 
                                    "Información", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al desinstalar: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}