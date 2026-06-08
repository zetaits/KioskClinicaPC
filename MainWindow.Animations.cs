using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using KioskClinicaPC.Core;
using KioskClinicaPC.Models;

namespace KioskClinicaPC
{
    /// <summary>
    /// Parte de <see cref="MainWindow"/> con las rutinas de animación puramente visuales: deriva del
    /// blob de fondo, episodios del orbe, pings del radar, anillo de progreso del escaneo, morph
    /// tarjeta→spotlight y la animación de entrada al HUD. Separadas del code-behind principal
    /// (navegación, timers, edición) por volumen y por ser independientes de la lógica de pantallas.
    /// </summary>
    public partial class MainWindow
    {
        #region Ambient / orb / radar

        /// <summary>
        /// Arranca el movimiento ambiental: la deriva del blob de fondo (26 s, ida y vuelta) y el
        /// temporizador de "episodios" del orbe (en reposo el orbe está quieto; cada cierto tiempo gira
        /// una vuelta encogiéndose y atenuándose). La deriva se omite en modo gráfico ligero (un blob
        /// grande moviéndose cuesta en iGPU/software).
        /// </summary>
        private void StartAmbientMotion()
        {
            _timers.Start(KioskTimer.OrbEpisode);

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
    }
}
