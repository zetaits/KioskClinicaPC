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

            _highlightTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _highlightTimer.Tick += HighlightTimer_Tick;

            _hotspotResetTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            _hotspotResetTimer.Tick += (s, e) => { _hotspotResetTimer.Stop(); _hotspotClicks = 0; };
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
#if !DEBUG
            _hook.Start();
            RegisterInStartup();
#endif
            _settings = KioskSettings.Load(App.SettingsFilePath);
            ApplyTimerIntervals();

            SpawnParticles(26);
            await _viewModel.LoadHardwareAndConfigAsync();
            RefreshQr();
            UpdateSlideDots(0);
            _attractTimer.Start();
            _attractAutoScanTimer.Start();
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

        private void SpawnParticles(int count)
        {
            var random = new Random();
            for (int i = 0; i < count; i++)
            {
                double size = 1 + random.NextDouble() * 2.5;
                var dot = new Ellipse
                {
                    Width = size,
                    Height = size,
                    Fill = random.NextDouble() > 0.5 ? (SolidColorBrush)FindResource("CyanBrush") : (SolidColorBrush)FindResource("MagentaBrush"),
                    Effect = new DropShadowEffect
                    {
                        Color = random.NextDouble() > 0.5 ? (Color)FindResource("CyanColor") : (Color)FindResource("MagentaColor"),
                        BlurRadius = 16,
                        ShadowDepth = 0,
                        Opacity = 0.9
                    },
                    Opacity = 0
                };
                Canvas.SetLeft(dot, random.NextDouble() * 1920);
                Canvas.SetTop(dot, 1080 + 20);
                ParticleCanvas.Children.Add(dot);

                double duration = 14 + random.NextDouble() * 18;
                double delay = random.NextDouble() * -22;

                var up = new DoubleAnimation
                {
                    From = 1100,
                    To = -20,
                    Duration = TimeSpan.FromSeconds(duration),
                    RepeatBehavior = RepeatBehavior.Forever,
                    BeginTime = TimeSpan.FromSeconds(delay)
                };
                var fade = new DoubleAnimationUsingKeyFrames
                {
                    Duration = TimeSpan.FromSeconds(duration),
                    RepeatBehavior = RepeatBehavior.Forever,
                    BeginTime = TimeSpan.FromSeconds(delay)
                };
                fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
                fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.9, KeyTime.FromPercent(0.1)));
                fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.6, KeyTime.FromPercent(0.9)));
                fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));

                dot.BeginAnimation(Canvas.TopProperty, up);
                dot.BeginAnimation(OpacityProperty, fade);
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

            var spin = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(2)) { RepeatBehavior = RepeatBehavior.Forever };
            ScanRadarTransform.BeginAnimation(RotateTransform.AngleProperty, spin);

            var logLines = new System.Collections.Generic.List<string> {
                "INIT · ClinicaPC diagnostic v3.2.1",
                "PROBE bios · OK",
                $"DETECT chassis · {_viewModel.DisplayConfig.ChassisName} {_viewModel.DisplayConfig.ModelName}",
                $"READ cpu  · {_viewModel.DisplayConfig.Cpu}",
                $"READ gpu  · {_viewModel.DisplayConfig.Gpu}",
                $"READ ram  · {_viewModel.DisplayConfig.Ram}",
                $"READ ssd  · {_viewModel.DisplayConfig.Storage}",
                $"READ disp · {_viewModel.DisplayConfig.Screen}",
                $"READ batt · {_viewModel.DisplayConfig.Battery}",
                $"READ wifi · {_viewModel.DisplayConfig.Wifi}",
                $"READ cam  · {_viewModel.DisplayConfig.Camera}",
                "VERIFY    · grado A+",
                "COMPLETE  · 100%"
            };

            int total = logLines.Count;
            for(int i=0; i<total; i++)
            {
                await Task.Delay(170);
                _viewModel.ScanLogs.Add(new ScanLogItem {
                    Time = (i+1).ToString("D2"),
                    Step = logLines[i],
                    Color = logLines[i].StartsWith("COMPLETE") ? "OkBrush" : "Text1Brush"
                });
                int pct = ((i+1)*100)/total;
                ScanProgressText.Text = pct.ToString("D3");
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

            int from = _viewModel.CurrentScreen;
            Grid[] screens = { Screen0_Attract, Screen1_Scan, Screen2_Main, Screen3_Detail };
            string[] names = { "MODO ESPERA", "ANÁLISIS DE SISTEMA", "RESUMEN DEL EQUIPO", "ESPECIFICACIONES" };

            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(250));
            fadeOut.Completed += (s, e) =>
            {
                screens[from].Visibility = Visibility.Collapsed;
                screens[target].Visibility = Visibility.Visible;
                _viewModel.CurrentScreenName = names[target];
                screens[target].BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)));
                _viewModel.CurrentScreen = target;
                if (target > 0) { _inactivityTimer.Start(); _attractAutoScanTimer.Stop(); }
                else { _attractTimer.Start(); _attractAutoScanTimer.Stop(); _attractAutoScanTimer.Start(); }

                if (target == 2)
                {
                    _viewModel.ActiveSpec ??= _viewModel.Specs.FirstOrDefault();
                    if (!EditModeService.Instance.IsActive) _highlightTimer.Start();
                    else _highlightTimer.Stop();
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
                    dots[i].Effect = new DropShadowEffect { Color = cyanColor, BlurRadius = 10, ShadowDepth = 0, Opacity = 0.8 };
                }
                else
                {
                    dots[i].Width = 8;
                    dots[i].Fill = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
                    dots[i].Effect = null;
                }
            }
        }

        private void HighlightTimer_Tick(object sender, EventArgs e)
        {
            if (_viewModel.Specs == null || _viewModel.Specs.Count == 0) return;
            foreach (var spec in _viewModel.Specs) spec.IsHighlighted = false;
            var active = _viewModel.Specs[_highlightIndex];
            active.IsHighlighted = true;
            _viewModel.ActiveSpec = active; // el spotlight central sigue al componente resaltado (anima en XAML vía TargetUpdated)
            _highlightIndex = (_highlightIndex + 1) % _viewModel.Specs.Count;
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

        private void RegisterInStartup()
        {
            try
            {
                const string appName = "KioskHardwareDisplay";
                // MainModule.FileName es seguro bajo publicación single-file (Assembly.Location
                // devuelve "" ahí → registraba una ruta vacía y rompía el autostart).
                string? exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath))
                {
                    Log.Warning("No se pudo resolver la ruta del ejecutable; autostart no registrado.");
                    return;
                }
                using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
                if (key != null) key.SetValue(appName, $"\"{exePath}\"");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error al intentar registrar la aplicación en el inicio de Windows.");
            }
        }

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