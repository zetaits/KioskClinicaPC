using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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

        /// <summary>Orquesta el arranque: carga config (migra/respalda), detecta hardware, ofrece
        /// actualizar si cambió, siembra defaults y reconstruye la vista. Cada paso es un método con nombre.</summary>
        public async Task LoadHardwareAndConfigAsync()
        {
            var load = await _configRepo.LoadConfigAsync();
            var savedConfig = load.Config;
            if (load.WasCorrupt)
                _dialogs.Warn(
                    "El archivo de configuración estaba dañado. Se ha guardado una copia (.corrupt) y se han restaurado los valores por defecto.",
                    "Configuración");

            var lastDetectedSpecs = await _configRepo.LoadLastHardwareAsync();
            await DetectHardwareAsync();

            if (IsNewHardware(lastDetectedSpecs)
                && _dialogs.Confirm("Se ha detectado hardware nuevo.\n¿Actualizar valores detectados?", "Hardware"))
            {
                ClearDetectedOverrides(savedConfig);
                _configRepo.SaveConfig(savedConfig);
            }

            _configRepo.SaveHardware(_detectedSpecs);

            _savedConfig = savedConfig;

            // Persiste si migró el esquema o si hubo que sembrar contenido por defecto.
            bool needsSeedSave = load.Migrated;
            needsSeedSave |= SeedDefaults(_savedConfig);
            if (needsSeedSave) TrySaveConfig();

            ApplyConfig();
        }

        /// <summary>Detección de hardware en vivo (no crítica: si falla, se conserva lo que hubiera).</summary>
        private async Task DetectHardwareAsync()
        {
            try
            {
                _detectedSpecs = await _hardwareService.GetHardwareInfoAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error durante la detección de hardware en vivo.");
            }
        }

        /// <summary>¿El hardware en vivo difiere del último detectado (CPU/GPU/RAM)?</summary>
        private bool IsNewHardware(AppConfig lastDetected)
        {
            static bool IsDiff(string? s1, string? s2)
                => !string.IsNullOrWhiteSpace(s1) && !string.Equals(s1, s2, StringComparison.OrdinalIgnoreCase);

            return IsDiff(lastDetected.Cpu, _detectedSpecs.Cpu)
                || IsDiff(lastDetected.Gpu, _detectedSpecs.Gpu)
                || IsDiff(lastDetected.Ram, _detectedSpecs.Ram);
        }

        /// <summary>Siembra marketing/slides por defecto si faltan y migra etiquetas obsoletas.
        /// Devuelve true si cambió algo (y por tanto hay que persistir).</summary>
        private bool SeedDefaults(AppConfig config)
        {
            bool changed = false;

            if (config.MarketingData == null || config.MarketingData.Count == 0)
            {
                config.MarketingData = GetDefaultMarketingData();
                changed = true;
            }

            if (config.AttractSlides == null || config.AttractSlides.Count == 0)
            {
                config.AttractSlides = GetDefaultSlides();
                changed = true;
            }

            // Migra etiquetas por defecto obsoletas a las nuevas (solo si el usuario no las ha personalizado).
            changed |= UpgradeStaleLabels(config.MarketingData);
            return changed;
        }

        /// <summary>Persiste la configuración registrando (sin propagar) cualquier fallo de E/S.</summary>
        private void TrySaveConfig()
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
                ShopServices = ConfigMerger.Display(_savedConfig.ShopServices, "Asistencia · Cambio · Reparación"),
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

                // "Qué significa"/"Para qué te sirve" adaptados al hardware real (p.ej. no decir "última
                // generación de WiFi" en un WiFi 5). Solo pisa el texto si la tienda no lo editó.
                var narration = SpecNarrator.Narrate(m.Id, DisplayConfig, score);
                string? summary = PickSummary(m, narration);
                List<string> pros = PickPros(m, narration);

                // Identidad visual (icono/acento/ángulo): tabla de presentación fuera del VM.
                var vis = ComponentVisuals.For(m.Id);

                var item = new SpecItem
                {
                    Id = m.Id,
                    Family = m.Family,
                    Label = m.Label,
                    Value = !string.IsNullOrWhiteSpace(fmt.Headline) ? fmt.Headline : (m.DefaultValue ?? "N/D"),
                    TechDetail = fmt.Tech,
                    IsPresent = present,
                    Detail = detail,
                    Summary = summary,
                    BenchScore = score,
                    Tier = PerformanceScorer.TierLabel(m.Id, score),
                    BenchLabel = m.BenchLabel,
                    Pros = pros.Select((p, i) => new ProItem { Index = (i + 1).ToString("D2"), Text = p }).ToList(),
                    IconData = vis.IconData,
                    AccentBrush = vis.AccentBrush,
                    AccentColor = vis.AccentColor
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

        // Texto del catálogo por id, para saber si la tienda editó el Summary/Pros de un componente.
        private static readonly Dictionary<string, SpecMarketingData> DefaultMarketingById =
            SpecCatalog.DefaultMarketing().Where(d => d.Id != null).ToDictionary(d => d.Id!, d => d);

        // Aplica el Summary narrado solo si sigue siendo el del catálogo (no editado por la tienda).
        private static string? PickSummary(SpecMarketingData m, SpecNarrator.Narration n)
        {
            if (n.Summary == null) return m.Summary;
            bool untouched = m.Id != null && DefaultMarketingById.TryGetValue(m.Id, out var def)
                             && string.Equals(m.Summary, def.Summary);
            return untouched ? n.Summary : m.Summary;
        }

        // Igual para los Pros: respeta la lista si la tienda la cambió respecto al catálogo.
        private static List<string> PickPros(SpecMarketingData m, SpecNarrator.Narration n)
        {
            if (n.Pros == null) return m.Pros;
            bool untouched = m.Id != null && DefaultMarketingById.TryGetValue(m.Id, out var def)
                             && m.Pros.SequenceEqual(def.Pros);
            return untouched ? n.Pros : m.Pros;
        }

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