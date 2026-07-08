using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using KioskClinicaPC.Core;
using Serilog;

namespace KioskClinicaPC.Controls
{
    /// <summary>Editor inline: superpone un TextBox sobre el TextBlock objetivo, reusando su binding.</summary>
    public class InlineEditAdorner : Adorner
    {
        private readonly VisualCollection _visuals;
        private readonly TextBox _box;
        private readonly bool _multiline;
        private bool _finished;

        public event Action? Finished;

        public InlineEditAdorner(TextBlock target, bool multiline) : base(target)
        {
            _multiline = multiline;
            _box = new TextBox
            {
                FontFamily = target.FontFamily,
                FontSize = target.FontSize,
                FontWeight = target.FontWeight,
                FontStyle = target.FontStyle,
                FontStretch = target.FontStretch,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromArgb(0xF7, 0x0A, 0x07, 0x16)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xF3, 0x7A, 0x4A)),
                BorderThickness = new Thickness(1),
                CaretBrush = new SolidColorBrush(Color.FromRgb(0xF3, 0x7A, 0x4A)),
                Padding = new Thickness(2, 0, 2, 0),
                AcceptsReturn = multiline,
                TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
                TextAlignment = target.TextAlignment,
                VerticalContentAlignment = VerticalAlignment.Center,
                DataContext = target.DataContext
            };

