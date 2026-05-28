using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using KioskClinicaPC.Core;
using KioskClinicaPC.Services;
using KioskClinicaPC.Models;
using Newtonsoft.Json;
using Serilog;

namespace KioskClinicaPC.ViewModels
{
    public class MainViewModel : BaseViewModel
    {
        private readonly IHardwareService _hardwareService;
        private AppConfig _detectedSpecs = new AppConfig();
        public AppConfig DetectedSpecs => _detectedSpecs;
        private AppConfig _savedConfig = new AppConfig();
        
        private int _currentScreen = 0;
        public int CurrentScreen
        {
            get => _currentScreen;
            set => SetProperty(ref _currentScreen, value);
        }

        private string _currentScreenName = "RESUMEN DEL EQUIPO";
        public string CurrentScreenName
        {
            get => _currentScreenName;
            set => SetProperty(ref _currentScreenName, value);
        }

        private AppConfig _displayConfig;
        public AppConfig DisplayConfig
        {
            get => _displayConfig;
            set
            {
                if (SetProperty(ref _displayConfig, value))
                {
                    OnPropertyChanged(nameof(FormattedPrice));
                    OnPropertyChanged(nameof(FormattedOriginalPrice));
                    OnPropertyChanged(nameof(FormattedDiscount));
                    OnPropertyChanged(nameof(FormattedMonthly));
                    OnPropertyChanged(nameof(HasDiscount));
                }
            }
        }

        public ObservableCollection<SpecItem> Specs { get; } = new ObservableCollection<SpecItem>();
        public ObservableCollection<ScanLogItem> ScanLogs { get; } = new ObservableCollection<ScanLogItem>();

        private SpecItem _selectedSpec;
        public SpecItem SelectedSpec
        {
            get => _selectedSpec;
            set
            {
                if (SetProperty(ref _selectedSpec, value))
                {
                    if (CurrentScreen == 3 && value != null)
                        CurrentScreenName = $"DETALLE · {value.Label?.ToUpperInvariant()}";
                }
            }
        }

        public string FormattedPrice => FormatPrice(DisplayConfig?.DiscountedPrice ?? DisplayConfig?.Price);
        public string FormattedOriginalPrice => FormatPrice(DisplayConfig?.Price);
        public string FormattedDiscount => CalculateDiscount();
        public string FormattedMonthly => CalculateMonthly();
        public bool HasDiscount => !string.IsNullOrWhiteSpace(DisplayConfig?.DiscountedPrice);

        // Attract screen bindings
        private string _attractEyebrow = "CLINICAPC · ANÁLISIS EN VIVO";
        public string AttractEyebrow { get => _attractEyebrow; set => SetProperty(ref _attractEyebrow, value); }

        private string _attractTitle1 = "ESTE EQUIPO";
        public string AttractTitle1 { get => _attractTitle1; set => SetProperty(ref _attractTitle1, value); }

        private string _attractTitle2 = "TE ESTÁ OBSERVANDO.";
        public string AttractTitle2 { get => _attractTitle2; set => SetProperty(ref _attractTitle2, value); }

        private string _attractSubtitle = "Conéctate · escanea · descubre cada componente en 30 segundos.";
        public string AttractSubtitle { get => _attractSubtitle; set => SetProperty(ref _attractSubtitle, value); }

        private string _currentTimeString = "";
        public string CurrentTimeString { get => _currentTimeString; set => SetProperty(ref _currentTimeString, value); }

        public MainViewModel(IHardwareService hardwareService)
        {
            _hardwareService = hardwareService;
            StartClock();
        }

        private void StartClock()
        {
            CurrentTimeString = DateTime.Now.ToString("HH:mm");
            var now = DateTime.Now;
            var msToNextMinute = 60_000 - (now.Second * 1000 + now.Millisecond);
            var bootstrap = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(msToNextMinute) };
            bootstrap.Tick += (s, e) =>
            {
                bootstrap.Stop();
                CurrentTimeString = DateTime.Now.ToString("HH:mm");
                var clockTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
                clockTimer.Tick += (_, __) => CurrentTimeString = DateTime.Now.ToString("HH:mm");
                clockTimer.Start();
            };
            bootstrap.Start();
        }

