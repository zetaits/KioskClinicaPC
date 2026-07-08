using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace KioskClinicaPC.Controls
{
    /// <summary>
    /// Circular benchmark readout: a 270° track arc (gap at the bottom) with a value
    /// arc filled proportionally to <see cref="Value"/> (0–100) and a centered "%" readout.
    /// Matches the mockup's RadialGauge (components.jsx). Pure code-behind — no XAML.
    /// </summary>
    public class RadialGauge : UserControl
    {
        private const double StartAngle = 135;   // bottom-left
        private const double SweepAngle = 270;    // leaves a 90° gap at the bottom

        private readonly Path _track = new();
        private readonly Path _value = new();
        private readonly TextBlock _number = new();
        private readonly Run _numberRun = new();

        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
            nameof(Value), typeof(double), typeof(RadialGauge),
            new PropertyMetadata(0.0, OnVisualChanged));

        public static readonly DependencyProperty AccentColorProperty = DependencyProperty.Register(
            nameof(AccentColor), typeof(Color), typeof(RadialGauge),
            new PropertyMetadata(Color.FromRgb(0xF3, 0x7A, 0x4A), OnVisualChanged));

        public static readonly DependencyProperty GaugeSizeProperty = DependencyProperty.Register(
            nameof(GaugeSize), typeof(double), typeof(RadialGauge),
            new PropertyMetadata(196.0, OnVisualChanged));

        public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
        public Color AccentColor { get => (Color)GetValue(AccentColorProperty); set => SetValue(AccentColorProperty, value); }
        public double GaugeSize { get => (double)GetValue(GaugeSizeProperty); set => SetValue(GaugeSizeProperty, value); }

        public RadialGauge()
        {
            var grid = new Grid();

            _track.StrokeThickness = 12;
            _track.StrokeStartLineCap = PenLineCap.Round;
            _track.StrokeEndLineCap = PenLineCap.Round;
            var trackBrush = new SolidColorBrush(Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF));
            trackBrush.Freeze();
            _track.Stroke = trackBrush;

            _value.StrokeThickness = 12;
            _value.StrokeStartLineCap = PenLineCap.Round;
            _value.StrokeEndLineCap = PenLineCap.Round;

            _number.HorizontalAlignment = HorizontalAlignment.Center;
            _number.VerticalAlignment = VerticalAlignment.Center;
            _number.FontFamily = (FontFamily)Application.Current.FindResource("ChakraPetch");
            _number.FontWeight = FontWeights.Bold;
            _number.FontSize = 58;
            _number.Inlines.Add(_numberRun);
            var pct = new Run("%") { FontSize = 22, Foreground = (Brush)Application.Current.FindResource("Text2Brush") };
            _number.Inlines.Add(pct);

            grid.Children.Add(_track);
            grid.Children.Add(_value);
            grid.Children.Add(_number);
            Content = grid;

            Rebuild();
        }

        private static void OnVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((RadialGauge)d).Rebuild();

        private void Rebuild()
        {
            double size = GaugeSize;
            Width = size;
            Height = size;

            double stroke = 12;
            double r = (size - stroke) / 2 - 4;
            double cx = size / 2;
            double v = Math.Max(0, Math.Min(100, Value));

            _track.Data = BuildArc(cx, r, StartAngle, SweepAngle);
            _value.Data = BuildArc(cx, r, StartAngle, SweepAngle * (v / 100.0));

            var accent = new SolidColorBrush(AccentColor);
            accent.Freeze();
            _value.Stroke = accent;
            var glow = new DropShadowEffect { Color = AccentColor, BlurRadius = 14, ShadowDepth = 0, Opacity = 0.85 };
            glow.Freeze();
            _value.Effect = glow;
            _number.Foreground = accent;
            _numberRun.Text = Math.Round(v).ToString("0");
        }

        private static Geometry BuildArc(double cx, double r, double startDeg, double sweepDeg)
        {
            if (sweepDeg <= 0.01)
                sweepDeg = 0.01; // avoid a zero-length arc that WPF would draw as a full circle

            Point start = PointOnArc(cx, r, startDeg);
            Point end = PointOnArc(cx, r, startDeg + sweepDeg);
            var fig = new PathFigure { StartPoint = start, IsClosed = false };
            fig.Segments.Add(new ArcSegment
            {
                Point = end,
                Size = new Size(r, r),
                IsLargeArc = sweepDeg > 180,
                SweepDirection = SweepDirection.Clockwise
            });
            var geo = new PathGeometry();
            geo.Figures.Add(fig);
            geo.Freeze();
            return geo;
        }

        private static Point PointOnArc(double cx, double r, double deg)
        {
            double rad = deg * Math.PI / 180.0;
            return new Point(cx + r * Math.Cos(rad), cx + r * Math.Sin(rad));
        }
    }
}
