using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
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
        private readonly KeyboardHook _hook;
        private readonly MainViewModel _viewModel;
        
        private DispatcherTimer _inactivityTimer;
        private DispatcherTimer _attractTimer;
        private DispatcherTimer _attractAutoScanTimer;
        private DispatcherTimer _highlightTimer;
        
        private int _highlightIndex = 0;
        private int _attractSlideIndex = 0;
        private const int InactivityTimeoutSeconds = 90;

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
                if (_viewModel.CurrentScreen == 0) StartScanSequence();
            };

            _highlightTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _highlightTimer.Tick += HighlightTimer_Tick;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
#if !DEBUG
            _hook.Start();
            RegisterInStartup();
#endif
            SpawnParticles(26);
            BuildQrPattern();
            await _viewModel.LoadHardwareAndConfigAsync();
            _attractTimer.Start();
            _attractAutoScanTimer.Start();
        }

        private void BuildQrPattern()
        {
            QrPattern.Children.Clear();
            for (int i = 0; i < 81; i++)
            {
                int x = i % 9;
                int y = i / 9;
                bool corner = (x < 3 && y < 3) || (x > 5 && y < 3) || (x < 3 && y > 5);
                bool on;
                if (corner)
                {
                    if ((x == 1 && y == 1) || (x == 7 && y == 1) || (x == 1 && y == 7)) on = false;
                    else on = true;
                }
                else
                {
                    int n = ((x * 31 + y * 17 + x * y) % 7);
                    on = n < 3;
                }
                QrPattern.Children.Add(new Rectangle
                {
                    Fill = on ? Brushes.Black : Brushes.Transparent,
                    Margin = new Thickness(1)
                });
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
            StartScanSequence();
        }

        private async void StartScanSequence()
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

        public void SpecNode_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is SpecItem item)
            {
                _viewModel.SelectedSpec = item;
                NavigateToScreen(3); // Detail
            }
        }

        public void SpecNode_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.DataContext is not SpecItem item) return;

            var st = new ScaleTransform(0.4, 0.4);
            fe.RenderTransformOrigin = new Point(0.5, 0.5);
            fe.RenderTransform = st;

            var sx = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(550), BeginTime = item.NodeAnimDelay };
            sx.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.4, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            sx.KeyFrames.Add(new SplineDoubleKeyFrame(1.06, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(330)), new KeySpline(0.2, 0.8, 0.2, 1.1)));
            sx.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(550))));

            var sy = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(550), BeginTime = item.NodeAnimDelay };
            sy.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.4, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            sy.KeyFrames.Add(new SplineDoubleKeyFrame(1.06, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(330)), new KeySpline(0.2, 0.8, 0.2, 1.1)));
            sy.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(550))));

            var op = new DoubleAnimation
            {
                From = 0, To = 1,
                Duration = TimeSpan.FromMilliseconds(400),
                BeginTime = item.NodeAnimDelay
            };

            st.BeginAnimation(ScaleTransform.ScaleXProperty, sx);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, sy);
            fe.BeginAnimation(OpacityProperty, op);
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
            _inactivityTimer.Stop();

            Grid[] screens = { Screen0_Attract, Screen1_Scan, Screen2_Main, Screen3_Detail };
            string[] names = { "MODO ESPERA", "ANÁLISIS DE SISTEMA", "RESUMEN DEL EQUIPO", "ESPECIFICACIONES" };
            
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(250));
            fadeOut.Completed += (s, e) =>
            {
                screens[_viewModel.CurrentScreen].Visibility = Visibility.Collapsed;
                screens[target].Visibility = Visibility.Visible;
                _viewModel.CurrentScreenName = names[target];
                screens[target].BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)));
                _viewModel.CurrentScreen = target;
                if (target > 0) { _inactivityTimer.Start(); _attractAutoScanTimer.Stop(); }
                else { _attractTimer.Start(); _attractAutoScanTimer.Stop(); _attractAutoScanTimer.Start(); }

                if (target == 2) _highlightTimer.Start();
                else _highlightTimer.Stop();
            };
            screens[_viewModel.CurrentScreen].BeginAnimation(OpacityProperty, fadeOut);
        }

        private void ResetInactivityTimer()
        {
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
            _attractSlideIndex = (_attractSlideIndex + 1) % 3;
            if (_attractSlideIndex == 0)
            {
                _viewModel.AttractEyebrow = "CLINICAPC · ANÁLISIS EN VIVO";
                _viewModel.AttractTitle1 = "ESTE EQUIPO";
                _viewModel.AttractTitle2 = "TE ESTÁ OBSERVANDO.";
                _viewModel.AttractSubtitle = "Conéctate · escanea · descubre cada componente en 30 segundos.";
            }
            else if (_attractSlideIndex == 1)
            {
                _viewModel.AttractEyebrow = "SIN TECNICISMOS";
                _viewModel.AttractTitle1 = "LO ENTIENDES";
                _viewModel.AttractTitle2 = "AUNQUE NO SEAS TÉCNICO.";
                _viewModel.AttractSubtitle = "Te traducimos cada spec a lenguaje de calle.";
            }
            else
            {
                _viewModel.AttractEyebrow = "REACONDICIONADOS CON CABEZA";
                _viewModel.AttractTitle1 = "HASTA 60% MENOS";
                _viewModel.AttractTitle2 = "QUE COMPRARLO NUEVO.";
                _viewModel.AttractSubtitle = "Probado, limpiado y con 24 meses de garantía.";
            }

            var dots = new[] { SlideDot0, SlideDot1, SlideDot2 };
            var cyanBrush = (SolidColorBrush)FindResource("CyanBrush");
            var cyanColor = (Color)FindResource("CyanColor");
            for (int i = 0; i < dots.Length; i++)
            {
                if (i == _attractSlideIndex)
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
            _viewModel.Specs[_highlightIndex].IsHighlighted = true;
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
                    MessageBox.Show(
                        KeyboardHook.AllowWindowsKey
                            ? "Tecla Windows DESBLOQUEADA. Win+Impr Pant guardará captura en Imágenes\\Capturas de pantalla."
                            : "Tecla Windows BLOQUEADA.",
                        "Capturas",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            if (e.Key == Key.Escape && _viewModel.CurrentScreen > 0) NavigateToScreen(0);
        }

        private void SettingsClickArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 3) OpenSettingsDialog();
        }

        private void RegisterInStartup()
        {
            try
            {
                const string appName = "KioskHardwareDisplay";
                using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
                if (key != null) key.SetValue(appName, $"\"{Assembly.GetExecutingAssembly().Location}\"");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error al intentar registrar la aplicación en el inicio de Windows.");
            }
        }

        private async void OpenSettingsDialog()
        {
            if (new PasswordDialog { Owner = this }.ShowDialog() == true)
            {
                if (new SettingsWindow(_viewModel.DetectedSpecs) { Owner = this }.ShowDialog() == true)
                {
                    await _viewModel.LoadHardwareAndConfigAsync();
                }
            }
        }

        #endregion
    }
}