        public async Task LoadHardwareAndConfigAsync()
        {
            AppConfig savedConfig = null;
            AppConfig lastDetectedSpecs = null;

            try
            {
                if (File.Exists(App.ConfigFilePath))
                    savedConfig = JsonConvert.DeserializeObject<AppConfig>(await File.ReadAllTextAsync(App.ConfigFilePath));
                if (File.Exists(App.HardwareFilePath))
                    lastDetectedSpecs = JsonConvert.DeserializeObject<AppConfig>(await File.ReadAllTextAsync(App.HardwareFilePath));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error al cargar la configuración o el hardware guardado.");
            }

            savedConfig ??= new AppConfig();
            lastDetectedSpecs ??= new AppConfig();

            try
            {
                _detectedSpecs = await _hardwareService.GetHardwareInfoAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error durante la detección de hardware en vivo.");
            }

            bool newHardwareDetected = false;
            Func<string, string, bool> IsDiff = (s1, s2) => !string.IsNullOrWhiteSpace(s1) && !string.Equals(s1, s2, StringComparison.OrdinalIgnoreCase);

            if (IsDiff(lastDetectedSpecs.Cpu, _detectedSpecs.Cpu) || IsDiff(lastDetectedSpecs.Gpu, _detectedSpecs.Gpu) || IsDiff(lastDetectedSpecs.Ram, _detectedSpecs.Ram))
            {
                newHardwareDetected = true;
            }

            if (newHardwareDetected)
            {
                MessageBoxResult result = MessageBox.Show("Se ha detectado hardware nuevo.\n¿Actualizar valores detectados?", "Hardware", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    savedConfig.Cpu = savedConfig.Cores = savedConfig.Ram = savedConfig.Gpu = savedConfig.Storage = savedConfig.Screen = savedConfig.Os = null;
                    await File.WriteAllTextAsync(App.ConfigFilePath, JsonConvert.SerializeObject(savedConfig, Formatting.Indented));
                }
            }

            await File.WriteAllTextAsync(App.HardwareFilePath, JsonConvert.SerializeObject(_detectedSpecs, Formatting.Indented));

            _savedConfig = savedConfig;

            if (_savedConfig.MarketingData == null || _savedConfig.MarketingData.Count == 0)
            {
                _savedConfig.MarketingData = GetDefaultMarketingData();
                try
                {
                    await File.WriteAllTextAsync(App.ConfigFilePath, JsonConvert.SerializeObject(_savedConfig, Formatting.Indented));
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error al guardar la configuración con datos de marketing por defecto.");
                }
            }
            
            DisplayConfig = new AppConfig
            {
                Cpu = !string.IsNullOrWhiteSpace(_savedConfig.Cpu) ? _savedConfig.Cpu : (_detectedSpecs.Cpu),
                Cores = !string.IsNullOrWhiteSpace(_savedConfig.Cores) ? _savedConfig.Cores : (_detectedSpecs.Cores),
                Ram = !string.IsNullOrWhiteSpace(_savedConfig.Ram) ? _savedConfig.Ram : (_detectedSpecs.Ram),
                Gpu = !string.IsNullOrWhiteSpace(_savedConfig.Gpu) ? _savedConfig.Gpu : (_detectedSpecs.Gpu),
                Storage = !string.IsNullOrWhiteSpace(_savedConfig.Storage) ? _savedConfig.Storage : (_detectedSpecs.Storage),
                Screen = !string.IsNullOrWhiteSpace(_savedConfig.Screen) ? _savedConfig.Screen : (_detectedSpecs.Screen),
                Os = !string.IsNullOrWhiteSpace(_savedConfig.Os) ? _savedConfig.Os : (_detectedSpecs.Os?.Split('(')[0].Trim()),
                Battery = !string.IsNullOrWhiteSpace(_savedConfig.Battery) ? _savedConfig.Battery : "No detectada",
                Wifi = !string.IsNullOrWhiteSpace(_savedConfig.Wifi) ? _savedConfig.Wifi : "No detectado",
                Camera = !string.IsNullOrWhiteSpace(_savedConfig.Camera) ? _savedConfig.Camera : "No detectada",
                Ports = !string.IsNullOrWhiteSpace(_savedConfig.Ports) ? _savedConfig.Ports : "No detectado",
                Price = _savedConfig.Price,
                DiscountedPrice = _savedConfig.DiscountedPrice,
                ChassisName = _savedConfig.ChassisName ?? "ASUS ROG",
                ModelName = _savedConfig.ModelName ?? "STRIX G16",
                Family = _savedConfig.Family ?? "Gaming Laptop · 16″",
                Sku = _savedConfig.Sku ?? "G614JV-N3170W",
                ShopAddress = !string.IsNullOrWhiteSpace(_savedConfig.ShopAddress) ? _savedConfig.ShopAddress : "Calle Goya 12 · Madrid",
                ShopServices = !string.IsNullOrWhiteSpace(_savedConfig.ShopServices) ? _savedConfig.ShopServices : "Asistencia · Cambio · Reparación · Reacondicionado",
                MarketingData = _savedConfig.MarketingData
            };

            PopulateSpecs();
        }

