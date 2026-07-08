using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

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
        public static void Spawn(Canvas target, int count)
        {
            if (target == null || count <= 0) return;

            var app = Application.Current;
            var cyanColor = (Color)app.FindResource("CyanColor");
            var magentaColor = (Color)app.FindResource("MagentaColor");

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
                var dot = new Ellipse
                {
                    Width = size,
                    Height = size,
                    Fill = random.NextDouble() > 0.5 ? cyanGlow : magentaGlow,
                    Opacity = 0
                };
                Canvas.SetLeft(dot, random.NextDouble() * 1920);
                Canvas.SetTop(dot, 1080 + 20);
                target.Children.Add(dot);

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
                dot.BeginAnimation(UIElement.OpacityProperty, fade);
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
