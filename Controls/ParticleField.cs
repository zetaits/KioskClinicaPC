using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace KioskClinicaPC.Controls
{
    /// <summary>
    /// Partículas decorativas que ascienden por el fondo de la pantalla de atracción.
    /// Extraído de MainWindow para sacar el motor de animación de la vista. Visual puro.
    /// </summary>
    public static class ParticleField
    {
        public static void Spawn(Canvas target, int count)
        {
            if (target == null) return;

            var app = Application.Current;
            var cyanBrush = (SolidColorBrush)app.FindResource("CyanBrush");
            var magentaBrush = (SolidColorBrush)app.FindResource("MagentaBrush");
            var cyanColor = (Color)app.FindResource("CyanColor");
            var magentaColor = (Color)app.FindResource("MagentaColor");

            var random = new Random();
            for (int i = 0; i < count; i++)
            {
                double size = 1 + random.NextDouble() * 2.5;
                var dot = new Ellipse
                {
                    Width = size,
                    Height = size,
                    Fill = random.NextDouble() > 0.5 ? cyanBrush : magentaBrush,
                    Effect = new DropShadowEffect
                    {
                        Color = random.NextDouble() > 0.5 ? cyanColor : magentaColor,
                        BlurRadius = 16,
                        ShadowDepth = 0,
                        Opacity = 0.9
                    },
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
    }
}