        private void PopulateSpecs()
        {
            Specs.Clear();
            var cyanBrush = (System.Windows.Media.SolidColorBrush)Application.Current.FindResource("CyanBrush");
            var cyanColor = (System.Windows.Media.Color)Application.Current.FindResource("CyanColor");
            var magentaBrush = (System.Windows.Media.SolidColorBrush)Application.Current.FindResource("MagentaBrush");
            var magentaColor = (System.Windows.Media.Color)Application.Current.FindResource("MagentaColor");
            var limeBrush = (System.Windows.Media.SolidColorBrush)Application.Current.FindResource("OkBrush");
            var limeColor = (System.Windows.Media.Color)Application.Current.FindResource("OkColor");
            var amberBrush = (System.Windows.Media.SolidColorBrush)Application.Current.FindResource("AmberBrush");
            var amberColor = (System.Windows.Media.Color)Application.Current.FindResource("AmberColor");

            const string IconCpu = "M 9,9 L 23,9 L 23,23 L 9,23 Z M 13,13 L 19,13 L 19,19 L 13,19 Z M 12,9 L 12,6 M 15,9 L 15,6 M 18,9 L 18,6 M 21,9 L 21,6 M 12,23 L 12,26 M 15,23 L 15,26 M 18,23 L 18,26 M 21,23 L 21,26 M 9,12 L 6,12 M 9,15 L 6,15 M 9,18 L 6,18 M 9,21 L 6,21 M 23,12 L 26,12 M 23,15 L 26,15 M 23,18 L 26,18 M 23,21 L 26,21";
            const string IconGpu = "M 4,11 L 28,11 L 28,22 L 4,22 Z M 11,14 A 2.6,2.6 0 1,0 11,19 A 2.6,2.6 0 1,0 11,14 M 21,14 A 2.6,2.6 0 1,0 21,19 A 2.6,2.6 0 1,0 21,14 M 4,22 L 2,25 M 28,22 L 30,25";
            const string IconRam = "M 4,10 L 28,10 L 28,20 L 4,20 Z M 9,10 L 9,20 M 14,10 L 14,20 M 19,10 L 19,20 M 24,10 L 24,20 M 6,22 L 6,25 M 26,22 L 26,25";
            const string IconStorage = "M 5,6 L 27,6 L 27,12 L 5,12 Z M 5,14 L 27,14 L 27,20 L 5,20 Z M 5,22 L 27,22 L 27,28 L 5,28 Z M 23,9 A 0.6,0.6 0 1,0 23,9.01 M 23,17 A 0.6,0.6 0 1,0 23,17.01 M 23,25 A 0.6,0.6 0 1,0 23,25.01";
            const string IconScreen = "M 3,6 L 29,6 L 29,22 L 3,22 Z M 11,26 L 21,26 M 16,22 L 16,26";
            const string IconBattery = "M 3,10 L 27,10 L 27,22 L 3,22 Z M 27,13 L 29,13 L 29,19 L 27,19 Z M 6,13 L 20,13 L 20,19 L 6,19 Z";
            const string IconWifi = "M 5,13 A 17,17 0 0,1 27,13 M 9,17 A 11,11 0 0,1 23,17 M 13,21 A 5,5 0 0,1 19,21 M 16,25 A 1,1 0 1,0 16,25.01";
            const string IconCamera = "M 3,8 L 29,8 L 29,24 L 3,24 Z M 16,11.5 A 4.5,4.5 0 1,0 16,20.5 A 4.5,4.5 0 1,0 16,11.5 M 20,5 L 26,5 L 26,8 L 20,8 Z";
            const string IconPorts = "M 3,13 L 12,13 L 12,19 L 3,19 Z M 14,11 L 20,11 L 20,21 L 14,21 Z M 22,14 L 29,14 L 29,18 L 22,18 Z";
            const string IconOs = "M 4,5 L 15,5 L 15,15 L 4,15 Z M 17,5 L 28,5 L 28,15 L 17,15 Z M 4,17 L 15,17 L 15,27 L 4,27 Z M 17,17 L 28,17 L 28,27 L 17,27 Z";

            var icons = new Dictionary<string, string> {
                {"cpu", IconCpu}, {"gpu", IconGpu}, {"ram", IconRam}, {"storage", IconStorage},
                {"screen", IconScreen}, {"battery", IconBattery}, {"wifi", IconWifi},
                {"camera", IconCamera}, {"ports", IconPorts}, {"os", IconOs}
            };
            
            var brushes = new Dictionary<string, (System.Windows.Media.SolidColorBrush Brush, System.Windows.Media.Color Color)> {
                {"cpu", (cyanBrush, cyanColor)}, {"gpu", (magentaBrush, magentaColor)}, {"ram", (cyanBrush, cyanColor)},
                {"storage", (limeBrush, limeColor)}, {"screen", (cyanBrush, cyanColor)}, {"battery", (limeBrush, limeColor)},
                {"wifi", (magentaBrush, magentaColor)}, {"camera", (cyanBrush, cyanColor)}, {"ports", (amberBrush, amberColor)},
                {"os", (cyanBrush, cyanColor)}
            };

            var angles = new Dictionary<string, int> {
                {"cpu", 8}, {"gpu", 50}, {"ram", 92}, {"storage", 134}, {"screen", 176},
                {"battery", 218}, {"wifi", 260}, {"camera", 302}, {"ports", 344}, {"os", 26}
            };

            var items = new List<SpecItem>();
            var marketingList = DisplayConfig.MarketingData ?? GetDefaultMarketingData();

            foreach (var m in marketingList)
            {
                string val = GetValueForId(m.Id);
                string detail = GetDetailForId(m.Id, m.DefaultDetail);
                
                var item = new SpecItem
                {
                    Id = m.Id,
                    Family = m.Family,
                    Label = m.Label,
                    Value = !string.IsNullOrWhiteSpace(val) ? val : (m.DefaultValue ?? "N/D"),
                    Detail = detail,
                    Summary = m.Summary,
                    BenchScore = m.BenchScore,
                    BenchLabel = m.BenchLabel,
                    Pros = m.Pros.Select((p, i) => new ProItem { Index = (i + 1).ToString("D2"), Text = p }).ToList(),
                    IconData = icons.ContainsKey(m.Id) ? icons[m.Id] : "",
                    AccentBrush = brushes.ContainsKey(m.Id) ? brushes[m.Id].Brush : cyanBrush,
                    AccentColor = brushes.ContainsKey(m.Id) ? brushes[m.Id].Color : cyanColor,
                    Angle = angles.ContainsKey(m.Id) ? angles[m.Id] : 0
                };
                items.Add(item);
            }

            int total = items.Count;
            for (int i = 0; i < items.Count; i++)
            {
                items[i].Index = i;
                items[i].IndexText = (i + 1).ToString("D2");
                items[i].IndexLabelFull = $"{(i + 1):D2} / {total:D2} · {items[i].Label}";
                items[i].LabelShort = items[i].Label.Length > 6 ? items[i].Label.Substring(0, 6) : items[i].Label;
                items[i].BenchBarWidth = items[i].BenchScore * 6.4; // 100% → 640px
                items[i].HasBench = items[i].BenchScore > 0;
                double rad = items[i].Angle * (Math.PI / 180);
                double r = 360;
                items[i].NodeX = 450 + Math.Cos(rad) * r - 84;
                items[i].NodeY = 450 + Math.Sin(rad) * r - 46;
                items[i].ConnectorOnRight = items[i].NodeX < 450 - 84;
                items[i].ConnectorX = items[i].ConnectorOnRight ? items[i].NodeX + 168 : items[i].NodeX - 28;
                items[i].ConnectorY = items[i].NodeY + 46;
                items[i].ConnectorLeft = items[i].ConnectorOnRight ? 168 : -28;
                items[i].NodeAnimDelay = TimeSpan.FromSeconds(0.05 * i);

                Specs.Add(items[i]);
            }
        }

