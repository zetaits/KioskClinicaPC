using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using KioskClinicaPC.Core.Config;

namespace KioskClinicaPC.Controls
{
    /// <summary>
    /// Partículas decorativas que ascienden por el fondo de la pantalla de atracción.
    /// Extraído de MainWindow para sacar el motor de animación de la vista. Visual puro.
    ///
    /// Rendimiento: el glow se hornea en un <see cref="RadialGradientBrush"/> congelado (núcleo opaco →
    /// halo transparente) en vez de un <c>DropShadowEffect</c> por punto. Antes eran N blurs (pixel shader)
    /// re-compuestos cada frame mientras suben — el mayor coste por frame de la app y la causa del lag en
    /// iGPU. La versión horneada se ve prácticamente igual y cuesta ~0.
    /// </summary>
    public static class ParticleField
    {
        public static void Spawn(Canvas target, int count, Color? primary = null, Color? secondary = null,
            ThemeAmbientEffect effect = ThemeAmbientEffect.Default, int intensity = 70)
        {
            if (target == null) return;
            target.Children.Clear();
            if (count <= 0 || intensity <= 0) return;

            var app = Application.Current;
            var cyanColor = primary ?? (Color)app.FindResource("CyanColor");
            var magentaColor = secondary ?? (Color)app.FindResource("MagentaColor");
            count = Math.Max(1, count * Math.Clamp(intensity, 0, 100) / 100);

            if (effect == ThemeAmbientEffect.Spotlights)
            {
                AddSpotlights(target, cyanColor, magentaColor, intensity);
                return;
            }
            if (effect == ThemeAmbientEffect.Notebook)
            {
                AddNotebookMarks(target, cyanColor, magentaColor, intensity);
                return;
            }

            // Dos pinceles de glow congelados (compartidos por todas las partículas del mismo color).
            var cyanGlow = BuildGlowBrush(cyanColor);
            var magentaGlow = BuildGlowBrush(magentaColor);

            var random = new Random();
            for (int i = 0; i < count; i++)
            {
                // El "punto" nítido es pequeño (1–3.5 px); el óvalo total es ~6× para dejar sitio al halo,
                // así la fracción del núcleo sólido es constante y el pincel de glow se puede compartir.
                // (Antes size=core+30 hacía todas ~31px → "bolas de fuego". Ahora escala con el punto.)
                double core = 1 + random.NextDouble() * 2.5;
                double size = core * 6;
                Shape dot = effect == ThemeAmbientEffect.Confetti
                    ? new Rectangle { Width = core * 2.2, Height = core * 5.5, RadiusX = 1, RadiusY = 1 }
                    : new Ellipse { Width = size, Height = size };
                if (effect == ThemeAmbientEffect.Snow)
                {
                    dot.Width = dot.Height = 2 + random.NextDouble() * 6;
                    dot.Fill = new SolidColorBrush(Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF));
                }
                else dot.Fill = random.NextDouble() > 0.5 ? cyanGlow : magentaGlow;
                dot.Opacity = 0;
                Canvas.SetLeft(dot, random.NextDouble() * 1920);
                bool descending = effect is ThemeAmbientEffect.Snow or ThemeAmbientEffect.Confetti;
                Canvas.SetTop(dot, descending ? -20 : 1100);
                target.Children.Add(dot);

                double duration = 14 + random.NextDouble() * 18;
                double delay = random.NextDouble() * -22;

                var up = new DoubleAnimation
                {
                    From = descending ? -20 : 1100,
                    To = descending ? 1100 : -20,
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
                dot.BeginAnimation(UIElement.OpacityProperty, fade);
            }
        }

        private static void AddSpotlights(Canvas target, Color primary, Color secondary, int intensity)
        {
            foreach (var item in new[] { (primary, 180d, -180d, -18d), (secondary, 1450d, -120d, 20d) })
            {
                var beam = new Rectangle
                {
                    Width = 210, Height = 1450, Opacity = .08 * intensity / 100d,
                    Fill = new LinearGradientBrush(
                        Color.FromArgb(0, item.Item1.R, item.Item1.G, item.Item1.B),
                        Color.FromArgb(0xCC, item.Item1.R, item.Item1.G, item.Item1.B), 90),
                    RenderTransform = new RotateTransform(item.Item4)
                };
                Canvas.SetLeft(beam, item.Item2); Canvas.SetTop(beam, item.Item3); target.Children.Add(beam);
            }
        }

        private static void AddNotebookMarks(Canvas target, Color primary, Color secondary, int intensity)
        {
            for (int i = 0; i < 8; i++)
            {
                var line = new Line
                {
                    X1 = 0, X2 = 190 + i * 18, Y1 = 0, Y2 = i % 2 == 0 ? 0 : 35,
                    Stroke = new SolidColorBrush(i % 2 == 0 ? primary : secondary), StrokeThickness = 2,
                    Opacity = .12 * intensity / 100d
                };
                Canvas.SetLeft(line, i % 2 == 0 ? 70 : 1600); Canvas.SetTop(line, 160 + i * 105); target.Children.Add(line);
            }
        }

        /// <summary>Pincel radial congelado: núcleo brillante del color → halo transparente. Imita el
        /// antiguo glow de DropShadowEffect sin pixel shader. Congelado para compartirlo y cachearlo.</summary>
        private static RadialGradientBrush BuildGlowBrush(Color color)
        {
            var brush = new RadialGradientBrush
            {
                Center = new Point(0.5, 0.5),
                GradientOrigin = new Point(0.5, 0.5),
                RadiusX = 0.5,
                RadiusY = 0.5
            };
            // size = core*6 ⇒ el punto sólido (radio ~core/2) ocupa fracción ~1/6 ≈ 0.17 del radio.
            // Núcleo sólido pequeño + caída rápida a halo tenue (imita blur, no una bola rellena).
            brush.GradientStops.Add(new GradientStop(color, 0.0));
            brush.GradientStops.Add(new GradientStop(color, 0.17));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x55, color.R, color.G, color.B), 0.40));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, color.R, color.G, color.B), 1.0));
            brush.Freeze();
            return brush;
        }
    }
}
