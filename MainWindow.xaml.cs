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
        
        private readonly KioskTimers _timers = new KioskTimers();

        private int _highlightIndex = 0;
        private int _attractSlideIndex = 0;
        private int _mainShown = 0;        // specs ya mostrados en la pasada actual de Main
        private int _detailTourIndex = 0;  // índice del recorrido automático por Detail
        private bool _autoTour = false;    // true mientras el bucle conduce la pantalla Detail

        private KioskSettings _settings = new KioskSettings();
        private int _hotspotClicks = 0;

        public MainWindow(MainViewModel viewModel)
        {
            InitializeComponent();

            _viewModel = viewModel;
            DataContext = _viewModel;

            _hook = new KeyboardHook();

            // Toda la lógica de DispatcherTimer (intervalos, arranque/paro, one-shot) vive en KioskTimers.
            // Aquí solo se cablea QUÉ hace cada tick; KioskTimers decide CUÁNDO/CÓMO.
            _timers.Inactivity = () => NavigateToScreen(0);
            _timers.AttractAdvance = AdvanceAttractSlide;
            _timers.AutoScan = () =>
            {
                if (_viewModel.CurrentScreen == 0 && !EditModeService.Instance.IsActive) StartScanSequence();
            };
            // Main resalta cada spec una vez; tras la última pasada arranca el recorrido por Detail.
            _timers.Highlight = () =>
            {
                if (_viewModel.Specs.Count == 0) return;
                if (_mainShown >= _viewModel.Specs.Count) { StartDetailTour(); return; }
                ApplyHighlight(_highlightIndex, animateMorph: true);
                _mainShown++;
            };
            _timers.DetailAdvance = AdvanceDetailTour;
            _timers.OrbEpisode = PlayOrbEpisode;
            _timers.HotspotReset = () => _hotspotClicks = 0;
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
            EnterAttractMode();
        }

        private void ApplyTimerIntervals() => _timers.ApplyIntervals(_settings);

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
            _timers.Stop(KioskTimer.AttractAdvance);
            _timers.Stop(KioskTimer.AutoScan);
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
            var logLines = new System.Collections.Generic.List<(string Step, string Color, string? Id)> {
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
                _autoTour = false; // entrada manual: no auto-avanza, el usuario navega a su ritmo
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
            EndAutoTour();
            NavigateToScreen(2);
        }

        private void DetailPrev_Click(object sender, RoutedEventArgs e)
        {
            EndAutoTour();
            if (_viewModel.SelectedSpec == null) return;
            int idx = _viewModel.Specs.IndexOf(_viewModel.SelectedSpec);
            idx = idx > 0 ? idx - 1 : _viewModel.Specs.Count - 1;
            _viewModel.SelectedSpec = _viewModel.Specs[idx];
        }

        private void DetailNext_Click(object sender, RoutedEventArgs e)
        {
            EndAutoTour();
            if (_viewModel.SelectedSpec == null) return;
            int idx = _viewModel.Specs.IndexOf(_viewModel.SelectedSpec);
            idx = idx < _viewModel.Specs.Count - 1 ? idx + 1 : 0;
            _viewModel.SelectedSpec = _viewModel.Specs[idx];
        }

        /// <summary>El usuario tomó el control en Detail: corta el auto-avance del recorrido.</summary>
        private void EndAutoTour()
        {
            _autoTour = false;
            _timers.Stop(KioskTimer.DetailAdvance);
        }

        private void NavigateToScreen(int target)
        {
            if (target == _viewModel.CurrentScreen) return;
            // Si una transición está en curso, ignora: el handler Completed leía CurrentScreen y
            // una llamada re-entrante durante el fade (250ms) colapsaba la pantalla equivocada y
            // dejaba otra invisible. Capturamos 'from' y serializamos las transiciones.
            if (_isNavigating) return;
            _isNavigating = true;
            _timers.Stop(KioskTimer.Inactivity);
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
                if (target > 0) { _timers.Start(KioskTimer.Inactivity); _timers.Stop(KioskTimer.AutoScan); }
                else { EnterAttractMode(); }

                if (target == 2)
                {
                    _timers.Stop(KioskTimer.Highlight);
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
                        _mainShown = 1;                         // spec 0 ya mostrado; el resto, por timer
                        PlayMainEntrance();
                        _timers.Start(KioskTimer.Highlight);
                    }
                }
                else _timers.Stop(KioskTimer.Highlight);

                // Recorrido automático por Detail: solo cuando lo conduce el bucle (no entrada manual).
                if (target == 3 && _autoTour && !EditModeService.Instance.IsActive)
                    _timers.Restart(KioskTimer.DetailAdvance);
                else
                    _timers.Stop(KioskTimer.DetailAdvance);

                if (EditModeService.Instance.IsActive)
                {
                    _timers.Stop(KioskTimer.Inactivity);
                    _timers.Stop(KioskTimer.AttractAdvance);
                    _timers.Stop(KioskTimer.AutoScan);
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
                _timers.Restart(KioskTimer.Inactivity);
        }

        #endregion

        #region Timers & Loops

        /// <summary>Entra en modo espera (Attract): reinicia el recorrido de slides desde el primero.
        /// El bucle muestra todos los textos una vez y luego pasa a Scan→Main automáticamente.</summary>
        private void EnterAttractMode()
        {
            _autoTour = false;
            _attractSlideIndex = 0;
            if (_viewModel.Slides.Count > 0)
            {
                _viewModel.CurrentSlide = _viewModel.Slides[0];
                UpdateSlideDots(0);
            }
            _timers.Restart(KioskTimer.AttractAdvance);
            // AutoScan solo como red de seguridad cuando no hay slides que conduzcan la transición.
            if (_viewModel.Slides.Count == 0) _timers.Restart(KioskTimer.AutoScan);
        }

        private void AdvanceAttractSlide()
        {
            if (_viewModel.Slides.Count == 0) return;
            int next = _attractSlideIndex + 1;
            // Mostrados todos los textos de Attract → arranca el recorrido (Scan → Main).
            if (next >= _viewModel.Slides.Count) { StartScanSequence(); return; }
            _attractSlideIndex = next;
            _viewModel.CurrentSlide = _viewModel.Slides[_attractSlideIndex];
            UpdateSlideDots(_attractSlideIndex);
        }

        /// <summary>Fin de la pasada por Main: arranca el recorrido automático por la pantalla Detail.</summary>
        private void StartDetailTour()
        {
            _timers.Stop(KioskTimer.Highlight);
            if (EditModeService.Instance.IsActive || _viewModel.Specs.Count == 0) { NavigateToScreen(0); return; }
            _autoTour = true;
            _detailTourIndex = 0;
            _viewModel.SelectedSpec = _viewModel.Specs[0];
            NavigateToScreen(3); // el timer DetailAdvance arranca al entrar (ver NavigateToScreen)
        }

        /// <summary>Avanza al Detail del siguiente spec; al terminar la vuelta, reinicia el bucle en Attract.</summary>
        private void AdvanceDetailTour()
        {
            if (_viewModel.CurrentScreen != 3 || _viewModel.Specs.Count == 0) return;
            int next = _detailTourIndex + 1;
            if (next >= _viewModel.Specs.Count) { _autoTour = false; NavigateToScreen(0); return; }
            _detailTourIndex = next;
            _viewModel.SelectedSpec = _viewModel.Specs[_detailTourIndex];
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

        #region Window Events

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (!_isExitingSafely) e.Cancel = true;
            else { _hook.Stop(); _timers.StopAll(); }
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

            // En Detalle, cualquier tecla (no modificadora, salvo Escape) vuelve al resumen (Main).
            if (_viewModel.CurrentScreen == 3 && !EditModeService.Instance.IsActive
                && Keyboard.Modifiers == ModifierKeys.None
                && e.Key != Key.Escape
                && e.Key != Key.System && e.Key != Key.LeftCtrl && e.Key != Key.RightCtrl
                && e.Key != Key.LeftShift && e.Key != Key.RightShift
                && e.Key != Key.LWin && e.Key != Key.RWin)
            {
                EndAutoTour();
                NavigateToScreen(2);
            }
        }

        private void SettingsHotspot_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (EditModeService.Instance.IsActive) return;
            _hotspotClicks++;
            _timers.Restart(KioskTimer.HotspotReset);
            if (_hotspotClicks >= 3)
            {
                _hotspotClicks = 0;
                _timers.Stop(KioskTimer.HotspotReset);
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