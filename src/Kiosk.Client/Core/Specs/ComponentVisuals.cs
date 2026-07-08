using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace KioskClinicaPC.Core.Specs
{
    /// <summary>Identidad visual de un componente: icono vectorial y acento (brush + color).
    /// Datos puramente de presentación.</summary>
    public sealed class ComponentVisual
    {
        public string IconData { get; init; } = "";
        public SolidColorBrush AccentBrush { get; init; } = Brushes.Transparent;
        public Color AccentColor { get; init; }
    }

    /// <summary>
    /// Tablas de presentación por componente (iconos SVG, acento, ángulo). Vivían dentro de
    /// <c>MainViewModel.PopulateSpecs</c>, mezclando geometría y <see cref="Brush"/> con la lógica de
    /// datos del ViewModel. Aquí quedan aisladas: añadir/editar el aspecto de un componente es tocar
    /// solo este archivo. Los acentos se resuelven del tema (App.xaml) con fallback al hex literal.
    /// </summary>
    public static class ComponentVisuals
    {
        private const string IconCpu = "M 9,9 L 23,9 L 23,23 L 9,23 Z M 13,13 L 19,13 L 19,19 L 13,19 Z M 12,9 L 12,6 M 15,9 L 15,6 M 18,9 L 18,6 M 21,9 L 21,6 M 12,23 L 12,26 M 15,23 L 15,26 M 18,23 L 18,26 M 21,23 L 21,26 M 9,12 L 6,12 M 9,15 L 6,15 M 9,18 L 6,18 M 9,21 L 6,21 M 23,12 L 26,12 M 23,15 L 26,15 M 23,18 L 26,18 M 23,21 L 26,21";
        private const string IconGpu = "M 4,11 L 28,11 L 28,22 L 4,22 Z M 11,14 A 2.6,2.6 0 1,0 11,19 A 2.6,2.6 0 1,0 11,14 M 21,14 A 2.6,2.6 0 1,0 21,19 A 2.6,2.6 0 1,0 21,14 M 4,22 L 2,25 M 28,22 L 30,25";
        // DIMM: cuerpo PCB + 2 ventanas de chip (huecos even-odd) + tira de contactos con muesca de llave.
        // Todo formas CERRADAS porque el icono se rellena con Fill (las líneas abiertas no se verían).
        private const string IconRam = "M 3,8 L 29,8 L 29,20 L 3,20 Z M 6,11 L 13,11 L 13,17 L 6,17 Z M 16,11 L 23,11 L 23,17 L 16,17 Z M 5,20 L 27,20 L 27,23 L 5,23 Z M 15,20 L 17,20 L 17,23 L 15,23 Z";
        // SSD: carcasa redondeada (marco) + 3 chips NAND + LED de actividad. No base de datos.
        private const string IconStorage = "M 6,7 A 2,2 0 0,0 4,9 L 4,23 A 2,2 0 0,0 6,25 L 26,25 A 2,2 0 0,0 28,23 L 28,9 A 2,2 0 0,0 26,7 Z M 7,11 L 25,11 L 25,21 L 7,21 Z M 9,13 L 12,13 L 12,19 L 9,19 Z M 14.5,13 L 17.5,13 L 17.5,19 L 14.5,19 Z M 20,13 L 23,13 L 23,16 L 20,16 Z M 21,18 A 1.1,1.1 0 1,0 21.01,18 Z";
        // Monitor: marco con pantalla (hueco) + cuello + peana. No bloque sólido.
        private const string IconScreen = "M 5,5 A 2,2 0 0,0 3,7 L 3,19 A 2,2 0 0,0 5,21 L 27,21 A 2,2 0 0,0 29,19 L 29,7 A 2,2 0 0,0 27,5 Z M 6,8 L 26,8 L 26,18 L 6,18 Z M 14.5,21 L 17.5,21 L 17.5,25 L 14.5,25 Z M 10,25 L 22,25 L 22,27 L 10,27 Z";
        private const string IconBattery = "M 3,10 L 27,10 L 27,22 L 3,22 Z M 27,13 L 29,13 L 29,19 L 27,19 Z M 6,13 L 20,13 L 20,19 L 6,19 Z";
        // Iconos se renderizan con Fill (no Stroke): el wifi debe ser formas CERRADAS.
        // Antes eran arcos abiertos → al rellenarse formaban medias lunas ("AI slop").
        // Ahora: dos bandas anulares (sector exterior+interior cerrado) + punto sólido.
        private const string IconWifi = "M 0.24,13.68 A 20,20 0 0,1 31.76,13.68 L 28.61,16.14 A 16,16 0 0,0 3.39,16.14 Z M 5.76,17.99 A 13,13 0 0,1 26.24,17.99 L 23.09,20.46 A 9,9 0 0,0 8.91,20.46 Z M 13.8,26 A 2.2,2.2 0 1,1 18.2,26 A 2.2,2.2 0 1,1 13.8,26 Z";
        private const string IconCamera = "M 3,8 L 29,8 L 29,24 L 3,24 Z M 16,11.5 A 4.5,4.5 0 1,0 16,20.5 A 4.5,4.5 0 1,0 16,11.5 M 20,5 L 26,5 L 26,8 L 20,8 Z";
        private const string IconPorts = "M 3,13 L 12,13 L 12,19 L 3,19 Z M 14,11 L 20,11 L 20,21 L 14,21 Z M 22,14 L 29,14 L 29,18 L 22,18 Z";
        private const string IconOs = "M 4,5 L 15,5 L 15,15 L 4,15 Z M 17,5 L 28,5 L 28,15 L 17,15 Z M 4,17 L 15,17 L 15,27 L 4,27 Z M 17,17 L 28,17 L 28,27 L 17,27 Z";

        // Acento por componente: (claveColor del tema, claveBrush del tema, hex de respaldo).
        private static readonly (string ColorKey, string BrushKey, string Hex) Cyan = ("CyanColor", "CyanBrush", "#F37A4A");
        private static readonly (string ColorKey, string BrushKey, string Hex) Magenta = ("MagentaColor", "MagentaBrush", "#FFB069");
        private static readonly (string ColorKey, string BrushKey, string Hex) Lime = ("OkColor", "OkBrush", "#F0D26B");
        private static readonly (string ColorKey, string BrushKey, string Hex) Amber = ("AmberColor", "AmberBrush", "#FFA75C");

        private static readonly Dictionary<string, string> Icons = new()
        {
            [ComponentIds.Cpu] = IconCpu, [ComponentIds.Gpu] = IconGpu, [ComponentIds.Ram] = IconRam,
            [ComponentIds.Storage] = IconStorage, [ComponentIds.Screen] = IconScreen, [ComponentIds.Battery] = IconBattery,
            [ComponentIds.Wifi] = IconWifi, [ComponentIds.Camera] = IconCamera, [ComponentIds.Ports] = IconPorts,
            [ComponentIds.Os] = IconOs
        };

        private static readonly Dictionary<string, (string ColorKey, string BrushKey, string Hex)> Accents = new()
        {
            [ComponentIds.Cpu] = Cyan, [ComponentIds.Gpu] = Magenta, [ComponentIds.Ram] = Cyan,
            [ComponentIds.Storage] = Lime, [ComponentIds.Screen] = Cyan, [ComponentIds.Battery] = Lime,
            [ComponentIds.Wifi] = Magenta, [ComponentIds.Camera] = Cyan, [ComponentIds.Ports] = Amber,
            [ComponentIds.Os] = Cyan
        };

        /// <summary>Identidad visual de un componente por id. Acento desconocido → cian (igual que antes).</summary>
        public static ComponentVisual For(string id)
        {
            string key = id.ToLowerInvariant();
            var accent = Accents.TryGetValue(key, out var a) ? a : Cyan;
            Color color = ResolveColor(accent.ColorKey, accent.Hex);
            return new ComponentVisual
            {
                IconData = Icons.TryGetValue(key, out var icon) ? icon : "",
                AccentColor = color,
                AccentBrush = ResolveBrush(accent.BrushKey, color)
            };
        }

        // TryFindResource (no FindResource): si faltara una clave de tema, FindResource lanza. Con
        // fallback al hex degrada en vez de romper. El color resuelto sirve de respaldo para su brush.
        private static Color ResolveColor(string key, string hex)
            => Application.Current?.TryFindResource(key) is Color c
                ? c
                : (Color)ColorConverter.ConvertFromString(hex);

        private static SolidColorBrush ResolveBrush(string key, Color fallback)
            => Application.Current?.TryFindResource(key) as SolidColorBrush
               ?? new SolidColorBrush(fallback);
    }
}
