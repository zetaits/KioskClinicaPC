using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Windows.Media;
using Microsoft.Win32;
using Newtonsoft.Json;
using KioskClinicaPC.Core;
using KioskClinicaPC.Controls;
using KioskClinicaPC.Windows;
using KioskClinicaPC.ViewModels;
using KioskClinicaPC.Services;
using KioskClinicaPC.Models;
using Serilog;

namespace KioskClinicaPC
{
    public partial class MainWindow : Window
    {
        private bool _isExitingSafely = false;
        private bool _scanning = false;       // evita secuencias de escaneo solapadas
        private bool _isNavigating = false;   // evita transiciones de pantalla re-entrantes
        private readonly KeyboardHook _hook;
        private readonly MainViewModel _viewModel;
        
        private DispatcherTimer _inactivityTimer;
        private DispatcherTimer _attractTimer;
        private DispatcherTimer _attractAutoScanTimer;
        private DispatcherTimer _highlightTimer;
        private DispatcherTimer _orbEpisodeTimer;   // dispara los "episodios" de giro del orbe en Attract
        
        private int _highlightIndex = 0;
        private int _attractSlideIndex = 0;
        private const int InactivityTimeoutSeconds = 90;

        private KioskSettings _settings = new KioskSettings();
        private int _hotspotClicks = 0;
        private readonly DispatcherTimer _hotspotResetTimer;

        public MainWindow()
        {
            InitializeComponent();

            _viewModel = new MainViewModel(new HardwareDiscoveryService());
            DataContext = _viewModel;

            _hook = new KeyboardHook();
            _inactivityTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(InactivityTimeoutSeconds) };
            _inactivityTimer.Tick += InactivityTimer_Tick;

            _attractTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5.2) };
            _attractTimer.Tick += AttractTimer_Tick;

            _attractAutoScanTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(18) };
            _attractAutoScanTimer.Tick += (s, e) =>
            {
                _attractAutoScanTimer.Stop();
                if (_viewModel.CurrentScreen == 0 && !EditModeService.Instance.IsActive) StartScanSequence();
            };

            _highlightTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4.5) };
            _highlightTimer.Tick += HighlightTimer_Tick;

            _orbEpisodeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(11) };
            _orbEpisodeTimer.Tick += (s, e) => PlayOrbEpisode();

            _hotspotResetTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            _hotspotResetTimer.Tick += (s, e) => { _hotspotResetTimer.Stop(); _hotspotClicks = 0; };
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
#if !DEBUG
            _hook.Start();
            AutostartRegistration.Register();