        private string GetValueForId(string id)
        {
            return id.ToLower() switch
            {
                "cpu" => DisplayConfig.Cpu,
                "gpu" => DisplayConfig.Gpu,
                "ram" => DisplayConfig.Ram,
                "storage" => DisplayConfig.Storage,
                "screen" => DisplayConfig.Screen,
                "battery" => DisplayConfig.Battery,
                "wifi" => DisplayConfig.Wifi,
                "camera" => DisplayConfig.Camera,
                "ports" => DisplayConfig.Ports,
                "os" => DisplayConfig.Os,
                _ => null
            };
        }

        private string GetDetailForId(string id, string defaultDetail)
        {
            return id.ToLower() switch
            {
                "cpu" => DisplayConfig.Cores ?? defaultDetail,
                _ => defaultDetail
            };
        }

        private List<SpecMarketingData> GetDefaultMarketingData()
        {
            return new List<SpecMarketingData>
            {
                new SpecMarketingData { Id = "cpu", Family = "CPU", Label = "Procesador", Summary = "El cerebro del equipo. Más núcleos y más velocidad significan que abre programas al instante y no se atasca con varias cosas a la vez.", BenchScore = 86, BenchLabel = "vs. PC de gama media", Pros = new List<string> { "Edición de vídeo sin tirones", "Multitarea pesada", "Compilar / renderizar rápido" }, DefaultValue = "Intel Core i7", DefaultDetail = "13650HX · 14 núcleos · hasta 4.9 GHz" },
                new SpecMarketingData { Id = "gpu", Family = "GPU", Label = "Gráfica", Summary = "La pieza que dibuja en pantalla. Esta gráfica está pensada para jugar a calidad alta y para acelerar IA, edición y modelado 3D.", BenchScore = 92, BenchLabel = "rendimiento gaming 1080p", Pros = new List<string> { "Juegos AAA en alto", "Streaming sin lag", "Aceleración IA local" }, DefaultValue = "NVIDIA RTX 4060", DefaultDetail = "8 GB GDDR6 · Ray Tracing · DLSS 3.5" },
                new SpecMarketingData { Id = "ram", Family = "RAM", Label = "Memoria", Summary = "El espacio de trabajo del PC. Cuanta más RAM, más pestañas, programas y proyectos abiertos a la vez sin que vaya lento.", BenchScore = 90, BenchLabel = "vs. portátil estándar (8 GB)", Pros = new List<string> { "Decenas de pestañas abiertas", "Photoshop + navegador + Discord", "Máquinas virtuales" }, DefaultValue = "32 GB DDR5", DefaultDetail = "4800 MHz · 2× SO-DIMM · ampliable a 64 GB" },
                new SpecMarketingData { Id = "storage", Family = "SSD", Label = "Almacenamiento", Summary = "Donde se guarda todo. Un SSD NVMe es hasta 30× más rápido que un disco duro: el PC arranca en segundos y los juegos cargan al vuelo.", BenchScore = 95, BenchLabel = "vs. disco duro tradicional", Pros = new List<string> { "Arranque en <10 s", "Carga de juegos instantánea", "Transferencia de archivos rápida" }, DefaultValue = "1 TB NVMe", DefaultDetail = "PCIe 4.0 · 7000 MB/s lectura · M.2 2280" },
                new SpecMarketingData { Id = "screen", Family = "DISPLAY", Label = "Pantalla", Summary = "240 imágenes por segundo en alta resolución. Todo se ve nítido y el movimiento es mucho más suave que en una pantalla normal.", BenchScore = 88, BenchLabel = "fluidez en juegos competitivos", Pros = new List<string> { "Juegos competitivos", "Edición de foto/vídeo en color real", "Series y películas QHD" }, DefaultValue = "16″ QHD 240 Hz", DefaultDetail = "2560 × 1600 · IPS · 100% DCI-P3 · G-Sync" },
                new SpecMarketingData { Id = "battery", Family = "POWER", Label = "Batería", Summary = "Una jornada de trabajo lejos del enchufe en uso normal. Para jugar se recomienda con el cargador conectado.", BenchScore = 70, BenchLabel = "autonomía vs. gaming laptops", Pros = new List<string> { "Universidad / oficina", "Carga rápida USB-C", "Sin enchufe para tareas ligeras" }, DefaultValue = "90 Wh", DefaultDetail = "Hasta 8 h uso ofimática · carga rápida 65 %/30 min" },
                new SpecMarketingData { Id = "wifi", Family = "WIFI 6E", Label = "Conectividad", Summary = "La última generación de WiFi: hasta 3× más rápido y con menos interferencias. Bluetooth 5.3 para mando, auriculares y periféricos.", BenchScore = 84, BenchLabel = "vs. WiFi 5", Pros = new List<string> { "Streaming 4K sin cortes", "Partidas con ping bajo", "Latencia mínima en BT" }, DefaultValue = "WiFi 6E + BT 5.3", DefaultDetail = "Tri-banda 6 GHz · Bluetooth 5.3 · 2.5 GbE" },
                new SpecMarketingData { Id = "camera", Family = "WEBCAM", Label = "Cámara", Summary = "Resolución Full HD para videollamadas con buena cara. Lleva tapa física, así que cuando no la usas estás 100% privado.", BenchScore = 65, BenchLabel = "calidad llamadas", Pros = new List<string> { "Teams / Zoom / Meet", "Privacidad por hardware", "Reconocimiento facial" }, DefaultValue = "1080p FHD", DefaultDetail = "Con obturador de privacidad · micrófono dual" },
                new SpecMarketingData { Id = "ports", Family = "I/O", Label = "Puertos", Summary = "Todo lo que necesitas conectar sin adaptadores: dos monitores externos por HDMI/USB-C, red por cable, periféricos USB-A clásicos.", BenchScore = 0, BenchLabel = "", Pros = new List<string> { "Dos monitores externos", "Carga por USB-C", "LAN gigabit" }, DefaultValue = "USB-C × 2 · HDMI 2.1", DefaultDetail = "Thunderbolt 4 · USB-A × 3 · RJ-45 · jack 3.5" },
                new SpecMarketingData { Id = "os", Family = "OS", Label = "Sistema", Summary = "Sistema operativo más reciente, con todas las actualizaciones de seguridad al día y licencia legal incluida.", BenchScore = 0, BenchLabel = "", Pros = new List<string> { "Activado de fábrica", "Office compatible", "Soporte oficial Microsoft" }, DefaultValue = "Windows 11 Home", DefaultDetail = "Licencia original · activada · 64-bit" }
            };
        }

