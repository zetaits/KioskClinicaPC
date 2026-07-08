using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace KioskClinicaPC.Core.Specs
{
    /// <summary>
    /// Deriva una "potencia" (0–100, escalada a 70–100 para los componentes con benchmark) y una
    /// etiqueta de gama amigable a partir del hardware REAL detectado. Sustituye las constantes
    /// inventadas que vivían en SpecCatalog (mismo número/gama en cualquier PC).
    ///
    /// Diseño: heurística por componente, lógica pura y testeable (sin WPF, sin WMI). El número se
    /// recorta a [70,100] a propósito — un equipo en venta nunca debe enseñar una potencia "fea", y
    /// la etiqueta nunca dice "gama baja" (ver <see cref="TierLabel"/>).
    /// Componentes sin métrica de potencia significativa (cámara, puertos, SO) devuelven 0: el caller
    /// los pinta como "componente verificado" en vez del gauge.
    /// </summary>
    public static class PerformanceScorer
    {
        public const int MinScore = 70;
        public const int MaxScore = 100;

        /// <summary>Potencia 70–100 para componentes con benchmark; 0 para los no puntuables.</summary>
        public static int Score(string? id, AppConfig hw)
        {
            if (hw == null) return 0;
            return (id?.ToLowerInvariant()) switch
            {
                ComponentIds.Cpu => Clamp(ScoreCpu(hw.Cores)),
                ComponentIds.Gpu => Clamp(ScoreGpu(hw.Gpu)),
                ComponentIds.Ram => Clamp(ScoreRam(hw.Ram)),
                ComponentIds.Storage => Clamp(ScoreStorage(hw.Storage, hw.StorageDetail)),
                ComponentIds.Screen => Clamp(ScoreScreen(hw.Screen, hw.ScreenDetail)),
                ComponentIds.Battery => Clamp(ScoreBattery(hw.Battery)),
                ComponentIds.Wifi => Clamp(ScoreWifi(hw.Wifi)),
                _ => 0 // cámara, puertos, SO: sin potencia → panel "verificado"
            };
        }

        private static int Clamp(double raw) => (int)Math.Round(Math.Max(MinScore, Math.Min(MaxScore, raw)));

        // --- Heurísticas por componente -------------------------------------------------------

        private static double ScoreCpu(string? cores)
        {
            // "8 Núcleos / 16 Hilos" → núcleos e hilos mandan la potencia.
            var nums = Regex.Matches(cores ?? "", @"\d+");
            int c = nums.Count > 0 && int.TryParse(nums[0].Value, out int cc) ? cc : 0;
            int t = nums.Count > 1 && int.TryParse(nums[1].Value, out int tt) ? tt : c * 2;
            if (c == 0) return MinScore;
            return 70 + c * 1.5 + t * 0.6;
        }

        private static double ScoreRam(string? ram)
        {
            // "32 GB DDR5 (...)" → capacidad + generación.
            double gb = FirstNumber(ram);
            double gbFactor = Math.Min(24, Math.Max(0, gb - 8) * 0.6);
            string l = (ram ?? "").ToLowerInvariant();
            double ddr = l.Contains("ddr5") ? 6 : l.Contains("ddr4") ? 2 : 0;
            return 70 + gbFactor + ddr;
        }

        private static double ScoreGpu(string? gpu)
        {
            string n = (gpu ?? "").ToUpperInvariant();
            bool Has(string s) => n.Contains(s);

            if (Has("RTX 4090") || Has("RTX 4080") || Has("RTX 5090") || Has("RTX 5080")) return 100;
            if (Has("RTX 4070") || Has("RTX 3080") || Has("RTX 5070")) return 96;
            if (Has("RTX 4060") || Has("RTX 3070")) return 94;
            if (Has("RTX 4050") || Has("RTX 3060") || Has("RTX 3050")) return 90;
            if (Has("RX 7") || Has("RX 6")) return 90;
            if (Has("RTX 20") || Has("GTX 16")) return 84;
            if (Has("GTX 10")) return 80;
            // iGPU: Intel UHD/Iris, AMD Radeon Graphics/Vega → suelo de gama.
            if (Has("UHD") || Has("IRIS") || Has("INTEL") || Has("VEGA") || Has("RADEON GRAPHICS")) return 72;
            return string.IsNullOrWhiteSpace(gpu) ? MinScore : 82;
        }

        private static double ScoreStorage(string? storage, string? detail)
        {
            string l = ((detail ?? "") + " " + (storage ?? "")).ToUpperInvariant();
            double tb = Math.Max(FirstNumber(storage) / 1024.0, 0); // value trae GB
            if (l.Contains("NVME")) return 92 + Math.Min(8, Math.Max(0, tb - 1) * 2);
            if (l.Contains("SSD")) return 84;
            if (l.Contains("HDD")) return 72;
            return 80;
        }

        private static double ScoreScreen(string? screen, string? detail)
        {
            // Resolución por ancho + bonus por Hz.
            string src = (detail ?? "") + " " + (screen ?? "");
            var m = Regex.Match(src, @"(?<w>\d{3,5})\D+(?<h>\d{3,5})");
            int w = m.Success && int.TryParse(m.Groups["w"].Value, out int ww) ? ww : 0;
            double bySize = w >= 3840 ? 96 : w >= 2560 ? 90 : w >= 1920 ? 82 : w > 0 ? 74 : MinScore;

            var hzMatch = Regex.Match(detail ?? "", @"(?<hz>\d{2,3})\s*Hz", RegexOptions.IgnoreCase);
            int hz = hzMatch.Success ? int.Parse(hzMatch.Groups["hz"].Value) : 0;
            double hzBonus = hz >= 240 ? 6 : hz >= 165 ? 4 : hz >= 120 ? 2 : 0;
            return bySize + hzBonus;
        }

        private static double ScoreBattery(string? battery)
        {
            double wh = FirstNumber(battery);
            return wh >= 90 ? 90 : wh >= 70 ? 84 : wh >= 50 ? 78 : wh >= 30 ? 74 : 72;
        }

        private static double ScoreWifi(string? wifi)
        {
            string l = (wifi ?? "").ToLowerInvariant();
            if (l.Contains("wi-fi 7") || l.Contains("wifi 7") || l.Contains("802.11be") || l.Contains("be200")) return 96;
            if (l.Contains("6e") || l.Contains("ax21") || l.Contains("ax41")) return 92;
            if (l.Contains("wi-fi 6") || l.Contains("wifi 6") || l.Contains("802.11ax") || l.Contains("ax20") || l.Contains("ax15")) return 88;
            if (l.Contains("802.11ac") || Regex.IsMatch(l, @"\bac\b")) return 80;
            return string.IsNullOrWhiteSpace(wifi) ? MinScore : 76;
        }

        private static double FirstNumber(string? s)
        {
            var m = Regex.Match(s ?? "", @"\d+(?:[.,]\d+)?");
            return m.Success && double.TryParse(m.Value.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out double d) ? d : 0;
        }

        // --- Etiqueta de gama: banda de score × matiz por componente --------------------------
        // Nunca dice "baja"/"ligero". Cuatro bandas: 95+ / 88+ / 80+ / 70+.

        private static int Band(int score) => score >= 95 ? 0 : score >= 88 ? 1 : score >= 80 ? 2 : 3;

        public static string TierLabel(string? id, int score)
        {
            int b = Band(score);
            return (id?.ToLowerInvariant()) switch
            {
                ComponentIds.Cpu     => Pick(b, "Tope de gama",      "Gama alta",          "Gama media-alta",     "Equilibrado"),
                ComponentIds.Gpu     => Pick(b, "Élite gaming",      "Gama gaming",        "Buen rendimiento",    "Experiencia fluida"),
                ComponentIds.Ram     => Pick(b, "Multitarea extrema","Multitarea fluida",  "Holgada",             "Suficiente"),
                ComponentIds.Storage => Pick(b, "Ultrarrápido",      "Muy rápido",         "Rápido",              "Ágil"),
                ComponentIds.Screen  => Pick(b, "Panel pro",         "Alta resolución",    "Nítida",              "Buena imagen"),
                ComponentIds.Battery => Pick(b, "Gran autonomía",    "Buena autonomía",    "Autonomía sólida",    "Uso diario"),
                ComponentIds.Wifi    => Pick(b, "Conexión élite",    "Conexión rápida",    "Buena conexión",      "Conexión estable"),
                _                    => Pick(b, "Tope de gama",      "Gama alta",          "Gama media-alta",     "Equilibrado"),
            };
        }

        private static string Pick(int band, string s95, string s88, string s80, string s70)
            => band switch { 0 => s95, 1 => s88, 2 => s80, _ => s70 };
    }
}