#endif
            _settings = KioskSettings.Load(App.SettingsFilePath);
            ApplyTimerIntervals();

            GraphicsQuality.Initialize(_settings.GraphicsMode);
            ParticleField.Spawn(ParticleCanvas, GraphicsQuality.ParticleCount);
            StartAmbientMotion();
            await _viewModel.LoadHardwareAndConfigAsync();
            RefreshQr();
            UpdateSlideDots(0);
            _attractTimer.Start();
            _attractAutoScanTimer.Start();
        }

        /// <summary>
        /// Arranca el movimiento ambiental: la deriva del blob de fondo (26 s, ida y vuelta) y el
        /// temporizador de "episodios" del orbe (en reposo el orbe está quieto; cada cierto tiempo gira
        /// una vuelta encogiéndose y atenuándose). La deriva se omite en modo gráfico ligero (un blob
        /// grande moviéndose cuesta en iGPU/software).
        /// </summary>
        private void StartAmbientMotion()
        {
            _orbEpisodeTimer.Start();

            if (GraphicsQuality.IsLow) return;   // blob estático en equipos débiles

            var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
            var dur = TimeSpan.FromSeconds(26);
            // Rango ≈ (−12%,−8%) → (16%,10%) de 1920×1080 → X −230→307, Y −86→108.
            var driftX = new DoubleAnimation(-230, 307, dur) { RepeatBehavior = RepeatBehavior.Forever, AutoReverse = true, EasingFunction = ease };
            var driftY = new DoubleAnimation(-86, 108, dur) { RepeatBehavior = RepeatBehavior.Forever, AutoReverse = true, EasingFunction = ease };
            DriftTransform.BeginAnimation(TranslateTransform.XProperty, driftX);
            DriftTransform.BeginAnimation(TranslateTransform.YProperty, driftY);
        }

        /// <summary>
        /// Episodio del orbe de Attract: parte del reposo, se encoge un poco, se atenúa (más tenue/fino)
        /// y da una vuelta completa con arranque/parada suaves; al terminar vuelve solo al reposo
        /// (FillBehavior.Stop). Solo si estamos en Attract y no en modo edición.
        /// </summary>
        private void PlayOrbEpisode()
        {
            if (_viewModel.CurrentScreen != 0 || EditModeService.Instance.IsActive) return;

            var dur = TimeSpan.FromSeconds(9);
            var easeOut = new CubicEase { EasingMode = EasingMode.EaseOut };
            var easeIn = new CubicEase { EasingMode = EasingMode.EaseIn };
            var easeInOut = new CubicEase { EasingMode = EasingMode.EaseInOut };

            // Una vuelta lenta con aceleración/frenada suaves; vuelve a 0° (mismo aspecto en reposo).
            var spin = new DoubleAnimation(0, 360, dur) { EasingFunction = easeInOut, FillBehavior = FillBehavior.Stop };
            AttractOrbTransform.BeginAnimation(RotateTransform.AngleProperty, spin);

            // Encoge a 0.85 durante el giro y recupera al final.
            var scale = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
            scale.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            scale.KeyFrames.Add(new EasingDoubleKeyFrame(0.85, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2)), easeOut));
            scale.KeyFrames.Add(new EasingDoubleKeyFrame(0.85, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(7))));
            scale.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(dur), easeIn));
            AttractOrbScale.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
            AttractOrbScale.BeginAnimation(ScaleTransform.ScaleYProperty, scale);

            // Aún más tenue durante el giro (reposo 0.32 → 0.16 → reposo).
            var fade = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
            fade.KeyFrames.Add(new EasingDoubleKeyFrame(0.32, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            fade.KeyFrames.Add(new EasingDoubleKeyFrame(0.16, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2)), easeOut));
            fade.KeyFrames.Add(new EasingDoubleKeyFrame(0.16, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(7))));
            fade.KeyFrames.Add(new EasingDoubleKeyFrame(0.32, KeyTime.FromTimeSpan(dur), easeIn));
            AttractOrb.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        /// <summary>
        /// Arranca el ping continuo de un blip del radar: el anillo crece de 10px a 64px (×6.4) y se
        /// desvanece, en bucle de 1.7 s. El desfase por blip (Pd, en el Tag) escalona los pings como en
        /// el mockup. Sin pings en modo gráfico ligero (el blip + lock-on quedan estáticos).
        /// </summary>
        private void ScanPingRing_Loaded(object sender, RoutedEventArgs e)
        {
            if (GraphicsQuality.IsLow) return;
            if (sender is not Ellipse ring) return;
            double delay = ring.Tag is double d ? d : 0;
            var begin = TimeSpan.FromSeconds(delay);
            var dur = TimeSpan.FromSeconds(1.7);

            // El anillo crece de 10→64px manteniendo el TRAZO FINO (1.5px) como el mockup (border constante).
            // Animar un ScaleTransform escalaría también el StrokeThickness (1.5→~9.6) → anillo gordo y borroso
            // (lo que se veía "sin pulir"). Animamos Width/Height + Canvas.Left/Top (re-centrado) en su lugar.
            // Ritmo "ping" del mockup: expande+desvanece hasta el 70% y queda invisible el 30% restante (pausa),
            // no un crecimiento continuo sin respiro.
            ring.RenderTransform = null;
            var easeOut = new KeySpline(0.2, 0.8, 0.2, 1);

            DoubleAnimationUsingKeyFrames Pulse(double from, double to)
            {
                var k = new DoubleAnimationUsingKeyFrames { Duration = dur, RepeatBehavior = RepeatBehavior.Forever, BeginTime = begin };
                k.KeyFrames.Add(new SplineDoubleKeyFrame(from, KeyTime.FromPercent(0)));
                k.KeyFrames.Add(new SplineDoubleKeyFrame(to, KeyTime.FromPercent(0.70), easeOut));
                k.KeyFrames.Add(new LinearDoubleKeyFrame(to, KeyTime.FromPercent(1)));
                return k;
            }

            var fade = new DoubleAnimationUsingKeyFrames { Duration = dur, RepeatBehavior = RepeatBehavior.Forever, BeginTime = begin };
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.9, KeyTime.FromPercent(0)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromPercent(0.70)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromPercent(1)));

            ring.BeginAnimation(FrameworkElement.WidthProperty, Pulse(10, 64));
            ring.BeginAnimation(FrameworkElement.HeightProperty, Pulse(10, 64));
            ring.BeginAnimation(Canvas.LeftProperty, Pulse(-5, -32));
            ring.BeginAnimation(Canvas.TopProperty, Pulse(-5, -32));
            ring.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        /// <summary>Dibuja el arco del anillo de progreso del radar (0–100 %) desde las 12 en sentido horario.</summary>
        private void UpdateScanProgressRing(double pct)
        {
            const double r = 394, cx = 410, cy = 410;  // lienzo 820, centro 410, radio 394 (margen holgado)
            pct = Math.Max(0, Math.Min(100, pct));
            if (pct <= 0) { ScanProgressRing.Data = null; return; }

            // A 100% un ArcSegment de ~360° con inicio≈fin es degenerado (WPF no resuelve el centro y
            // deja un hueco). Se dibuja un círculo completo con EllipseGeometry.
            if (pct >= 99.999)
            {
                ScanProgressRing.Data = new EllipseGeometry(new Point(cx, cy), r, r);
                return;
            }

            double sweep = pct / 100.0 * 360.0;
            double end = (-90 + sweep) * Math.PI / 180.0;
            var start = new Point(cx, cy - r);
            var endPt = new Point(cx + r * Math.Cos(end), cy + r * Math.Sin(end));

            var fig = new PathFigure { StartPoint = start, IsClosed = false };
            fig.Segments.Add(new ArcSegment(endPt, new Size(r, r), 0, sweep > 180, SweepDirection.Clockwise, true));
            var geo = new PathGeometry();
            geo.Figures.Add(fig);
            ScanProgressRing.Data = geo;
        }

        private void ApplyTimerIntervals()
        {
            _inactivityTimer.Interval = TimeSpan.FromSeconds(Math.Max(5, _settings.InactivitySeconds));
            _attractTimer.Interval = TimeSpan.FromSeconds(Math.Max(1, _settings.SlideIntervalSeconds));
            _attractAutoScanTimer.Interval = TimeSpan.FromSeconds(Math.Max(3, _settings.AutoScanSeconds));
        }

        /// <summary>URL de la web (GitHub Pages) que genera el PDF de la ficha. El QR apunta a "{url}#{datos}".</summary>
        private const string FichaPdfBaseUrl = "https://zetaits.github.io/KioskClinicaPC/";

        /// <summary>Genera el QR real con la ficha del equipo embebida (datos en el #hash de la URL).</summary>
        private void RefreshQr()
        {
            try
            {
                var specs = _viewModel.Specs.Select(s => new EquipmentPayload.SpecLine
                {
                    Id = s.Id,
                    Label = s.Label,
                    Value = s.Value,
                    Detail = s.Detail
                });

                string? url = EquipmentPayload.BuildUrl(FichaPdfBaseUrl, _viewModel.DisplayConfig, specs, shopName: null);
                if (url != null) Log.Information("QR payload longitud {Len} caracteres.", url.Length);
                var qr = QrGenerator.Generate(url);

                // Si el payload excede la capacidad del QR (ECC-L ~2.9KB), genera uno con la URL
                // base sin datos: al menos la landing es alcanzable en vez de quedarse sin QR.
                if (qr == null)
                {
                    Log.Warning("QR con datos embebidos falló (payload demasiado grande); usando URL base.");
                    qr = QrGenerator.Generate(FichaPdfBaseUrl);
                }

                QrImage.Source = qr;
                QrBorder.Visibility = qr != null ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "No se pudo refrescar el código QR de la ficha.");
                QrBorder.Visibility = Visibility.Collapsed;
            }
        }

        #region Navigation & Interactions

        private void AttractScreen_Click(object sender, MouseButtonEventArgs e)
        {
            if (EditModeService.Instance.IsActive) return;
            StartScanSequence();
        }

        private async void StartScanSequence()
        {
            // Disparable desde clic en atracción, timer de auto-escaneo y teclado. Sin este guard,
            // dos secuencias concurrentes duplicarían logs y navegarían dos veces.
            if (_scanning) return;
            _scanning = true;
            try
            {
            _attractTimer.Stop();
            _attractAutoScanTimer.Stop();
            NavigateToScreen(1); // Scan
            _viewModel.ScanLogs.Clear();
            ScanProgressText.Text = "000";
            UpdateScanProgressRing(0);
            foreach (var b in _viewModel.RadarBlips) b.IsDetected = false;  // reinicia lock-on

            // Sweep continuo a 1.7 s/vuelta durante el escaneo (mockup var-a).
            var spin = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.7)) { RepeatBehavior = RepeatBehavior.Forever };
            ScanRadarTransform.BeginAnimation(RotateTransform.AngleProperty, spin);

            var cfg = _viewModel.DisplayConfig;
            // Líneas del log (espejo de SCAN_LINES del mockup). El id liga la línea READ a su componente
            // para teñir el icono y enganchar la retícula lock-on de ese blip al detectarse.
            var logLines = new System.Collections.Generic.List<(string Step, string Color, string Id)> {
                ("INIT · ClinicaPC diagnostic v3.2.1", "Text1Brush", null),
                ("PROBE bios · OK", "Text1Brush", null),
                ($"DETECT chassis · {cfg.ChassisName} {cfg.ModelName}", "Text1Brush", null),
                ($"READ cpu  · {cfg.Cpu}", "Text1Brush", ComponentIds.Cpu),
                ($"READ gpu  · {cfg.Gpu}", "Text1Brush", ComponentIds.Gpu),
                ($"READ ram  · {cfg.Ram}", "Text1Brush", ComponentIds.Ram),
                ($"READ ssd  · {cfg.Storage}", "Text1Brush", ComponentIds.Storage),
                ($"READ disp · {cfg.Screen}", "Text1Brush", ComponentIds.Screen),
                ($"READ batt · {cfg.Battery}", "Text1Brush", ComponentIds.Battery),
                ($"READ wifi · {cfg.Wifi}", "Text1Brush", ComponentIds.Wifi),
                ($"READ cam  · {cfg.Camera}", "Text1Brush", ComponentIds.Camera),
                ("VERIFY    · grado A+", "Text1Brush", null),
                ("COMPLETE  · 100%", "OkBrush", null)
            };

            int total = logLines.Count;
            for(int i=0; i<total; i++)
            {
                await Task.Delay(300);
                var (step, color, id) = logLines[i];
                var spec = id != null ? _viewModel.Specs.FirstOrDefault(s => s.Id == id) : null;
                _viewModel.ScanLogs.Add(new ScanLogItem {
                    Time = (i+1).ToString("D2"),
                    Step = step,
                    Color = color,
                    IconData = spec?.IconData,
                    AccentBrush = spec?.AccentBrush
                });
                // Engancha el lock-on del blip correspondiente, en sincronía con el log.
                if (id != null)
                {
                    var blip = _viewModel.RadarBlips.FirstOrDefault(b => b.Id == id);
                    if (blip != null) blip.IsDetected = true;
                }
                int pct = ((i+1)*100)/total;
                ScanProgressText.Text = pct.ToString("D3");
                UpdateScanProgressRing(pct);
            }

            await Task.Delay(600);
            ScanRadarTransform.BeginAnimation(RotateTransform.AngleProperty, null);
            NavigateToScreen(2); // Main HUD
            }
            finally { _scanning = false; }
        }

        public void SpecNode_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is SpecItem item)
            {
                _viewModel.SelectedSpec = item;
                NavigateToScreen(3); // Detail
            }
        }

        private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".webp" };

        private void ProductImage_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = TryGetDroppedImagePath(e) != null ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void ProductImage_Drop(object sender, DragEventArgs e)
        {
            string? src = TryGetDroppedImagePath(e);
            if (src == null) return;
            try
            {
                string dest = System.IO.Path.Combine(App.AppDataFolderPath, "product" + System.IO.Path.GetExtension(src).ToLowerInvariant());
                File.Copy(src, dest, overwrite: true);
                // Clear first so the binding/converter reloads even when the path string is unchanged.
                _viewModel.SaveProductImage(null);
                _viewModel.SaveProductImage(dest);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "No se pudo guardar la foto del producto arrastrada.");
            }
            e.Handled = true;
        }

        private static string? TryGetDroppedImagePath(DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return null;
            if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return null;
            string f = files[0];
            return ImageExtensions.Contains(System.IO.Path.GetExtension(f).ToLowerInvariant()) ? f : null;
        }

        private void DetailBack_Click(object sender, RoutedEventArgs e)
        {
            NavigateToScreen(2);
        }

        private void DetailPrev_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel.SelectedSpec == null) return;
            int idx = _viewModel.Specs.IndexOf(_viewModel.SelectedSpec);
            idx = idx > 0 ? idx - 1 : _viewModel.Specs.Count - 1;
            _viewModel.SelectedSpec = _viewModel.Specs[idx];
        }

        private void DetailNext_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel.SelectedSpec == null) return;
            int idx = _viewModel.Specs.IndexOf(_viewModel.SelectedSpec);
            idx = idx < _viewModel.Specs.Count - 1 ? idx + 1 : 0;
            _viewModel.SelectedSpec = _viewModel.Specs[idx];
        }

        private void NavigateToScreen(int target)
        {
            if (target == _viewModel.CurrentScreen) return;
            // Si una transición está en curso, ignora: el handler Completed leía CurrentScreen y
            // una llamada re-entrante durante el fade (250ms) colapsaba la pantalla equivocada y
            // dejaba otra invisible. Capturamos 'from' y serializamos las transiciones.
            if (_isNavigating) return;
            _isNavigating = true;
            _inactivityTimer.Stop();
            FinalizeMorph();           // cancela cualquier morph en vuelo antes de cambiar de pantalla
            ResetEntranceVisuals();    // restaura el logo/scanline si una entrada quedó a medias

            int from = _viewModel.CurrentScreen;
            Grid[] screens = { Screen0_Attract, Screen1_Scan, Screen2_Main, Screen3_Detail };
            string[] names = { "MODO ESPERA", "ANÁLISIS DE SISTEMA", "RESUMEN DEL EQUIPO", "ESPECIFICACIONES" };

            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(250));
            fadeOut.Completed += (s, e) =>
            {
                screens[from].Visibility = Visibility.Collapsed;
                screens[target].Visibility = Visibility.Visible;
                _viewModel.CurrentScreenName = names[target];
                // Main (2) controla su propia opacidad vía la animación de entrada; el resto, fade genérico.
                if (target != 2)
                    screens[target].BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)));
                _viewModel.CurrentScreen = target;
                if (target > 0) { _inactivityTimer.Start(); _attractAutoScanTimer.Stop(); }
                else { _attractTimer.Start(); _attractAutoScanTimer.Stop(); _attractAutoScanTimer.Start(); }

                if (target == 2)
                {
                    _highlightTimer.Stop();
                    if (EditModeService.Instance.IsActive)
                    {
                        // En edición: HUD visible y estático, sin animación de entrada ni morph.
                        screens[target].BeginAnimation(OpacityProperty, null);
                        screens[target].Opacity = 1;
                        MainRevealScale.ScaleX = MainRevealScale.ScaleY = 1;
                        _viewModel.ActiveSpec ??= _viewModel.Specs.FirstOrDefault();
                    }
                    else
                    {
                        _highlightIndex = 0;
                        ApplyHighlight(0, animateMorph: false); // 1ª tarjeta ya resaltada al entrar (fix selección)
                        PlayMainEntrance();
                        _highlightTimer.Start();
                    }
                }
                else _highlightTimer.Stop();

                if (EditModeService.Instance.IsActive)
                {
                    _inactivityTimer.Stop();
                    _attractTimer.Stop();
                    _attractAutoScanTimer.Stop();
                    RefreshEditHighlights();
                }

                _isNavigating = false;
            };
            screens[from].BeginAnimation(OpacityProperty, fadeOut);
        }

        private void ResetInactivityTimer()
        {
            if (EditModeService.Instance.IsActive) return;
            if (_viewModel.CurrentScreen > 0)
            {
                _inactivityTimer.Stop();
                _inactivityTimer.Start();
            }
        }

        private void InactivityTimer_Tick(object sender, EventArgs e)
        {
            _inactivityTimer.Stop();
            NavigateToScreen(0);
        }

        #endregion

        #region Timers & Loops

        private void AttractTimer_Tick(object sender, EventArgs e)
        {
            if (_viewModel.Slides.Count == 0) return;
            _attractSlideIndex = (_attractSlideIndex + 1) % _viewModel.Slides.Count;
            _viewModel.CurrentSlide = _viewModel.Slides[_attractSlideIndex];
            UpdateSlideDots(_attractSlideIndex);
        }

        private void UpdateSlideDots(int index)
        {
            var dots = new[] { SlideDot0, SlideDot1, SlideDot2 };
            var cyanBrush = (SolidColorBrush)FindResource("CyanBrush");
            var cyanColor = (Color)FindResource("CyanColor");
            for (int i = 0; i < dots.Length; i++)
            {
                if (i == index)
                {
                    dots[i].Width = 32;
                    dots[i].Fill = cyanBrush;
                    var glow = new DropShadowEffect { Color = cyanColor, BlurRadius = 10, ShadowDepth = 0, Opacity = 0.8 };
                    glow.Freeze();
                    dots[i].Effect = glow;
                }
                else
                {
                    dots[i].Width = 8;
                    var dim = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
                    dim.Freeze();
                    dots[i].Fill = dim;
                    dots[i].Effect = null;
                }
            }
        }

        private void HighlightTimer_Tick(object sender, EventArgs e)
        {
            ApplyHighlight(_highlightIndex, animateMorph: true);
        }

        /// <summary>Resalta la tarjeta <paramref name="index"/> y actualiza el spotlight central.
        /// Con <paramref name="animateMorph"/>=true, la tarjeta "vuela" y escala hasta el spotlight
        /// (shared-element) y es el morph quien fija ActiveSpec al aterrizar para un crossfade real.</summary>
        private void ApplyHighlight(int index, bool animateMorph)
        {
            if (_viewModel.Specs == null || _viewModel.Specs.Count == 0) return;
            index %= _viewModel.Specs.Count;
            foreach (var spec in _viewModel.Specs) spec.IsHighlighted = false;
            var active = _viewModel.Specs[index];
            active.IsHighlighted = true;
            _highlightIndex = (index + 1) % _viewModel.Specs.Count;

            // Movimiento tarjeta→spotlight (card morph) retirado a petición: la tarjeta ya no "vuela"
            // hasta la imagen. El spotlight cambia con crossfade en XAML (TargetUpdated). Flag a true
            // para restaurar el morph.
            if (animateMorph && EnableCardMorph && TryRunCardMorph(active)) return;
            _viewModel.ActiveSpec = active; // el spotlight central anima en XAML vía TargetUpdated
        }

        #endregion

        #region Card morph (shared-element tarjeta → spotlight)

        // Movimiento de la tarjeta volando al spotlight. Desactivado a petición (cambio = crossfade seco).
        private const bool EnableCardMorph = false;
        private bool _morphActive;
        private FrameworkElement _morphCard;
        private DispatcherTimer _morphRevealTimer;
        private SpecItem _morphPendingSpec;

        /// <summary>Anima un clon congelado de la tarjeta seleccionada trasladándose y escalándose
        /// desde la cuadrícula inferior hasta el marco del spotlight. Devuelve false si no procede
        /// (no estamos en Main, modo edición, contenedor sin realizar) para que el llamador haga el
        /// cambio de spotlight directo.</summary>
        private bool TryRunCardMorph(SpecItem spec)
        {
            if (spec == null || _viewModel.CurrentScreen != 2 || EditModeService.Instance.IsActive) return false;
            if (MorphLayer == null || MorphGhost == null || SpotlightFrame == null || SpecTiles == null) return false;

            var card = SpecTiles.ItemContainerGenerator.ContainerFromItem(spec) as FrameworkElement;
            if (card == null || card.ActualWidth < 1 || card.ActualHeight < 1) return false;

            FinalizeMorph(); // si hubiera uno en curso, ciérralo limpio

            Rect src, dst;
            try
            {
                src = card.TransformToVisual(MorphLayer).TransformBounds(new Rect(0, 0, card.ActualWidth, card.ActualHeight));
                dst = SpotlightFrame.TransformToVisual(MorphLayer).TransformBounds(new Rect(0, 0, SpotlightFrame.ActualWidth, SpotlightFrame.ActualHeight));
            }
            catch { return false; }
            if (src.Width < 1 || src.Height < 1 || dst.Width < 1 || dst.Height < 1) return false;

            // Efecto ABSTRACTO: un panel de luz con el color de acento del componente se "eleva" de la
            // tarjeta y se EXPANDE llenando el marco del spotlight, disolviéndose al llegar mientras la
            // imagen real emerge. No es contenido real (ni icono ni textos): solo el gesto del traslado.
            var accent = spec.AccentColor;
            // Arranca como un RECTÁNGULO sólido (como la tarjeta). Una OpacityMask radial erosiona su
            // zona visible HACIA DENTRO conforme llega → se difumina haciéndose cada vez más pequeña.
            MorphGhost.Background = new SolidColorBrush(Color.FromArgb(0x82, accent.R, accent.G, accent.B));
            MorphGhost.CornerRadius = new CornerRadius(8);
            // El ghost se traslada+escala durante 0.55s; un BlurEffect encima es un shader recalculado por
            // frame mientras se mueve. En modo ligero se omite (el morph sigue, solo sin desenfoque suave).
            MorphGhost.Effect = GraphicsQuality.IsLow ? null : new BlurEffect { Radius = 6, KernelType = KernelType.Gaussian };

            var maskInner = new GradientStop(Colors.White, 1.0);
            var maskOuter = new GradientStop(Color.FromArgb(0, 0xFF, 0xFF, 0xFF), 1.2);
            var mask = new RadialGradientBrush { Center = new Point(0.5, 0.5), GradientOrigin = new Point(0.5, 0.5), RadiusX = 0.75, RadiusY = 0.75 };
            mask.GradientStops.Add(maskInner);
            mask.GradientStops.Add(maskOuter);
            MorphGhost.OpacityMask = mask;

            MorphGhost.Width = src.Width;
            MorphGhost.Height = src.Height;
            Canvas.SetLeft(MorphGhost, src.X);
            Canvas.SetTop(MorphGhost, src.Y);
            MorphGhost.Opacity = 0;
            MorphGhostScale.ScaleX = MorphGhostScale.ScaleY = 1;
            MorphGhostTranslate.X = MorphGhostTranslate.Y = 0;
            MorphGhost.Visibility = Visibility.Visible;

            _morphCard = null; // no atenuamos ni movemos nada real
            _morphActive = true;
            _morphPendingSpec = spec;

            // Oculta el contenido previo del spotlight durante el traslado.
            SpotlightContent.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromSeconds(0.14)));

            // El panel crece hacia el marco pero SIN llenarlo del todo (~60%, centrado): llega más
            // contenido y deja respirar el área de la imagen.
            const double dur = 0.55, fillFactor = 0.62;
            double finalW = dst.Width * fillFactor, finalH = dst.Height * fillFactor;
            double sx = finalW / src.Width, sy = finalH / src.Height;
            double tx = (dst.X + dst.Width / 2 - finalW / 2) - src.X;
            double ty = (dst.Y + dst.Height / 2 - finalH / 2) - src.Y;
            var glide = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 5 };
            MorphGhostScale.BeginAnimation(ScaleTransform.ScaleXProperty, Anim(1, sx, dur, glide));
            MorphGhostScale.BeginAnimation(ScaleTransform.ScaleYProperty, Anim(1, sy, dur, glide));
            MorphGhostTranslate.BeginAnimation(TranslateTransform.XProperty, Anim(0, tx, dur, glide));
            var fly = Anim(0, ty, dur, glide);
            fly.Completed += (s, e) => FinalizeMorph();
            MorphGhostTranslate.BeginAnimation(TranslateTransform.YProperty, fly);

            // Erosión hacia dentro: el disco visible encoge desde los bordes al centro (feather suave).
            // Empieza pronto (0.10s) y TERMINA antes de aterrizar (0.40s): cuando el panel llega al
            // centro ya está prácticamente desaparecido (no un rectángulo que erosiona al final).
            const double erodeBegin = 0.10, erodeEnd = 0.40, erodeDur = erodeEnd - erodeBegin;
            maskInner.BeginAnimation(GradientStop.OffsetProperty, Anim(1.0, 0.0, erodeDur, null, erodeBegin));
            maskOuter.BeginAnimation(GradientStop.OffsetProperty, Anim(1.2, 0.22, erodeDur, null, erodeBegin));

            // Pulso de luz: sube rápido y se disuelve conforme llena el marco (no queda nada nítido encima).
            var ghostFade = new DoubleAnimationUsingKeyFrames();
            ghostFade.KeyFrames.Add(new SplineDoubleKeyFrame(0.85, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.08)), new KeySpline(0.2, 0, 0.4, 1)));
            ghostFade.KeyFrames.Add(new LinearDoubleKeyFrame(0.6, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.24))));
            ghostFade.KeyFrames.Add(new SplineDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.40)), new KeySpline(0.4, 0, 1, 1)));
            MorphGhost.BeginAnimation(OpacityProperty, ghostFade);

            // El spec real emerge PRONTO, mientras la nube de luz aún se expande/disuelve por encima
            // (translúcida y difusa → la imagen se materializa "dentro" de la energía).
            _morphRevealTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(0.20) };
            _morphRevealTimer.Tick += (s, e) =>
            {
                _morphRevealTimer.Stop();
                // Cambiar ActiveSpec dispara la entrada del spotlight (From=0 To=1), que reemplaza
                // limpiamente la animación manual de ocultado (también en 0) → crossfade sin parpadeo.
                _viewModel.ActiveSpec = _morphPendingSpec;
            };
            _morphRevealTimer.Start();
            return true;
        }

        /// <summary>Cierra el morph (normal o interrumpido): garantiza el spotlight correcto,
        /// restaura la opacidad de la tarjeta y oculta el clon.</summary>
        private void FinalizeMorph()
        {
            bool revealPending = _morphRevealTimer != null && _morphRevealTimer.IsEnabled;
            if (_morphRevealTimer != null) { _morphRevealTimer.Stop(); _morphRevealTimer = null; }
            if (_morphActive && _morphPendingSpec != null && !ReferenceEquals(_viewModel.ActiveSpec, _morphPendingSpec))
                _viewModel.ActiveSpec = _morphPendingSpec; // dispara la entrada del spotlight (override del ocultado)
            else if (revealPending && SpotlightContent != null)
                SpotlightContent.BeginAnimation(OpacityProperty, null); // interrupción tras revelar: asegura visible
            if (_morphCard != null) { _morphCard.Opacity = 1; _morphCard = null; }
            if (MorphGhost != null)
            {
                MorphGhostScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                MorphGhostScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                MorphGhostTranslate.BeginAnimation(TranslateTransform.XProperty, null);
                MorphGhostTranslate.BeginAnimation(TranslateTransform.YProperty, null);
                MorphGhost.BeginAnimation(OpacityProperty, null);
                MorphGhost.Visibility = Visibility.Collapsed;
                MorphGhost.Background = null;
                MorphGhost.OpacityMask = null;
                MorphGhost.Effect = null;
                MorphGhost.Opacity = 1;
            }
            _morphActive = false;
            _morphPendingSpec = null;
        }

        #endregion

        #region Main entrance (animación de entrada al HUD)

        private static DoubleAnimation Anim(double from, double to, double seconds, IEasingFunction ease = null, double beginSeconds = 0)
        {
            var a = new DoubleAnimation(from, to, TimeSpan.FromSeconds(seconds));
            if (ease != null) a.EasingFunction = ease;
            if (beginSeconds > 0) a.BeginTime = TimeSpan.FromSeconds(beginSeconds);
            return a;
        }

        /// <summary>Lanza la animación de entrada a Main según ajuste (o ciclando las tres).</summary>
        private void PlayMainEntrance()
        {
            // El HUD parte oculto; la variante elegida controla su revelado.
            Screen2_Main.BeginAnimation(OpacityProperty, null);
            Screen2_Main.Opacity = 0;
            MainRevealScale.ScaleX = MainRevealScale.ScaleY = 1;

            // Solo se conserva la entrada "Iris" (el logo aparece grande y viaja a su esquina). La
            // segunda variante "ZoomThrough" (el logo atraviesa al espectador) se retiró a petición,
            // así que ya no se cicla ni se mira MainEntranceStyle: siempre Iris.
            // Diferir a Loaded: garantiza layout/contenedores realizados para geometría y stagger.
            Dispatcher.BeginInvoke(new Action(EntranceIris), DispatcherPriority.Loaded);
        }

        /// <summary>Variante "el logo abre la ventana": el logo aparece grande y centrado, viaja a su
        /// esquina mientras el HUD se revela desde el centro con tarjetas escalonadas.</summary>
        private void EntranceIris()
        {
            var easeOut = new CubicEase { EasingMode = EasingMode.EaseOut };
            var back = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 };

            HeaderLogo.Opacity = 0; // se revela al terminar el viaje
            var (tx, ty, scale) = ComputeLogoDock();

            EntranceLogo.Visibility = Visibility.Visible;
            EntranceLogoScale.ScaleX = EntranceLogoScale.ScaleY = 2.4;
            EntranceLogoTranslate.X = EntranceLogoTranslate.Y = 0;
            EntranceLogo.BeginAnimation(OpacityProperty, Anim(0, 1, 0.25, easeOut));

            EntranceLogoScale.BeginAnimation(ScaleTransform.ScaleXProperty, Anim(2.4, scale, 0.6, back, 0.30));
            EntranceLogoScale.BeginAnimation(ScaleTransform.ScaleYProperty, Anim(2.4, scale, 0.6, back, 0.30));
            EntranceLogoTranslate.BeginAnimation(TranslateTransform.XProperty, Anim(0, tx, 0.6, back, 0.30));
            var travelY = Anim(0, ty, 0.6, back, 0.30);
            travelY.Completed += (s, e) =>
            {
                HeaderLogo.Opacity = 1;
                EntranceLogo.Visibility = Visibility.Hidden;
                EntranceLogo.BeginAnimation(OpacityProperty, null);
            };
            EntranceLogoTranslate.BeginAnimation(TranslateTransform.YProperty, travelY);

            Screen2_Main.BeginAnimation(OpacityProperty, Anim(0, 1, 0.5, easeOut, 0.25));
            MainRevealScale.ScaleX = MainRevealScale.ScaleY = 0.92;
            MainRevealScale.BeginAnimation(ScaleTransform.ScaleXProperty, Anim(0.92, 1, 0.6, back, 0.25));
            MainRevealScale.BeginAnimation(ScaleTransform.ScaleYProperty, Anim(0.92, 1, 0.6, back, 0.25));

            StaggerTiles(0.45);
        }

        /// <summary>Entrada escalonada de las tarjetas (translate-up), 45 ms entre cada una.</summary>
        private void StaggerTiles(double startSeconds)
        {
            if (SpecTiles?.Items == null) return;
            var easeOut = new CubicEase { EasingMode = EasingMode.EaseOut };
            for (int i = 0; i < SpecTiles.Items.Count; i++)
            {
                if (SpecTiles.ItemContainerGenerator.ContainerFromIndex(i) is FrameworkElement fe)
                {
                    var tt = new TranslateTransform(0, 22);
                    fe.RenderTransform = tt;
                    tt.BeginAnimation(TranslateTransform.YProperty, Anim(22, 0, 0.4, easeOut, startSeconds + i * 0.045));
                }
            }
        }

        /// <summary>Restaura el logo de cabecera y oculta los visuales de entrada (por si una
        /// animación quedó a medias al navegar fuera de Main).</summary>
        private void ResetEntranceVisuals()
        {
            if (HeaderLogo != null)
            {
                HeaderLogo.BeginAnimation(OpacityProperty, null);
                HeaderLogo.Opacity = 1;
            }
            if (EntranceLogo != null)
            {
                EntranceLogo.BeginAnimation(OpacityProperty, null);
                EntranceLogoScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                EntranceLogoScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                EntranceLogoTranslate.BeginAnimation(TranslateTransform.XProperty, null);
                EntranceLogoTranslate.BeginAnimation(TranslateTransform.YProperty, null);
                EntranceLogo.Visibility = Visibility.Hidden;
                EntranceLogo.Opacity = 0;
                EntranceLogoScale.ScaleX = EntranceLogoScale.ScaleY = 1;
                EntranceLogoTranslate.X = EntranceLogoTranslate.Y = 0;
            }
        }

        /// <summary>Traslación (centro→esquina) y escala para que EntranceLogo encaje sobre HeaderLogo.</summary>
        private (double tx, double ty, double scale) ComputeLogoDock()
        {
            try
            {
                var hb = HeaderLogo.TransformToVisual(EntranceLayer)
                         .TransformBounds(new Rect(0, 0, HeaderLogo.ActualWidth, HeaderLogo.ActualHeight));
                double headerCx = hb.X + hb.Width / 2, headerCy = hb.Y + hb.Height / 2;
                double entCx = EntranceLayer.ActualWidth / 2, entCy = EntranceLayer.ActualHeight / 2;
                double scale = EntranceLogo.ActualHeight > 1 ? hb.Height / EntranceLogo.ActualHeight : 1.0;
                return (headerCx - entCx, headerCy - entCy, scale);
            }
            catch { return (-828, -466, 1.0); } // fallback aproximado: hacia arriba-izquierda
        }

        #endregion

        #region Window Events

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (!_isExitingSafely) e.Cancel = true;
            else { _hook.Stop(); _inactivityTimer.Stop(); _highlightTimer.Stop(); _attractTimer.Stop(); _attractAutoScanTimer.Stop(); }
        }

        public void ShutdownKiosk()
        {
            _isExitingSafely = true;
            Application.Current.Shutdown();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            ResetInactivityTimer();
            if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
            {
                if (e.Key == Key.K) ShutdownKiosk();
                if (e.Key == Key.S) OpenSettingsDialog();
                if (e.Key == Key.P)
                {
                    KeyboardHook.AllowWindowsKey = !KeyboardHook.AllowWindowsKey;
                    KioskDialog.Alert(this, "Capturas",
                        KeyboardHook.AllowWindowsKey
                            ? "Tecla Windows DESBLOQUEADA. Win+Impr Pant guardará captura en Imágenes\\Capturas de pantalla."
                            : "Tecla Windows BLOQUEADA.");
                }
            }
            if (e.Key == Key.Escape && _viewModel.CurrentScreen > 0 && !EditModeService.Instance.IsActive) NavigateToScreen(0);

            // En la pantalla de atracción, cualquier tecla (no modificadora) inicia el escaneo, igual que un clic.
            if (_viewModel.CurrentScreen == 0 && !EditModeService.Instance.IsActive
                && Keyboard.Modifiers == ModifierKeys.None
                && e.Key != Key.System && e.Key != Key.LeftCtrl && e.Key != Key.RightCtrl
                && e.Key != Key.LeftShift && e.Key != Key.RightShift
                && e.Key != Key.LWin && e.Key != Key.RWin)
            {
                StartScanSequence();
            }
        }

        private void SettingsHotspot_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (EditModeService.Instance.IsActive) return;
            _hotspotClicks++;
            _hotspotResetTimer.Stop();
            _hotspotResetTimer.Start();
            if (_hotspotClicks >= 3)
            {
                _hotspotClicks = 0;
                _hotspotResetTimer.Stop();
                OpenSettingsDialog();
            }
        }

        private void Window_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!EditModeService.Instance.IsActive) return;
            if (InlineEditController.TryBeginEdit(e.OriginalSource)) e.Handled = true;
        }

        // En un kiosco táctil sin teclado, la actividad del cliente llega por toque/ratón, no por
        // tecla. Estos handlers resetean el timer de inactividad para no expulsar a la pantalla de
        // atracción a alguien que está leyendo la ficha. No marcan el evento como manejado.
        private void Window_PreviewActivity(object sender, MouseButtonEventArgs e) => ResetInactivityTimer();

        private void Window_PreviewTouchActivity(object sender, TouchEventArgs e) => ResetInactivityTimer();

        #region Edit Mode

        private void EnterEditMode()
        {
            _inactivityTimer.Stop();
            _attractTimer.Stop();
            _attractAutoScanTimer.Stop();
            _highlightTimer.Stop();
            foreach (var s in _viewModel.Specs) s.IsHighlighted = false;

            EditModeService.Instance.IsDirty = false;
            EditModeService.Instance.IsActive = true;
            RefreshEditHighlights();
        }

        private void ExitEditMode()
        {
            InlineEditController.CommitActive();
            InlineEditController.SetHighlights(RootGrid, false);
            EditModeService.Instance.IsActive = false;

            if (_viewModel.CurrentScreen == 0) { _attractTimer.Start(); _attractAutoScanTimer.Start(); }
            else { _inactivityTimer.Start(); }
            if (_viewModel.CurrentScreen == 2) _highlightTimer.Start();

            RefreshQr(); // los datos editados (precio, specs) pueden haber cambiado
        }

        private void RefreshEditHighlights()
        {
            if (!EditModeService.Instance.IsActive) return;
            Dispatcher.BeginInvoke(new Action(() => InlineEditController.SetHighlights(RootGrid, true)), DispatcherPriority.Loaded);
        }

        private void EditSave_Click(object sender, RoutedEventArgs e)
        {
            InlineEditController.CommitActive();
            try
            {
                _viewModel.SaveEdits();
                RefreshEditHighlights();
            }
            catch (Exception ex)
            {
                KioskDialog.Alert(this, "Error", $"No se pudieron guardar los cambios.\n{ex.Message}", danger: true);
            }
        }

        private void EditDiscard_Click(object sender, RoutedEventArgs e)
        {
            InlineEditController.CancelActive();
            _viewModel.DiscardEdits();
            if (_viewModel.CurrentScreen == 3) NavigateToScreen(2);
            _attractSlideIndex = 0;
            UpdateSlideDots(0);
            RefreshEditHighlights();
        }

        private void EditExit_Click(object sender, RoutedEventArgs e)
        {
            if (EditModeService.Instance.IsDirty)
            {
                var r = KioskDialog.Show(this, "Modo edición", "Tienes cambios sin guardar.",
                    primaryText: "Guardar y salir", secondaryText: "Salir sin guardar", cancelText: "Cancelar");
                if (r == KioskDialogResult.Cancel) return;
                if (r == KioskDialogResult.Primary)
                {
                    InlineEditController.CommitActive();
                    try { _viewModel.SaveEdits(); }
                    catch (Exception ex)
                    {
                        KioskDialog.Alert(this, "Error", $"No se pudieron guardar los cambios.\n{ex.Message}", danger: true);
                        return;
                    }
                }
                else
                {
                    InlineEditController.CancelActive();
                    _viewModel.DiscardEdits();
                    if (_viewModel.CurrentScreen == 3) NavigateToScreen(2);
                }
            }
            ExitEditMode();
        }

        private void EditPrevSlide_Click(object sender, RoutedEventArgs e) => StepSlide(-1);
        private void EditNextSlide_Click(object sender, RoutedEventArgs e) => StepSlide(1);

        // Alterna el plazo de cuotas (6 ⇄ 12 meses) mostrado en la ficha de precio.
        private void InstallmentsToggle_Click(object sender, RoutedEventArgs e) => _viewModel.ToggleInstallments();

        private void StepSlide(int dir)
        {
            if (_viewModel.Slides.Count == 0) return;
            int idx = _viewModel.Slides.IndexOf(_viewModel.CurrentSlide);
            if (idx < 0) idx = 0;
            idx = (idx + dir + _viewModel.Slides.Count) % _viewModel.Slides.Count;
            _viewModel.CurrentSlide = _viewModel.Slides[idx];
            _attractSlideIndex = idx;
            UpdateSlideDots(idx);
            RefreshEditHighlights();
        }

        // Navegación entre pantallas dentro del modo edición (sin estos botones el usuario
        // queda atascado en la pantalla activa y no puede llegar a editar las demás).
        private void EditGoAttract_Click(object sender, RoutedEventArgs e) => GoToScreenInEdit(0);
        private void EditGoScan_Click(object sender, RoutedEventArgs e) => GoToScreenInEdit(1);
        private void EditGoMain_Click(object sender, RoutedEventArgs e) => GoToScreenInEdit(2);
        private void EditGoDetail_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.SelectedSpec ??= _viewModel.Specs.FirstOrDefault();
            GoToScreenInEdit(3);
        }

        private void GoToScreenInEdit(int target)
        {
            if (_viewModel.CurrentScreen == target) { RefreshEditHighlights(); return; }
            NavigateToScreen(target);
        }

        #endregion

        private async void OpenSettingsDialog()
        {
            // Pausa el render del kiosko (animaciones/blur) mientras el panel está abierto → scroll fluido.
            RootGrid.Visibility = Visibility.Collapsed;
            bool launchEdit = false;
            try
            {
                if (new PasswordDialog { Owner = this }.ShowDialog() != true) return;

                var settingsWindow = new SettingsWindow(_viewModel.DetectedSpecs) { Owner = this };
                bool? result = settingsWindow.ShowDialog();

                // Recarga ajustes y reaplica intervalos (timeouts/contraseña pueden haber cambiado).
                _settings = KioskSettings.Load(App.SettingsFilePath);
                ApplyTimerIntervals();

                if (result == true)
                    await _viewModel.LoadHardwareAndConfigAsync();

                RefreshQr(); // PdfBaseUrl o specs pueden haber cambiado
                launchEdit = settingsWindow.LaunchEditMode;
            }
            finally
            {
                RootGrid.Visibility = Visibility.Visible;
            }

            if (launchEdit)
                EnterEditMode();
        }

        #endregion
    }
}