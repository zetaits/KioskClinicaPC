using System;
using System.ComponentModel;
using System.Reflection;
using KioskClinicaPC.Core;
using KioskClinicaPC.Windows;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.Windows.Media.Imaging;

namespace KioskClinicaPC
{
    public partial class MainWindow : Window
    {
        private bool _isExitingSafely = false;
        private readonly KeyboardHook _hook;
        private AppConfig _detectedSpecs; 

        public MainWindow()
        {
            InitializeComponent();
            _hook = new KeyboardHook();
            _detectedSpecs = new AppConfig();
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
#if !DEBUG
            _hook.Start();
            RegisterInStartup();
#endif
            await LoadHardwareAndConfigAsync();
        }

        private async Task LoadHardwareAndConfigAsync()
        {
            AppConfig savedConfig = null;
            AppConfig lastDetectedSpecs = null;

            try
            {
                if (File.Exists(App.ConfigFilePath))
                {
                    string configJson = await File.ReadAllTextAsync(App.ConfigFilePath);
                    savedConfig = JsonConvert.DeserializeObject<AppConfig>(configJson);
                }
                if (File.Exists(App.HardwareFilePath))
                {
                    string hardwareJson = await File.ReadAllTextAsync(App.HardwareFilePath);
                    lastDetectedSpecs = JsonConvert.DeserializeObject<AppConfig>(hardwareJson);
                }
            }
            catch {}
            
            if (savedConfig == null) savedConfig = new AppConfig();
            if (lastDetectedSpecs == null) lastDetectedSpecs = new AppConfig();

            try
            {
                var info = new HardwareInfo();
                await Task.Run(() =>
                {
                    _detectedSpecs.Cpu = info.GetCpuName();
                    _detectedSpecs.Cores = info.GetCpuCores();
                    _detectedSpecs.Ram = info.GetRamDetails();
                    _detectedSpecs.Gpu = info.GetGpuName();
                    _detectedSpecs.Storage = info.GetStorageDetails();
                    _detectedSpecs.Screen = info.GetScreenResolution();
                    _detectedSpecs.Os = $"{info.GetOsName()} ({info.GetPcName()})";
                });
            }
            catch (Exception) {}

            bool newHardwareDetected = false;
            string newHardwareMessage = "Se ha detectado hardware nuevo o modificado:\n";

            Func<string, string, bool> IsDiff = (s1, s2) => 
                !string.IsNullOrWhiteSpace(s1) && 
                !string.Equals(s1, s2, StringComparison.OrdinalIgnoreCase);

            if (IsDiff(lastDetectedSpecs.Cpu, _detectedSpecs.Cpu))
            {
                newHardwareMessage += $"\n- CPU: (Antiguo: {lastDetectedSpecs.Cpu}, Nuevo: {_detectedSpecs.Cpu})";
                newHardwareDetected = true;
            }
            if (IsDiff(lastDetectedSpecs.Gpu, _detectedSpecs.Gpu))
            {
                newHardwareMessage += $"\n- GPU: (Antiguo: {lastDetectedSpecs.Gpu}, Nuevo: {_detectedSpecs.Gpu})";
                newHardwareDetected = true;
            }
            if (IsDiff(lastDetectedSpecs.Ram, _detectedSpecs.Ram))
            {
                newHardwareMessage += $"\n- RAM: (Antiguo: {lastDetectedSpecs.Ram}, Nuevo: {_detectedSpecs.Ram})";
                newHardwareDetected = true;
            }
            
            if (newHardwareDetected)
            {
                newHardwareMessage += "\n\n¿Desea borrar sus valores manuales y actualizar a los nuevos specs detectados?";
                MessageBoxResult result = MessageBox.Show(newHardwareMessage, "Nuevo Hardware Detectado", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    savedConfig.Cpu = null;
                    savedConfig.Cores = null;
                    savedConfig.Ram = null;
                    savedConfig.Gpu = null;
                    savedConfig.Storage = null;
                    savedConfig.Screen = null;
                    savedConfig.Os = null;
                    string configJson = JsonConvert.SerializeObject(savedConfig, Formatting.Indented);
                    await File.WriteAllTextAsync(App.ConfigFilePath, configJson);
                }
            }

            string hardwareJsonToSave = JsonConvert.SerializeObject(_detectedSpecs, Formatting.Indented);
            await File.WriteAllTextAsync(App.HardwareFilePath, hardwareJsonToSave);

            if (!string.IsNullOrWhiteSpace(savedConfig.DiscountedPrice))
            {
                OriginalPriceText.Text = savedConfig.Price;
                OriginalPriceText.Visibility = Visibility.Visible;
                DisplayPriceText.Text = savedConfig.DiscountedPrice;
            }
            else
            {
                OriginalPriceText.Visibility = Visibility.Collapsed;
                DisplayPriceText.Text = savedConfig.Price ?? "N/A";
            }

            PopulateSpecsPanel(savedConfig, _detectedSpecs);
        }

