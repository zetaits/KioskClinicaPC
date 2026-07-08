using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace KioskClinicaPC.Core.Specs
{
    /// <summary>
    /// Genera el "Qué significa" (Summary) y el "Para qué te sirve" (Pros) ADAPTADOS al hardware real,
    /// en vez de un texto genérico igual en todo PC (p.ej. el viejo "La última generación de WiFi"
    /// aparecía hasta en equipos con WiFi 5). Lógica pura y testeable (sin WPF/WMI), misma familia que
    /// <see cref="SpecFormatter"/> y <see cref="PerformanceScorer"/>.
    ///
    /// Cada componente devuelve <see cref="Narration"/>: Summary y/o Pros pueden ser null = "no opino,
    /// deja el texto del catálogo". El caller solo aplica lo narrado si la tienda no editó el texto.
    /// </summary>
    public static class SpecNarrator
    {
        public readonly struct Narration
        {
            public Narration(string? summary, List<string>? pros) { Summary = summary; Pros = pros; }
            /// <summary>Párrafo "Qué significa" adaptado, o null para conservar el del catálogo.</summary>
            public string? Summary { get; }
            /// <summary>Bullets "Para qué te sirve" adaptados, o null para conservar los del catálogo.</summary>
            public List<string>? Pros { get; }
            public static readonly Narration None = new Narration(null, null);
        }

        public static Narration Narrate(string? id, AppConfig hw, int score)
        {
            if (hw == null) return Narration.None;
            return (id?.ToLowerInvariant()) switch
            {
                ComponentIds.Cpu => NarrateCpu(score),
                ComponentIds.Gpu => NarrateGpu(hw.Gpu, score),
                ComponentIds.Ram => NarrateRam(hw.Ram),
                ComponentIds.Storage => NarrateStorage(hw.Storage, hw.StorageDetail),
                ComponentIds.Screen => NarrateScreen(hw.Screen, hw.ScreenDetail),
                ComponentIds.Battery => NarrateBattery(hw.Battery),
                ComponentIds.Wifi => NarrateWifi(hw.Wifi),
                ComponentIds.Camera => NarrateCamera(hw.Camera),
                _ => Narration.None // puertos / SO: el genérico del catálogo ya es honesto
            };
        }

        private static List<string> Pros(params string[] p) => new List<string>(p);

        // 4 bandas de gama, mismo corte que PerformanceScorer.Band.
        private static int Band(int score) => score >= 95 ? 0 : score >= 88 ? 1 : score >= 80 ? 2 : 3;

        private static Narration NarrateCpu(int score)
        {
            int b = Band(score);
            if (b <= 1)
                return new Narration(
                    "El cerebro del equipo. Muchos núcleos rinden de sobra en multitarea pesada, edición y trabajo exigente.",
                    Pros("Edición de vídeo sin tirones", "Multitarea pesada", "Compilar / renderizar rápido"));
            if (b == 2)
                return new Narration(
                    "El cerebro del equipo. Solvente para ofimática, web y multitarea del día a día sin atascarse.",
                    Pros("Multitarea fluida", "Ofimática y navegación", "Trabajo del día a día"));
            return new Narration(
                "El cerebro del equipo. Suficiente para navegar, ofimática y tareas cotidianas con varias apps a la vez.",
                Pros("Navegación y ofimática", "Varias apps a la vez", "Uso cotidiano"));
        }

        private static Narration NarrateGpu(string? gpu, int score)
        {
            string n = (gpu ?? "").ToUpperInvariant();
            bool integrated =
                n.Contains("UHD") || n.Contains("IRIS") || n.Contains("VEGA") ||
                n.Contains("RADEON GRAPHICS") || (n.Contains("INTEL") && !n.Contains("ARC"));

            if (integrated)
                return new Narration(
                    "Gráfica integrada: ideal para ofimática, web, vídeo y juegos ligeros. No está pensada para juegos AAA exigentes.",
                    Pros("Ofimática y navegación", "Vídeo y streaming", "Juegos ligeros / e-sports"));

            if (Band(score) <= 1)
                return new Narration(
                    "Gráfica dedicada potente: pensada para jugar a calidad alta y acelerar IA, edición y modelado 3D.",
                    Pros("Juegos AAA en alto", "Streaming sin lag", "Aceleración IA local"));

            return new Narration(
                "Gráfica dedicada: juega en buena calidad y acelera edición de foto/vídeo y trabajo 3D.",
                Pros("Juegos en buena calidad", "Edición foto/vídeo", "Modelado 3D"));
        }

        private static Narration NarrateRam(string? ram)
        {
            double gb = FirstNumber(ram);
            string l = (ram ?? "").ToLowerInvariant();
            string ddr = l.Contains("ddr5") ? " Memoria DDR5." : l.Contains("ddr4") ? " Memoria DDR4." : "";

            if (gb >= 32)
                return new Narration(
                    $"El espacio de trabajo del PC. {gb:0} GB dan margen de sobra para multitarea exigente y proyectos pesados.{ddr}",
                    Pros("Decenas de pestañas abiertas", "Edición de foto/vídeo", "Máquinas virtuales"));
            if (gb >= 16)
                return new Narration(
                    $"El espacio de trabajo del PC. {gb:0} GB cómodos para el día a día y multitarea habitual sin tirones.{ddr}",
                    Pros("Multitarea fluida", "Photoshop + navegador", "Muchas pestañas a la vez"));
            if (gb > 0)
                return new Narration(
                    $"El espacio de trabajo del PC. {gb:0} GB suficientes para navegar, ofimática y tareas básicas a la vez.{ddr}",
                    Pros("Navegación y ofimática", "Apps del día a día", "Multitarea ligera"));
            return Narration.None;
        }

        private static Narration NarrateStorage(string? storage, string? detail)
        {
            string l = ((detail ?? "") + " " + (storage ?? "")).ToUpperInvariant();
            if (l.Contains("NVME"))
                return new Narration(
                    "Donde se guarda todo. SSD NVMe: hasta 30× más rápido que un disco duro, el PC arranca en segundos y los juegos cargan al vuelo.",
                    Pros("Arranque en <10 s", "Carga de juegos instantánea", "Transferencia de archivos rápida"));
            if (l.Contains("SSD"))
                return new Narration(
                    "Donde se guarda todo. SSD: mucho más rápido que un disco duro mecánico, con arranque y cargas ágiles.",
                    Pros("Arranque rápido", "Carga de juegos ágil", "Transferencia rápida"));
            if (l.Contains("HDD"))
                return new Narration(
                    "Donde se guarda todo. Disco duro de gran capacidad, ideal para archivar fotos, vídeos y documentos.",
                    Pros("Mucha capacidad", "Archivo de fotos y vídeo", "Coste por GB bajo"));
            return Narration.None;
        }

        private static Narration NarrateScreen(string? screen, string? detail)
        {
            string src = (detail ?? "") + " " + (screen ?? "");
            var m = Regex.Match(src, @"(?<w>\d{3,5})\D+(?<h>\d{3,5})");
            int w = m.Success && int.TryParse(m.Groups["w"].Value, out int ww) ? ww : 0;

            var hzMatch = Regex.Match(detail ?? "", @"(?<hz>\d{2,3})\s*Hz", RegexOptions.IgnoreCase);
            int hz = hzMatch.Success ? int.Parse(hzMatch.Groups["hz"].Value) : 0;
            string hzText = hz >= 120 ? $" Con {hz} Hz, el movimiento se ve extra fluido." : "";
            string firstPro = hz >= 120 ? $"Fluidez a {hz} Hz" : "Juegos competitivos";

            string body =
                w >= 3840 ? "Resolución 4K: imagen muy nítida para juegos, vídeo y trabajo en detalle."
                : w >= 2560 ? "Alta resolución QHD: imagen nítida y espacio de trabajo amplio."
                : w >= 1920 ? "Full HD: imagen clara y nítida para el día a día, juegos y vídeo."
                : w > 0 ? "Pantalla clara para navegar, ofimática y contenido del día a día."
                : "";
            if (body.Length == 0) return Narration.None;

            return new Narration(
                body + hzText,
                Pros(firstPro, "Edición de foto/vídeo en color", "Series y películas nítidas"));
        }

        private static Narration NarrateBattery(string? battery)
        {
            double wh = FirstNumber(battery);
            if (wh >= 70)
                return new Narration(
                    "Buena autonomía para una jornada lejos del enchufe en uso normal. Para jugar, mejor con el cargador conectado.",
                    Pros("Jornada de trabajo", "Carga rápida USB-C", "Sin enchufe para tareas ligeras"));
            if (wh > 0)
                return new Narration(
                    "Autonomía pensada para tareas ligeras fuera de casa. Para uso intensivo, mejor con el cargador a mano.",
                    Pros("Tareas ligeras sin enchufe", "Carga por USB-C", "Ligero y portátil"));
            return Narration.None;
        }

        private static Narration NarrateWifi(string? wifi)
        {
            // Reutiliza la detección de estándar de SpecFormatter (WiFi 7 / 6E / 6 / 5 / 4 / WiFi).
            string std = SpecFormatter.Format(ComponentIds.Wifi, wifi).Headline ?? "WiFi";
            string bt = " Bluetooth para mando, auriculares y periféricos.";

            switch (std)
            {
                case "WiFi 7":
                    return new Narration(
                        "Lo más nuevo en conectividad (WiFi 7): máxima velocidad y mínima latencia, incluso con muchos dispositivos." + bt,
                        Pros("Streaming 4K sin cortes", "Partidas con ping bajo", "Descargas muy rápidas"));
                case "WiFi 6E":
                    return new Narration(
                        "La última generación de WiFi (6E): banda de 6 GHz, más rápida y con menos interferencias." + bt,
                        Pros("Streaming 4K sin cortes", "Partidas con ping bajo", "Latencia mínima en BT"));
                case "WiFi 6":
                    return new Narration(
                        "WiFi 6: rápido y estable, rinde bien aunque haya varios dispositivos conectados a la vez." + bt,
                        Pros("Streaming 4K fluido", "Videollamadas estables", "Buen rendimiento multitarea"));
                case "WiFi 5":
                    return new Narration(
                        "WiFi de doble banda (WiFi 5): estable y de sobra para navegar, streaming y videollamadas." + bt,
                        Pros("Streaming HD/4K", "Navegación fluida", "Videollamadas estables"));
                default:
                    return new Narration(
                        "Conexión inalámbrica para navegar, streaming y trabajo del día a día." + bt,
                        Pros("Navegación y streaming", "Videollamadas", "Periféricos Bluetooth"));
            }
        }

        private static Narration NarrateCamera(string? camera)
        {
            // Solo ajusta el Summary; los Pros del catálogo (Teams/privacidad/…) siguen valiendo.
            string head = SpecFormatter.Format(ComponentIds.Camera, camera).Headline ?? "Cámara";
            string body =
                head.Contains("4K") ? "Cámara 4K integrada: nítida para videollamadas y grabación."
                : head.Contains("Full HD") ? "Cámara Full HD integrada: buena calidad para videollamadas."
                : head.Contains("HD") ? "Cámara HD integrada para videollamadas del día a día."
                : "Cámara integrada para videollamadas del día a día.";
            return new Narration(body + " Pensada para el día a día con privacidad por hardware.", null);
        }

        private static double FirstNumber(string? s)
        {
            var m = Regex.Match(s ?? "", @"\d+(?:[.,]\d+)?");
            return m.Success && double.TryParse(m.Value.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out double d) ? d : 0;
        }
    }
}