        private static readonly CultureInfo EsCulture = CultureInfo.GetCultureInfo("es-ES");

        private string FormatPrice(string price)
        {
            if (string.IsNullOrWhiteSpace(price)) return "";
            if (double.TryParse(price, NumberStyles.Any, CultureInfo.InvariantCulture, out double val))
                return val.ToString("C0", EsCulture);
            return price;
        }

        private string CalculateDiscount()
        {
            if (string.IsNullOrWhiteSpace(DisplayConfig?.Price) || string.IsNullOrWhiteSpace(DisplayConfig?.DiscountedPrice)) return "";
            if (double.TryParse(DisplayConfig.Price, NumberStyles.Any, CultureInfo.InvariantCulture, out double p) &&
                double.TryParse(DisplayConfig.DiscountedPrice, NumberStyles.Any, CultureInfo.InvariantCulture, out double d))
            {
                double pct = Math.Round((1 - (d / p)) * 100);
                return $"-{pct}%";
            }
            return "";
        }

        private string CalculateMonthly()
        {
            string pStr = DisplayConfig?.DiscountedPrice ?? DisplayConfig?.Price;
            if (string.IsNullOrWhiteSpace(pStr)) return "";
            if (double.TryParse(pStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double p))
            {
                return (p / 12).ToString("C2", EsCulture);
            }
            return "";
        }
    }
}