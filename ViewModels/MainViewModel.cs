using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using KioskClinicaPC.Core;
using KioskClinicaPC.Services;
using KioskClinicaPC.Models;
using Serilog;

namespace KioskClinicaPC.ViewModels
{
    public class MainViewModel : BaseViewModel
    {
        private readonly IHardwareService _hardwareService;
        private readonly IConfigRepository _configRepo;
        private readonly IDialogService _dialogs;
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

        private AppConfig _displayConfig = null!; // late-init en ApplyConfig (siempre antes de cualquier acceso)
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
                    OnPropertyChanged(nameof(ShowRefurbished));
                    OnPropertyChanged(nameof(WarrantyText));
                }
            }
        }

        // Logo de la marca detectada (ChassisName = fabricante). Si no hay archivo → texto de siempre.
        public string? BrandLogoPath => Core.AssetResolver.ResolveBrandLogo(DisplayConfig?.ChassisName);
        public bool HasBrandLogo => !string.IsNullOrWhiteSpace(BrandLogoPath);

        public ObservableCollection<SpecItem> Specs { get; } = new ObservableCollection<SpecItem>();
        public ObservableCollection<ScanLogItem> ScanLogs { get; } = new ObservableCollection<ScanLogItem>();

        // Blips del radar de Scan (variación lock-on). Hasta 8, ligados a los primeros componentes.
        public ObservableCollection<RadarBlip> RadarBlips { get; } = new ObservableCollection<RadarBlip>();

        private SpecItem? _selectedSpec;
        public SpecItem? SelectedSpec
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
        private SpecItem? _activeSpec;
        public SpecItem? ActiveSpec
        {
            get => _activeSpec;
            set => SetProperty(ref _activeSpec, value);
        }

        public string FormattedPrice => PriceFormatter.Format(DisplayConfig?.DiscountedPrice ?? DisplayConfig?.Price);
        public string FormattedOriginalPrice => PriceFormatter.Format(DisplayConfig?.Price);
        public string FormattedDiscount => PriceFormatter.Discount(DisplayConfig?.Price, DisplayConfig?.DiscountedPrice);
        public string FormattedMonthly => PriceFormatter.Monthly(DisplayConfig?.DiscountedPrice ?? DisplayConfig?.Price, InstallmentMonths);
        public bool HasDiscount => !string.IsNullOrWhiteSpace(DisplayConfig?.DiscountedPrice);

        // Distintivo "Reacondicionado" y garantía derivada del estado del equipo (Nuevo/Ocasión).
        public bool ShowRefurbished => DisplayConfig?.ShowRefurbished ?? true;
        public string WarrantyText => Warranty.Label(DisplayConfig?.Condition);

        // Pago en cuotas: por defecto 6 meses; el botón de la ficha alterna a 12 y vuelve.
        private int _installmentMonths = 6;
        public int InstallmentMonths
        {
            get => _installmentMonths;
            set
            {
                if (SetProperty(ref _installmentMonths, value))
                {
                    OnPropertyChanged(nameof(FormattedMonthly));
                    OnPropertyChanged(nameof(InstallmentsPrefix));
                    OnPropertyChanged(nameof(InstallmentsToggleText));
                }
            }
        }

        // Prefijo "6 × " / "12 × " junto a la cuota.
        public string InstallmentsPrefix => $"{InstallmentMonths} × ";
        // Texto del botón que alterna el plazo: muestra el plazo alternativo al actual.
        public string InstallmentsToggleText => InstallmentMonths == 6 ? "VER 12 MESES" : "VER 6 MESES";

        /// <summary>Alterna el plazo de cuotas entre 6 y 12 meses.</summary>
        public void ToggleInstallments() => InstallmentMonths = InstallmentMonths == 6 ? 12 : 6;

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

        public MainViewModel(IHardwareService hardwareService, IConfigRepository configRepo, IDialogService dialogs)
        {
            _hardwareService = hardwareService;
            _configRepo = configRepo;
            _dialogs = dialogs;
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
            // Config (crítico): el repositorio migra el esquema y, si el archivo está dañado, lo respalda
            // y devuelve valores por defecto marcando WasCorrupt para que avisemos sin perder nada en silencio.
            var load = await _configRepo.LoadConfigAsync();
            var savedConfig = load.Config;
            if (load.WasCorrupt)
                _dialogs.Warn(
                    "El archivo de configuración estaba dañado. Se ha guardado una copia (.corrupt) y se han restaurado los valores por defecto.",
                    "Configuración");

            // Hardware: último detectado (no crítico) + detección en vivo.
            var lastDetectedSpecs = await _configRepo.LoadLastHardwareAsync();

            try
            {
                _detectedSpecs = await _hardwareService.GetHardwareInfoAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error durante la detección de hardware en vivo.");
            }

            static bool IsDiff(string? s1, string? s2)
                => !string.IsNullOrWhiteSpace(s1) && !string.Equals(s1, s2, StringComparison.OrdinalIgnoreCase);

            bool newHardwareDetected = IsDiff(lastDetectedSpecs.Cpu, _detectedSpecs.Cpu)
                || IsDiff(lastDetectedSpecs.Gpu, _detectedSpecs.Gpu)
                || IsDiff(lastDetectedSpecs.Ram, _detectedSpecs.Ram);

            if (newHardwareDetected && _dialogs.Confirm("Se ha detectado hardware nuevo.\n¿Actualizar valores detectados?", "Hardware"))
            {
                ClearDetectedOverrides(savedConfig);
                _configRepo.SaveConfig(savedConfig);
            }

            _configRepo.SaveHardware(_detectedSpecs);

            _savedConfig = savedConfig;

            // Persiste la migración de esquema (sello de versión + campos reubicados).
            bool needsSeedSave = load.Migrated;

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
                    _configRepo.SaveConfig(_savedConfig);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error al guardar la configuración con datos por defecto.");
                }
            }

            ApplyConfig();
        }

        /// <summary>Olvida los overrides manuales de identidad/componentes para que el equipo recién
        /// detectado se muestre tal cual (el merge en ApplyConfig caerá entonces a lo detectado).</summary>
        private static void ClearDetectedOverrides(AppConfig config)
        {
            config.Cpu = config.Cores = config.Ram = config.Gpu = config.Storage = config.Screen = config.Os = null;
            config.Battery = config.Wifi = config.Camera = config.Ports = null;
            config.ChassisName = config.ModelName = config.Family = config.Sku = null;
            config.RamDetail = config.StorageDetail = config.ScreenDetail = config.BatteryDetail = null;
            config.GpuDetail = config.WifiDetail = config.CameraDetail = config.PortsDetail = config.OsDetail = null;
        }

        /// <summary>Reconstruye DisplayConfig/Texts/Slides/Specs desde _savedConfig (sin redetectar hardware).</summary>
        private void ApplyConfig()
        {
            DisplayConfig = new AppConfig
            {
                Cpu = ConfigMerger.Display(_savedConfig.Cpu, _detectedSpecs.Cpu),
                Cores = ConfigMerger.Display(_savedConfig.Cores, _detectedSpecs.Cores),
                Ram = ConfigMerger.Display(_savedConfig.Ram, _detectedSpecs.Ram),
                Gpu = ConfigMerger.Display(_savedConfig.Gpu, _detectedSpecs.Gpu),
                Storage = ConfigMerger.Display(_savedConfig.Storage, _detectedSpecs.Storage),
                Screen = ConfigMerger.Display(_savedConfig.Screen, _detectedSpecs.Screen),
                Os = ConfigMerger.Display(_savedConfig.Os, ConfigMerger.NormalizeOs(_detectedSpecs.Os)),
                // Componentes opcionales: override manual o lo detectado; null = ausente (no se muestra).
                Battery = ConfigMerger.Display(_savedConfig.Battery, _detectedSpecs.Battery),
                Wifi = ConfigMerger.Display(_savedConfig.Wifi, _detectedSpecs.Wifi),
                Camera = ConfigMerger.Display(_savedConfig.Camera, _detectedSpecs.Camera),
                Ports = ConfigMerger.Display(_savedConfig.Ports, _detectedSpecs.Ports),
                // Detalle técnico (StatStrip): override manual de Settings o lo detectado por WMI.
                RamDetail = ConfigMerger.Display(_savedConfig.RamDetail, _detectedSpecs.RamDetail),
                StorageDetail = ConfigMerger.Display(_savedConfig.StorageDetail, _detectedSpecs.StorageDetail),
                ScreenDetail = ConfigMerger.Display(_savedConfig.ScreenDetail, _detectedSpecs.ScreenDetail),
                BatteryDetail = ConfigMerger.Display(_savedConfig.BatteryDetail, _detectedSpecs.BatteryDetail),
                GpuDetail = ConfigMerger.Display(_savedConfig.GpuDetail, _detectedSpecs.GpuDetail),
                WifiDetail = ConfigMerger.Display(_savedConfig.WifiDetail, _detectedSpecs.WifiDetail),
                CameraDetail = ConfigMerger.Display(_savedConfig.CameraDetail, _detectedSpecs.CameraDetail),
                PortsDetail = ConfigMerger.Display(_savedConfig.PortsDetail, _detectedSpecs.PortsDetail),
                OsDetail = ConfigMerger.Display(_savedConfig.OsDetail, _detectedSpecs.OsDetail),
                Price = _savedConfig.Price,
                DiscountedPrice = _savedConfig.DiscountedPrice,
                // Identidad real detectada (sin hardcode). null si la detección no la aporta → editable en Settings.
                ChassisName = ConfigMerger.Display(_savedConfig.ChassisName, _detectedSpecs.ChassisName),
                ModelName = ConfigMerger.Display(_savedConfig.ModelName, _detectedSpecs.ModelName),
                Family = ConfigMerger.Display(_savedConfig.Family, _detectedSpecs.Family),
                Sku = ConfigMerger.Display(_savedConfig.Sku, _detectedSpecs.Sku),
                ShopAddress = ConfigMerger.Display(_savedConfig.ShopAddress, AppConfig.DefaultShopAddress),
                ShopServices = ConfigMerger.Display(_savedConfig.ShopServices, "Asistencia · Cambio · Reparación · Reacondicionado"),
                ProductImagePath = _savedConfig.ProductImagePath,
                MarketingData = _savedConfig.MarketingData,
                // Distintivo "Reacondicionado" + estado (garantía): se copian directos. Omitirlos dejaba
                // ShowRefurbished/Condition en sus defaults (true / "Ocasion"), ignorando lo guardado.
                ShowRefurbished = _savedConfig.ShowRefurbished,
                Condition = _savedConfig.Condition
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

            // TryFindResource (no FindResource): si faltara una clave de tema, FindResource lanza
            // y aborta toda la creación de specs. Con fallback al color real del tema, degrada en
            // vez de romper. El color resuelto sirve de respaldo para su brush, manteniéndolos coherentes.
            System.Windows.Media.Color Col(string key, string hex)
                => Application.Current.TryFindResource(key) is System.Windows.Media.Color c
                    ? c
                    : (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
            System.Windows.Media.SolidColorBrush Br(string key, System.Windows.Media.Color fb)
                => Application.Current.TryFindResource(key) as System.Windows.Media.SolidColorBrush
                    ?? new System.Windows.Media.SolidColorBrush(fb);

            var cyanColor = Col("CyanColor", "#F37A4A");
            var cyanBrush = Br("CyanBrush", cyanColor);
            var magentaColor = Col("MagentaColor", "#FFB069");
            var magentaBrush = Br("MagentaBrush", magentaColor);
            var limeColor = Col("OkColor", "#F0D26B");
            var limeBrush = Br("OkBrush", limeColor);
            var amberColor = Col("AmberColor", "#FFA75C");
            var amberBrush = Br("AmberBrush", amberColor);

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
                if (m.Id is null) continue; // entrada de marketing sin id no es accionable

                string? raw = GetValueForId(m.Id);
                string detail = GetDetailForId(m.Id, m.DefaultDetail);

                // Ocultar componentes ausentes: un opcional sin valor detectado ni override no se muestra.
                bool present = ComponentIds.IsAlwaysPresent(m.Id) || !string.IsNullOrWhiteSpace(raw);
                if (!present) continue;

                // Titular amigable + técnico secundario (p.ej. "WiFi 6E" + "Intel AX211").
                var fmt = SpecFormatter.Format(m.Id, raw);

                // Potencia y gama calculadas desde el hardware real (no constantes inventadas).
                int score = PerformanceScorer.Score(m.Id, DisplayConfig);

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
                    BenchScore = score,
                    Tier = PerformanceScorer.TierLabel(m.Id, score),
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
                string label = items[i].Label ?? "";
                items[i].LabelShort = label.Length > 6 ? label.Substring(0, 6) : label;
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

            BuildRadarBlips();
        }

        // Posiciones del radar de Scan (variación lock-on), copiadas del mockup (RADAR_POS en
        // components.jsx): radar 760×760, centro (380,380), x=380+cos(a)·r, y=380+sin(a)·r,
        // flip si x>380, pd=((a+360)%360)/360·1.7s. Mapeo por id para reproducir el layout exacto.
        private static readonly Dictionary<string, (double X, double Y, bool Flip, double Pd)> RadarLayout = new()
        {
            [ComponentIds.Cpu]     = (485.6, 153.4, true,  1.393),
            [ComponentIds.Gpu]     = (680.7, 270.6, true,  1.606),
            [ComponentIds.Ram]     = (543.8, 494.7, true,  0.165),
            [ComponentIds.Storage] = (432.1, 675.4, true,  0.378),
            [ComponentIds.Screen]  = (225.7, 563.8, false, 0.614),
            [ComponentIds.Battery] = ( 91.0, 405.3, false, 0.826),
            [ComponentIds.Wifi]    = (224.1, 290.0, false, 0.992),
            [ComponentIds.Camera]  = (333.6, 237.4, false, 1.190),
        };

        private void BuildRadarBlips()
        {
            RadarBlips.Clear();
            foreach (var spec in Specs)
            {
                if (spec.Id == null || !RadarLayout.TryGetValue(spec.Id, out var p)) continue;
                RadarBlips.Add(new RadarBlip
                {
                    Id = spec.Id,
                    X = p.X,
                    Y = p.Y,
                    Flip = p.Flip,
                    PingDelaySeconds = p.Pd,
                    IconData = spec.IconData,
                    AccentBrush = spec.AccentBrush,
                    AccentColor = spec.AccentColor,
                    Label = spec.Label,
                });
            }
        }

        private string? GetValueForId(string? id)
            => ComponentRegistry.TryGet(id, out var accessor) ? accessor.GetValue(DisplayConfig) : null;

        // Detalle técnico del StatStrip por componente: override manual / detectado (ya fusionados en
        // DisplayConfig.*Detail; el CPU usa Cores) y, si no hay nada real, el genérico del catálogo (hoy "").
        private string GetDetailForId(string? id, string? defaultDetail)
        {
            string? detail = ComponentRegistry.TryGet(id, out var accessor) ? accessor.GetDetail(DisplayConfig) : null;
            return !string.IsNullOrWhiteSpace(detail) ? detail : (defaultDetail ?? "");
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
                        m.Pros = spec.Pros?.Select(p => p.Text ?? "").ToList() ?? new List<string>();
                    }
                }

                string? detectedOs = ConfigMerger.NormalizeOs(_detectedSpecs.Os);

                _savedConfig.Cpu = ConfigMerger.Override(DisplayConfig.Cpu, _detectedSpecs.Cpu);
                _savedConfig.Cores = ConfigMerger.Override(DisplayConfig.Cores, _detectedSpecs.Cores);
                _savedConfig.Ram = ConfigMerger.Override(DisplayConfig.Ram, _detectedSpecs.Ram);
                _savedConfig.Gpu = ConfigMerger.Override(DisplayConfig.Gpu, _detectedSpecs.Gpu);
                _savedConfig.Storage = ConfigMerger.Override(DisplayConfig.Storage, _detectedSpecs.Storage);
                _savedConfig.Screen = ConfigMerger.Override(DisplayConfig.Screen, _detectedSpecs.Screen);
                _savedConfig.Os = ConfigMerger.Override(DisplayConfig.Os, detectedOs);
                _savedConfig.Battery = ConfigMerger.NoPlaceholder(DisplayConfig.Battery);
                _savedConfig.Wifi = ConfigMerger.NoPlaceholder(DisplayConfig.Wifi);
                _savedConfig.Camera = ConfigMerger.NoPlaceholder(DisplayConfig.Camera);
                _savedConfig.Ports = ConfigMerger.NoPlaceholder(DisplayConfig.Ports);

                // Overrides de detalle técnico: guardar solo si difieren de lo detectado.
                _savedConfig.RamDetail = ConfigMerger.Override(DisplayConfig.RamDetail, _detectedSpecs.RamDetail);
                _savedConfig.StorageDetail = ConfigMerger.Override(DisplayConfig.StorageDetail, _detectedSpecs.StorageDetail);
                _savedConfig.ScreenDetail = ConfigMerger.Override(DisplayConfig.ScreenDetail, _detectedSpecs.ScreenDetail);
                _savedConfig.BatteryDetail = ConfigMerger.Override(DisplayConfig.BatteryDetail, _detectedSpecs.BatteryDetail);
                _savedConfig.GpuDetail = ConfigMerger.Override(DisplayConfig.GpuDetail, _detectedSpecs.GpuDetail);
                _savedConfig.WifiDetail = ConfigMerger.Override(DisplayConfig.WifiDetail, _detectedSpecs.WifiDetail);
                _savedConfig.CameraDetail = ConfigMerger.Override(DisplayConfig.CameraDetail, _detectedSpecs.CameraDetail);
                _savedConfig.PortsDetail = ConfigMerger.Override(DisplayConfig.PortsDetail, _detectedSpecs.PortsDetail);
                _savedConfig.OsDetail = ConfigMerger.Override(DisplayConfig.OsDetail, _detectedSpecs.OsDetail);

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

                _configRepo.SaveConfig(_savedConfig);
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
                _configRepo.SaveConfig(_savedConfig);
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
            if (ComponentRegistry.TryGet(id, out var accessor)) accessor.SetValue(DisplayConfig, value);
        }

    }
}