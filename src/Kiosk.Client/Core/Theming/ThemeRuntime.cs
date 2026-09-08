using KioskClinicaPC.Core.Config;
using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KioskClinicaPC.Core.Theming
{
    /// <summary>Estado visual actual y aplicación de tokens en el ámbito de MainWindow.</summary>
    public static class ThemeRuntime
    {
        public static ResolvedVisualTheme Current { get; private set; } = ThemePresetCatalog.Resolve(null);

        public static ResolvedVisualTheme Set(ResolvedVisualTheme? theme)
        {
            ResolvedVisualTheme fallback = ThemePresetCatalog.Resolve(null);
            Current = theme ?? fallback;
            Current.Palette ??= fallback.Palette;
            Current.Scenes ??= new ResolvedThemeScenes();
            Current.Scenes.Attract ??= new ResolvedThemeScene();
            Current.Scenes.Scan ??= new ResolvedThemeScene();
            Current.Scenes.Overview ??= new ResolvedThemeScene();
            Current.Scenes.Detail ??= new ResolvedThemeScene();
            Current.Intensity = Math.Clamp(Current.Intensity, 0, 100);
            return Current;
        }

        public static Color Primary => Parse(Current.Palette.Primary, "#F37A4A");
        public static Color Secondary => Parse(Current.Palette.Secondary, "#FFB069");
        public static Color Highlight => Parse(Current.Palette.Highlight, "#F0D26B");

        public static void ApplyResources(FrameworkElement scope, ResolvedVisualTheme? theme)
        {
            ResolvedVisualTheme resolved = Set(theme);
            ThemePalette p = resolved.Palette;
            SetColorAndBrush(scope, "Bg0", Parse(p.Background0, "#04020A"));
            SetColorAndBrush(scope, "Bg1", Parse(p.Background1, "#0A0716"));
            SetColorAndBrush(scope, "Bg2", Parse(p.Background2, "#140B24"));
            SetColorAndBrush(scope, "Bg3", Parse(p.Background3, "#1C1135"));
            SetColorAndBrush(scope, "Cyan", Parse(p.Primary, "#F37A4A"));
            SetColorAndBrush(scope, "Cyan2", Shade(Parse(p.Primary, "#F37A4A"), .72));
            SetColorAndBrush(scope, "Magenta", Parse(p.Secondary, "#FFB069"));
            SetColorAndBrush(scope, "Magenta2", Shade(Parse(p.Secondary, "#FFB069"), .78));
            SetColorAndBrush(scope, "Ok", Parse(p.Highlight, "#F0D26B"));
            SetColorAndBrush(scope, "Amber", Blend(Parse(p.Secondary, "#FFB069"), Parse(p.Highlight, "#F0D26B"), .5));
            SetColorAndBrush(scope, "Text0", Parse(p.Text0, "#FFFFFF"));
            SetColorAndBrush(scope, "Text1", Parse(p.Text1, "#D6D2E8"));
            SetColorAndBrush(scope, "Text2", Parse(p.Text2, "#A59EC2"));
            SetColorAndBrush(scope, "Text3", Parse(p.Text3, "#6F6890"));
            SetColorAndBrush(scope, "Line1", Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
            SetColorAndBrush(scope, "Line2", Color.FromArgb(0x29, 0xFF, 0xFF, 0xFF));

            Color primary = Primary, secondary = Secondary;
            scope.Resources["PrimaryTransparentColor"] = WithAlpha(primary, 0);
            scope.Resources["PrimaryHalfColor"] = WithAlpha(primary, 0x80);
            scope.Resources["PrimaryDeepColor"] = WithAlpha(Shade(primary, .48), 0xCC);
            scope.Resources["PrimaryDeepTransparentColor"] = WithAlpha(Shade(primary, .48), 0);
            scope.Resources["SecondaryTransparentColor"] = WithAlpha(secondary, 0);
            scope.Resources["SecondaryDeepColor"] = WithAlpha(Shade(secondary, .42), 0x66);
            scope.Resources["SecondaryDeepTransparentColor"] = WithAlpha(Shade(secondary, .42), 0);
        }

        public static ImageSource? LoadDecoration(string? key)
        {
            string? path = AssetResolver.ResolveThemeAsset(key);
            if (path == null) return null;
            try
            {
                var image = new BitmapImage();
                image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.UriSource = new Uri(path, UriKind.Absolute);
                image.EndInit(); image.Freeze(); return image;
            }
            catch { return null; }
        }

        private static void SetColorAndBrush(FrameworkElement scope, string prefix, Color color)
        {
            scope.Resources[prefix + "Color"] = color;
            var brush = new SolidColorBrush(color); brush.Freeze(); scope.Resources[prefix + "Brush"] = brush;
        }
        private static Color Parse(string? value, string fallback)
        {
            try { return (Color)ColorConverter.ConvertFromString(value ?? fallback); }
            catch { return (Color)ColorConverter.ConvertFromString(fallback); }
        }
        private static Color Shade(Color c, double factor) => Color.FromArgb(c.A,
            (byte)Math.Clamp(c.R * factor, 0, 255), (byte)Math.Clamp(c.G * factor, 0, 255), (byte)Math.Clamp(c.B * factor, 0, 255));
        private static Color Blend(Color a, Color b, double amount) => Color.FromRgb(
            (byte)(a.R + (b.R - a.R) * amount), (byte)(a.G + (b.G - a.G) * amount), (byte)(a.B + (b.B - a.B) * amount));
        private static Color WithAlpha(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);
    }
}
