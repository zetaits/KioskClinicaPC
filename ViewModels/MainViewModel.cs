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
            set
            {
                if (SetProperty(ref _currentScreen, value))
                    OnPropertyChanged(nameof(IsAttractScreen));
            }
        }

        public bool IsAttractScreen => _currentScreen == 0;

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
                    OnPropertyChanged(nameof(BrandLogoPath));
                    OnPropertyChanged(nameof(HasBrandLogo));
                }
            }
        }

        // Logo de la marca detectada (ChassisName = fabricante). Si no hay archivo → texto de siempre.
        public string BrandLogoPath => Core.AssetResolver.ResolveBrandLogo(DisplayConfig?.ChassisName);
        public bool HasBrandLogo => !string.IsNullOrWhiteSpace(BrandLogoPath);

        public ObservableCollection<SpecItem> Specs { get; } = new ObservableCollection<SpecItem>();
        public ObservableCollection<ScanLogItem> ScanLogs { get; } = new ObservableCollection<ScanLogItem>();

        private SpecItem _selectedSpec;
        public SpecItem SelectedSpec
        {
            get => _selectedSpec;
            set
            {
                var previous = _selectedSpec;
                if (SetProperty(ref _selectedSpec, value))
                {
                    if (previous != null) previous.IsCurrentDetail = false;
                    if (value != null) value.IsCurrentDetail = true;
                    if (CurrentScreen == 3 && value != null)
                        CurrentScreenName = $"DETALLE · {value.Label?.ToUpperInvariant()}";
                }
            }
        }

        // Componente "en foco" en la pantalla Main: el spotlight central lo sigue (icono + nombre + valor).
        private SpecItem _activeSpec;
        public SpecItem ActiveSpec
        {
            get => _activeSpec;
            set => SetProperty(ref _activeSpec, value);
        }

        public string FormattedPrice => FormatPrice(DisplayConfig?.DiscountedPrice ?? DisplayConfig?.Price);
        public string FormattedOriginalPrice => FormatPrice(DisplayConfig?.Price);
        public string FormattedDiscount => CalculateDiscount();
        public string FormattedMonthly => CalculateMonthly();
        public bool HasDiscount => !string.IsNullOrWhiteSpace(DisplayConfig?.DiscountedPrice);

        // Attract screen slides
        public ObservableCollection<AttractSlide> Slides { get; } = new ObservableCollection<AttractSlide>();

        private AttractSlide _currentSlide = new AttractSlide();
        public AttractSlide CurrentSlide { get => _currentSlide; set => SetProperty(ref _currentSlide, value); }

        // Textos de chrome editables (etiquetas fijas)
        private EditableContent _texts = new EditableContent(null);
        public EditableContent Texts { get => _texts; set => SetProperty(ref _texts, value); }

        // Estado del modo edición libre (binding de la barra flotante)
        public EditModeService EditMode => EditModeService.Instance;

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

            // Config (crítico): si existe pero está dañado, respáldalo y avisa en vez de perderlo en silencio.
            if (File.Exists(App.ConfigFilePath))
            {
                try
                {
                    savedConfig = JsonConvert.DeserializeObject<AppConfig>(await File.ReadAllTextAsync(App.ConfigFilePath));
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "KioskConfig.json dañado; se respalda y se continúa con valores por defecto.");
                    BackupCorruptFile(App.ConfigFilePath);
                    MessageBox.Show(
                        "El archivo de configuración estaba dañado. Se ha guardado una copia (.corrupt) y se han restaurado los valores por defecto.",
                        "Configuración", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }

            // Hardware (no crítico): se re-detecta igualmente si falla la lectura.
            try
            {
                if (File.Exists(App.HardwareFilePath))
                    lastDetectedSpecs = JsonConvert.DeserializeObject<AppConfig>(await File.ReadAllTextAsync(App.HardwareFilePath));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "KioskHardware.json no se pudo leer; se re-detecta.");
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
                    savedConfig.Battery = savedConfig.Wifi = savedConfig.Camera = savedConfig.Ports = null;
                    savedConfig.ChassisName = savedConfig.ModelName = savedConfig.Family = savedConfig.Sku = null;
                    JsonStore.WriteAtomic(App.ConfigFilePath, JsonConvert.SerializeObject(savedConfig, Formatting.Indented));
                }
            }

            JsonStore.WriteAtomic(App.HardwareFilePath, JsonConvert.SerializeObject(_detectedSpecs, Formatting.Indented));

            _savedConfig = savedConfig;

            bool needsSeedSave = false;

            if (_savedConfig.MarketingData == null || _savedConfig.MarketingData.Count == 0)
            {
                _savedConfig.MarketingData = GetDefaultMarketingData();
                needsSeedSave = true;
            }

            if (_savedConfig.AttractSlides == null || _savedConfig.AttractSlides.Count == 0)
            {
                _savedConfig.AttractSlides = GetDefaultSlides();
                needsSeedSave = true;
            }

            // Migra etiquetas por defecto obsoletas a las nuevas (solo si el usuario no las ha personalizado).
            if (UpgradeStaleLabels(_savedConfig.MarketingData))
                needsSeedSave = true;

            if (needsSeedSave)
            {
                try
                {
                    JsonStore.WriteAtomic(App.ConfigFilePath, JsonConvert.SerializeObject(_savedConfig, Formatting.Indented));
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error al guardar la configuración con datos por defecto.");
                }
            }

            ApplyConfig();
        }

        /// <summary>Mueve un archivo dañado a una copia con sello de tiempo para no perder los datos.</summary>
        private static void BackupCorruptFile(string path)
        {
            try
            {
                string backup = $"{path}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}.bak";
                File.Move(path, backup, overwrite: true);
                Log.Information("Archivo dañado respaldado en {Backup}.", backup);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "No se pudo respaldar el archivo dañado {Path}.", path);
            }
        }

        /// <summary>Reconstruye DisplayConfig/Texts/Slides/Specs desde _savedConfig (sin redetectar hardware).</summary>
        private void ApplyConfig()
        {
            DisplayConfig = new AppConfig
            {
                Cpu = !string.IsNullOrWhiteSpace(_savedConfig.Cpu) ? _savedConfig.Cpu : (_detectedSpecs.Cpu),
                Cores = !string.IsNullOrWhiteSpace(_savedConfig.Cores) ? _savedConfig.Cores : (_detectedSpecs.Cores),
                Ram = !string.IsNullOrWhiteSpace(_savedConfig.Ram) ? _savedConfig.Ram : (_detectedSpecs.Ram),
                Gpu = !string.IsNullOrWhiteSpace(_savedConfig.Gpu) ? _savedConfig.Gpu : (_detectedSpecs.Gpu),
                Storage = !string.IsNullOrWhiteSpace(_savedConfig.Storage) ? _savedConfig.Storage : (_detectedSpecs.Storage),
                Screen = !string.IsNullOrWhiteSpace(_savedConfig.Screen) ? _savedConfig.Screen : (_detectedSpecs.Screen),
                Os = !string.IsNullOrWhiteSpace(_savedConfig.Os) ? _savedConfig.Os : (_detectedSpecs.Os?.Split('(')[0].Trim()),
                // Componentes opcionales: override manual o lo detectado; null = ausente (no se muestra).
                Battery = !string.IsNullOrWhiteSpace(_savedConfig.Battery) ? _savedConfig.Battery : _detectedSpecs.Battery,
                Wifi = !string.IsNullOrWhiteSpace(_savedConfig.Wifi) ? _savedConfig.Wifi : _detectedSpecs.Wifi,
                Camera = !string.IsNullOrWhiteSpace(_savedConfig.Camera) ? _savedConfig.Camera : _detectedSpecs.Camera,
                Ports = !string.IsNullOrWhiteSpace(_savedConfig.Ports) ? _savedConfig.Ports : _detectedSpecs.Ports,
                Price = _savedConfig.Price,
                DiscountedPrice = _savedConfig.DiscountedPrice,
                // Identidad real detectada (sin hardcode). null si la detección no la aporta → editable en Settings.
                ChassisName = !string.IsNullOrWhiteSpace(_savedConfig.ChassisName) ? _savedConfig.ChassisName : _detectedSpecs.ChassisName,
                ModelName = !string.IsNullOrWhiteSpace(_savedConfig.ModelName) ? _savedConfig.ModelName : _detectedSpecs.ModelName,
                Family = !string.IsNullOrWhiteSpace(_savedConfig.Family) ? _savedConfig.Family : _detectedSpecs.Family,
                Sku = !string.IsNullOrWhiteSpace(_savedConfig.Sku) ? _savedConfig.Sku : _detectedSpecs.Sku,
                ShopAddress = !string.IsNullOrWhiteSpace(_savedConfig.ShopAddress) ? _savedConfig.ShopAddress : "Calle Sevilla 54, Málaga",
                ShopServices = !string.IsNullOrWhiteSpace(_savedConfig.ShopServices) ? _savedConfig.ShopServices : "Asistencia · Cambio · Reparación · Reacondicionado",
                ProductImagePath = _savedConfig.ProductImagePath,
                MarketingData = _savedConfig.MarketingData
            };

            Texts = new EditableContent(new Dictionary<string, string>(_savedConfig.UiTexts ?? new Dictionary<string, string>()));

            Slides.Clear();
            foreach (var s in _savedConfig.AttractSlides ?? new List<AttractSlide>())
                Slides.Add(new AttractSlide { Eyebrow = s.Eyebrow, Title1 = s.Title1, Title2 = s.Title2, Subtitle = s.Subtitle });
            CurrentSlide = Slides.Count > 0 ? Slides[0] : new AttractSlide();

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
            // Iconos se renderizan con Fill (no Stroke): el wifi debe ser formas CERRADAS.
            // Antes eran arcos abiertos → al rellenarse formaban medias lunas ("AI slop").
            // Ahora: dos bandas anulares (sector exterior+interior cerrado) + punto sólido.
            const string IconWifi = "M 0.24,13.68 A 20,20 0 0,1 31.76,13.68 L 28.61,16.14 A 16,16 0 0,0 3.39,16.14 Z M 5.76,17.99 A 13,13 0 0,1 26.24,17.99 L 23.09,20.46 A 9,9 0 0,0 8.91,20.46 Z M 13.8,26 A 2.2,2.2 0 1,1 18.2,26 A 2.2,2.2 0 1,1 13.8,26 Z";
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
                string raw = GetValueForId(m.Id);
                string detail = GetDetailForId(m.Id, m.DefaultDetail);

                // Ocultar componentes ausentes: un opcional sin valor detectado ni override no se muestra.
                bool present = ComponentIds.IsAlwaysPresent(m.Id) || !string.IsNullOrWhiteSpace(raw);
                if (!present) continue;

                // Titular amigable + técnico secundario (p.ej. "WiFi 6E" + "Intel AX211").
                var fmt = SpecFormatter.Format(m.Id, raw);

                var item = new SpecItem
                {
                    Id = m.Id,
                    Family = m.Family,
                    Label = m.Label,
                    Value = !string.IsNullOrWhiteSpace(fmt.Headline) ? fmt.Headline : (m.DefaultValue ?? "N/D"),
                    TechDetail = fmt.Tech,
                    IsPresent = present,
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
                // Foto real del componente: empareja modelo concreto / valor con archivo en SpecImages.
                item.ImagePath = Core.AssetResolver.ResolveSpecImage(item.TechDetail, item.Value);
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

            ActiveSpec = Specs.Count > 0 ? Specs[0] : null;
        }

        private string GetValueForId(string id)
        {
            return id.ToLowerInvariant() switch
            {
                ComponentIds.Cpu => DisplayConfig.Cpu,
                ComponentIds.Gpu => DisplayConfig.Gpu,
                ComponentIds.Ram => DisplayConfig.Ram,
                ComponentIds.Storage => DisplayConfig.Storage,
                ComponentIds.Screen => DisplayConfig.Screen,
                ComponentIds.Battery => DisplayConfig.Battery,
                ComponentIds.Wifi => DisplayConfig.Wifi,
                ComponentIds.Camera => DisplayConfig.Camera,
                ComponentIds.Ports => DisplayConfig.Ports,
                ComponentIds.Os => DisplayConfig.Os,
                _ => null
            };
        }

        private string GetDetailForId(string id, string defaultDetail)
        {
            return id.ToLowerInvariant() switch
            {
                ComponentIds.Cpu => DisplayConfig.Cores ?? defaultDetail,
                _ => defaultDetail
            };
        }

        // Etiquetas por defecto antiguas → nuevas. Solo se aplican si coinciden exactamente con la antigua
        // (es decir, el usuario no las ha cambiado), para no pisar personalizaciones.
        private static readonly Dictionary<string, (string Old, string New)> StaleLabelMap = new()
        {
            [ComponentIds.Gpu] = ("Gráfica", "Tarjeta gráfica"),
            [ComponentIds.Ram] = ("Memoria", "Memoria RAM"),
            [ComponentIds.Wifi] = ("Conectividad", "Conectividad WiFi"),
            [ComponentIds.Os] = ("Sistema", "Sistema operativo"),
        };

        private static bool UpgradeStaleLabels(List<SpecMarketingData>? marketing)
        {
            if (marketing == null) return false;
            bool changed = false;
            foreach (var m in marketing)
            {
                if (m.Id != null && StaleLabelMap.TryGetValue(m.Id.ToLowerInvariant(), out var map)
                    && string.Equals(m.Label?.Trim(), map.Old, StringComparison.OrdinalIgnoreCase))
                {
                    m.Label = map.New;
                    changed = true;
                }
            }
            return changed;
        }

        private List<SpecMarketingData> GetDefaultMarketingData() => SpecCatalog.DefaultMarketing();

        private List<AttractSlide> GetDefaultSlides() => SpecCatalog.DefaultSlides();

        /// <summary>Persiste el estado editado (DisplayConfig/Specs/Slides/Texts) en KioskConfig.json.</summary>
        public void SaveEdits()
        {
            try
            {
                foreach (var spec in Specs)
                {
                    SetDisplayValueById(spec.Id, spec.Value);
                    if (string.Equals(spec.Id, "cpu", StringComparison.OrdinalIgnoreCase))
                        DisplayConfig.Cores = spec.Detail;

                    var m = _savedConfig.MarketingData?.FirstOrDefault(x => string.Equals(x.Id, spec.Id, StringComparison.OrdinalIgnoreCase));
                    if (m != null)
                    {
                        m.Label = spec.Label;
                        m.Summary = spec.Summary;
                        m.BenchLabel = spec.BenchLabel;
                        m.Pros = spec.Pros?.Select(p => p.Text).ToList() ?? new List<string>();
                    }
                }

                string? Override(string? manual, string? detected) =>
                    (string.IsNullOrWhiteSpace(manual) || string.Equals(manual, detected, StringComparison.OrdinalIgnoreCase)) ? null : manual;
                string? NoPlaceholder(string? v) =>
                    (string.IsNullOrWhiteSpace(v) || v == "No detectada" || v == "No detectado") ? null : v;

                string? detectedOs = _detectedSpecs.Os?.Split('(')[0].Trim();

                _savedConfig.Cpu = Override(DisplayConfig.Cpu, _detectedSpecs.Cpu);
                _savedConfig.Cores = Override(DisplayConfig.Cores, _detectedSpecs.Cores);
                _savedConfig.Ram = Override(DisplayConfig.Ram, _detectedSpecs.Ram);
                _savedConfig.Gpu = Override(DisplayConfig.Gpu, _detectedSpecs.Gpu);
                _savedConfig.Storage = Override(DisplayConfig.Storage, _detectedSpecs.Storage);
                _savedConfig.Screen = Override(DisplayConfig.Screen, _detectedSpecs.Screen);
                _savedConfig.Os = Override(DisplayConfig.Os, detectedOs);
                _savedConfig.Battery = NoPlaceholder(DisplayConfig.Battery);
                _savedConfig.Wifi = NoPlaceholder(DisplayConfig.Wifi);
                _savedConfig.Camera = NoPlaceholder(DisplayConfig.Camera);
                _savedConfig.Ports = NoPlaceholder(DisplayConfig.Ports);

                _savedConfig.ChassisName = DisplayConfig.ChassisName;
                _savedConfig.ModelName = DisplayConfig.ModelName;
                _savedConfig.Family = DisplayConfig.Family;
                _savedConfig.Sku = DisplayConfig.Sku;
                _savedConfig.ShopAddress = DisplayConfig.ShopAddress;
                _savedConfig.ShopServices = DisplayConfig.ShopServices;
                _savedConfig.ProductImagePath = DisplayConfig.ProductImagePath;
                _savedConfig.Price = DisplayConfig.Price;
                _savedConfig.DiscountedPrice = DisplayConfig.DiscountedPrice;

                _savedConfig.AttractSlides = Slides
                    .Select(s => new AttractSlide { Eyebrow = s.Eyebrow, Title1 = s.Title1, Title2 = s.Title2, Subtitle = s.Subtitle })
                    .ToList();
                _savedConfig.UiTexts = new Dictionary<string, string>(Texts.Overrides);

                JsonStore.WriteAtomic(App.ConfigFilePath, JsonConvert.SerializeObject(_savedConfig, Formatting.Indented));
                EditModeService.Instance.IsDirty = false;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error al guardar los cambios del modo edición.");
                throw;
            }
        }

        /// <summary>Fija y persiste de inmediato la ruta de la foto del producto (drag-drop en la pantalla Resumen).</summary>
        public void SaveProductImage(string? path)
        {
            DisplayConfig.ProductImagePath = path;
            _savedConfig.ProductImagePath = path;
            try
            {
                JsonStore.WriteAtomic(App.ConfigFilePath, JsonConvert.SerializeObject(_savedConfig, Formatting.Indented));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error al guardar la ruta de la foto del producto.");
            }
        }

        /// <summary>Revierte los cambios no guardados reconstruyendo desde la configuración en memoria/disco.</summary>
        public void DiscardEdits()
        {
            ApplyConfig();
            EditModeService.Instance.IsDirty = false;
        }

        private void SetDisplayValueById(string? id, string? value)
        {
            switch (id?.ToLowerInvariant())
            {
                case "cpu": DisplayConfig.Cpu = value; break;
                case "gpu": DisplayConfig.Gpu = value; break;
                case "ram": DisplayConfig.Ram = value; break;
                case "storage": DisplayConfig.Storage = value; break;
                case "screen": DisplayConfig.Screen = value; break;
                case "battery": DisplayConfig.Battery = value; break;
                case "wifi": DisplayConfig.Wifi = value; break;
                case "camera": DisplayConfig.Camera = value; break;
                case "ports": DisplayConfig.Ports = value; break;
                case "os": DisplayConfig.Os = value; break;
            }
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