            var orig = BindingOperations.GetBinding(target, TextBlock.TextProperty);
            var nb = new Binding
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.Explicit
            };
            if (orig != null)
            {
                nb.Path = orig.Path;
                nb.Converter = orig.Converter;
                nb.ConverterParameter = orig.ConverterParameter;
                nb.ConverterCulture = orig.ConverterCulture;
                nb.StringFormat = orig.StringFormat;
                if (orig.Source != null) nb.Source = orig.Source;
                else if (orig.RelativeSource != null) nb.RelativeSource = orig.RelativeSource;
                else if (!string.IsNullOrEmpty(orig.ElementName)) nb.ElementName = orig.ElementName;
            }
            _box.SetBinding(TextBox.TextProperty, nb);

            _box.KeyDown += OnKeyDown;
            _box.LostKeyboardFocus += (s, e) => Commit();

            _visuals = new VisualCollection(this) { _box };
        }

        public void FocusEditor()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _box.Focus();
                Keyboard.Focus(_box);
                _box.SelectAll();
            }), DispatcherPriority.Input);
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Cancel();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && (!_multiline || (Keyboard.Modifiers & ModifierKeys.Control) != 0))
            {
                Commit();
                e.Handled = true;
            }
        }

        public void Commit()
        {
            if (_finished) return;
            _finished = true;
            try
            {
                var expr = _box.GetBindingExpression(TextBox.TextProperty);
                expr?.UpdateSource();
                // Un ConvertBack fallido no lanza excepción: deja HasError. No marques como
                // modificado en ese caso, o el usuario creería que guardó algo que se descartó.
                if (expr == null || !expr.HasError)
                    EditModeService.Instance.IsDirty = true;
                else
                    Log.Warning("Edición en línea: el binding quedó con error, cambio no aplicado.");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Edición en línea: fallo al escribir el origen del binding.");
            }
            Finished?.Invoke();
        }

        public void Cancel()
        {
            if (_finished) return;
            _finished = true;
            Finished?.Invoke();
        }

        protected override int VisualChildrenCount => _visuals.Count;
        protected override Visual GetVisualChild(int index) => _visuals[index];

        protected override Size MeasureOverride(Size constraint)
        {
            var sz = AdornedElement.RenderSize;
            double w = Math.Max(sz.Width + (_multiline ? 0 : 40), 120);
            double h = Math.Max(sz.Height, 26);
            _box.Measure(new Size(w, double.PositiveInfinity));
            h = Math.Max(h, _box.DesiredSize.Height);
            return new Size(w, h);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            _box.Arrange(new Rect(new Point(0, 0), finalSize));
            return finalSize;
        }
    }

    /// <summary>Resalte punteado que marca un texto como editable mientras el modo está activo.</summary>
    public class EditHighlightAdorner : Adorner
    {
        private static readonly Pen DashPen;

        static EditHighlightAdorner()
        {
            var brush = new SolidColorBrush(Color.FromArgb(0x99, 0xF3, 0x7A, 0x4A));
            brush.Freeze();
            DashPen = new Pen(brush, 1) { DashStyle = new DashStyle(new double[] { 3, 2 }, 0) };
            DashPen.Freeze();
        }

        public EditHighlightAdorner(UIElement adornedElement) : base(adornedElement)
        {
            IsHitTestVisible = false;
        }

        protected override void OnRender(DrawingContext dc)
        {
            var sz = AdornedElement.RenderSize;
            if (sz.Width <= 0 || sz.Height <= 0) return;

            // Ceñir la caja a la extensión real del texto, no al RenderSize arreglado
            // (un TextBlock con HorizontalAlignment=Stretch ocupa todo el ancho de su
            // contenedor aunque el texto sea corto, lo que producía cajas sobre el vacío).
            if (AdornedElement is TextBlock tb && !string.IsNullOrWhiteSpace(tb.Text))
            {
                double dpi = VisualTreeHelper.GetDpi(tb).PixelsPerDip;
                var ft = new FormattedText(
                    tb.Text,
                    System.Globalization.CultureInfo.CurrentUICulture,
                    tb.FlowDirection,
                    new Typeface(tb.FontFamily, tb.FontStyle, tb.FontWeight, tb.FontStretch),
                    tb.FontSize,
                    Brushes.White,
                    dpi)
                {
                    MaxTextWidth = sz.Width
                };

                double textW = ft.Width;
                double textH = ft.Height;
                if (textW <= 0 || textH <= 0) return;

                double offsetX = tb.TextAlignment switch
                {
                    TextAlignment.Center => (sz.Width - textW) / 2,
                    TextAlignment.Right => sz.Width - textW,
                    _ => 0
                };
                if (offsetX < 0) offsetX = 0;

                double w = Math.Min(textW + 4, sz.Width + 4);
                double h = Math.Min(textH + 2, sz.Height + 2);
                dc.DrawRectangle(null, DashPen, new Rect(new Point(offsetX - 2, -1), new Size(w, h)));
                return;
            }

            var rect = new Rect(new Point(-2, -1), new Size(sz.Width + 4, sz.Height + 2));
            dc.DrawRectangle(null, DashPen, rect);
        }
    }

    /// <summary>Orquesta el inicio de edición y los resaltes de afordancia.</summary>
    public static class InlineEditController
    {
        private static InlineEditAdorner? _active;
        private static readonly List<(AdornerLayer Layer, EditHighlightAdorner Adorner)> _highlights = new();

        public static bool TryBeginEdit(object? originalSource)
        {
            var tb = FindTextBlock(originalSource as DependencyObject);
            if (tb == null || !IsEditable(tb)) return false;

            var layer = AdornerLayer.GetAdornerLayer(tb);
            if (layer == null) return false;

            CommitActive();

            var adorner = new InlineEditAdorner(tb, Editable.GetMultiline(tb));
            adorner.Finished += () =>
            {
                layer.Remove(adorner);
                if (ReferenceEquals(_active, adorner)) _active = null;
            };
            layer.Add(adorner);
            _active = adorner;
            adorner.FocusEditor();
            return true;
        }

        public static void CommitActive() => _active?.Commit();

        public static void CancelActive() => _active?.Cancel();

        public static bool IsEditable(TextBlock tb)
        {
            return Editable.GetIsOn(tb)
                   && BindingOperations.GetBindingExpression(tb, TextBlock.TextProperty) != null;
        }

        private static TextBlock? FindTextBlock(DependencyObject? source)
        {
            while (source != null)
            {
                if (source is TextBlock tb) return tb;
                if (source is Run run) return run.Parent as TextBlock;
                source = VisualTreeHelper.GetParent(source) ?? LogicalTreeHelper.GetParent(source);
            }
            return null;
        }

        public static void SetHighlights(DependencyObject? root, bool on)
        {
            foreach (var (layer, adorner) in _highlights) layer.Remove(adorner);
            _highlights.Clear();
            if (!on || root == null) return;

            foreach (var tb in EnumerateTextBlocks(root))
            {
                if (!IsEditable(tb)) continue;
                if (!tb.IsVisible || string.IsNullOrWhiteSpace(tb.Text)) continue;
                var layer = AdornerLayer.GetAdornerLayer(tb);
                if (layer == null) continue;
                var h = new EditHighlightAdorner(tb);
                layer.Add(h);
                _highlights.Add((layer, h));
            }
        }

        private static IEnumerable<TextBlock> EnumerateTextBlocks(DependencyObject root)
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is TextBlock tb) yield return tb;
                foreach (var nested in EnumerateTextBlocks(child)) yield return nested;
            }
        }
    }
}