        private void PopulateSpecsPanel(AppConfig savedConfig, AppConfig detectedSpecs)
        {
            SpecsPanel.Children.Clear();

            // CPU Tile (Corrected)
            string cpuValue = !string.IsNullOrWhiteSpace(savedConfig.Cpu) ? savedConfig.Cpu : detectedSpecs.Cpu;
            string coresValue = !string.IsNullOrWhiteSpace(savedConfig.Cores) ? savedConfig.Cores : detectedSpecs.Cores;
            CreateSpecTile("PROCESADOR",
                $"{cpuValue} ({coresValue})",
                "El cerebro ultrarrápido para gaming y creación de contenido.",
                "POTENTE", "cpu_icon.png");

            // RAM Tile
            CreateSpecTile("MEMORIA RAM",
                !string.IsNullOrWhiteSpace(savedConfig.Ram) ? savedConfig.Ram : detectedSpecs.Ram,
                "Ideal para multitarea y juegos fluidos.",
                "RÁPIDA", "ram_icon.png");

            // GPU Tile
            CreateSpecTile("TARJETA GRÁFICA",
                !string.IsNullOrWhiteSpace(savedConfig.Gpu) ? savedConfig.Gpu : detectedSpecs.Gpu,
                "Gráficos impresionantes y alto rendimiento en juegos.",
                "GAMING", "gpu_icon.png");

            // Storage Tile
            CreateSpecTile("ALMACENAMIENTO",
                !string.IsNullOrWhiteSpace(savedConfig.Storage) ? savedConfig.Storage : detectedSpecs.Storage,
                "Arranque y carga de aplicaciones en segundos.",
                "VELOZ", "storage_icon.png");

            // Motherboard, PSU, Case Tiles (Now configurable)
            CreateSpecTile("PLACA BASE",
                !string.IsNullOrWhiteSpace(savedConfig.Motherboard) ? savedConfig.Motherboard : "No especificado",
                "La base estable para todos tus componentes.",
                "CONFIABLE", "motherboard_icon.png");

            CreateSpecTile("FUENTE DE PODER",
                !string.IsNullOrWhiteSpace(savedConfig.PowerSupply) ? savedConfig.PowerSupply : "No especificado",
                "Energía eficiente y segura para tu equipo.",
                "EFICIENTE", "psu_icon.png");

            CreateSpecTile("GABINETE",
                !string.IsNullOrWhiteSpace(savedConfig.Case) ? savedConfig.Case : "No especificado",
                "Diseño elegante con excelente flujo de aire.",
                "ESTILO", "case_icon.png");

            // Screen and OS Tiles (Re-integrated)
            CreateSpecTile("PANTALLA",
                !string.IsNullOrWhiteSpace(savedConfig.Screen) ? savedConfig.Screen : detectedSpecs.Screen,
                "Claridad y colores vibrantes para una inmersión total.",
                "NÍTIDA", "screen_icon.png");

            CreateSpecTile("SISTEMA OPERATIVO",
                !string.IsNullOrWhiteSpace(savedConfig.Os) ? savedConfig.Os : detectedSpecs.Os.Split('(')[0].Trim(),
                "Windows: El estándar para compatibilidad y rendimiento.",
                "MODERNO", "os_icon.png");
        }

        private void CreateSpecTile(string label, string value, string benefit, string tag, string iconName)
        {
            var tile = new SpecTile
            {
                Label = label,
                Value = value,
                Benefit = benefit,
                Tag = tag,
                IconSource = new BitmapImage(new Uri($"pack://application:,,,/KioskClinicaPC;component/Assets/{iconName}", UriKind.Absolute))
            };
            SpecsPanel.Children.Add(tile);
        }


        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (!_isExitingSafely)
            {
                e.Cancel = true;
            }
            else
            {
                _hook.Stop();
            }
        }

        public void ShutdownKiosk()
        {
            _isExitingSafely = true;
            Application.Current.Shutdown();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            bool ctrlPressed = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
            bool shiftPressed = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);

            bool kPressed = e.Key == Key.K;
            if (ctrlPressed && shiftPressed && kPressed)
            {
                ShutdownKiosk();
            }

            bool sPressed = e.Key == Key.S;
            if (ctrlPressed && shiftPressed && sPressed)
            {
                OpenSettingsDialog(); 
            }
        }

        private void SettingsClickArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 3)
            {
                OpenSettingsDialog();
            }
        }

        private void RegisterInStartup()
        {
            try
            {
                const string appName = "KioskHardwareDisplay";
                const string registryKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
                string exePath = Assembly.GetExecutingAssembly().Location;
                if (string.IsNullOrEmpty(exePath)) return;

                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(registryKeyPath, true))
                {
                    if (key == null) return;
                    object currentValue = key.GetValue(appName);
                    if (currentValue == null || currentValue.ToString() != $"\"{exePath}\"")
                        key.SetValue(appName, $"\"{exePath}\"");
                }
            }
            catch (Exception)
            {
            }
        }

        private async void OpenSettingsDialog()
        {

            PasswordDialog dialog = new PasswordDialog();
            dialog.Owner = this;
            bool? result = dialog.ShowDialog();

            if (result == true)
            {
                SettingsWindow settings = new SettingsWindow(_detectedSpecs);
                settings.Owner = this;
                bool? saveResult = settings.ShowDialog();
                
                if (saveResult == true)
                {
                    await LoadHardwareAndConfigAsync();
                }
            }
        }
    }
}