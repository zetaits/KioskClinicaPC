using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace KioskClinicaPC.Core
{
    /// <summary>
    /// Traduce el dato técnico crudo de un componente a un titular entendible por un cliente no técnico
    /// (p.ej. "Intel(R) Wi-Fi 6E AX211" → "WiFi 6E") y conserva el detalle técnico como secundario.
    /// Si no reconoce el patrón, devuelve el valor tal cual (pass-through seguro para ediciones manuales).
    /// </summary>
    public static class SpecFormatter
    {
        public readonly struct Result
        {
            public Result(string? headline, string? tech) { Headline = headline; Tech = tech; }
            /// <summary>Texto grande, orientado a cliente.</summary>
            public string? Headline { get; }
            /// <summary>Texto pequeño y opcional con el nombre técnico/chip (null si no aporta).</summary>
            public string? Tech { get; }
        }

        public static Result Format(string? id, string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return new Result(null, null);

            raw = raw.Trim();
            return (id?.ToLowerInvariant()) switch
            {
                ComponentIds.Wifi => FormatWifi(raw),
                ComponentIds.Camera => FormatCamera(raw),
                ComponentIds.Cpu => new Result(CleanVendorNoise(raw), null),
                ComponentIds.Gpu => FormatGpu(raw),
                ComponentIds.Ram => FormatRam(raw),
                ComponentIds.Storage => FormatStorage(raw),
                ComponentIds.Screen => FormatScreen(raw),
                _ => new Result(raw, null)
            };
        }

        /// <summary>Quita ruido de marca: (R), (TM), comillas, dobles espacios.</summary>
        private static string CleanVendorNoise(string s)
        {
            s = s.Replace("(R)", "").Replace("(r)", "")
                 .Replace("(TM)", "").Replace("(tm)", "")
                 .Replace("®", "").Replace("™", "");
            s = Regex.Replace(s, @"\s+", " ").Trim();
            return s;
        }

        private static Result FormatWifi(string raw)
        {
            string l = raw.ToLowerInvariant();
            string standard =
                (l.Contains("wi-fi 7") || l.Contains("wifi 7") || l.Contains("802.11be") || l.Contains("be200")) ? "WiFi 7"
                : (l.Contains("6e") || l.Contains("ax21") || l.Contains("ax41") || l.Contains("ax411")) ? "WiFi 6E"
                : (l.Contains("wi-fi 6") || l.Contains("wifi 6") || l.Contains("802.11ax") || l.Contains("ax20") || l.Contains("ax15")) ? "WiFi 6"
                : (l.Contains("802.11ac") || Regex.IsMatch(l, @"\bac\b")) ? "WiFi 5"
                : (l.Contains("802.11n")) ? "WiFi 4"
                : "WiFi";

            string tech = CleanVendorNoise(raw)
                .Replace(" 160MHz", "", StringComparison.OrdinalIgnoreCase)
                .Replace(" Network Adapter", "", StringComparison.OrdinalIgnoreCase)
                .Replace(" Wireless Network Adapter", "", StringComparison.OrdinalIgnoreCase)
                .Trim();
            return new Result(standard, string.Equals(tech, standard, StringComparison.OrdinalIgnoreCase) ? null : tech);
        }

        private static Result FormatCamera(string raw)
        {
            string l = raw.ToLowerInvariant();
            string headline =
                (l.Contains("4k") || l.Contains("2160")) ? "Cámara 4K"
                : (l.Contains("fhd") || l.Contains("1080") || l.Contains("full hd")) ? "Cámara Full HD"
                : (l.Contains("hd") || l.Contains("720")) ? "Cámara HD"
                : "Cámara";
            string tech = CleanVendorNoise(raw);
            return new Result(headline, string.Equals(tech, headline, StringComparison.OrdinalIgnoreCase) ? null : tech);
        }

        private static Result FormatGpu(string raw)
        {
            string clean = CleanVendorNoise(raw);
            string headline = clean
                .Replace("NVIDIA GeForce ", "NVIDIA ", StringComparison.OrdinalIgnoreCase)
                .Replace("GeForce ", "", StringComparison.OrdinalIgnoreCase)
                .Replace(" Laptop GPU", "", StringComparison.OrdinalIgnoreCase)
                .Replace(" Graphics", "", StringComparison.OrdinalIgnoreCase)
                .Trim();
            return new Result(headline, string.Equals(headline, clean, StringComparison.OrdinalIgnoreCase) ? null : clean);
        }

        private static Result FormatRam(string raw)
        {
            // Servicio entrega "32 GB (Kingston KF... @ 4800 MHz)". Titular = "32 GB", técnico = lo del paréntesis.
            int paren = raw.IndexOf('(');
            if (paren > 0)
            {
                string headline = raw.Substring(0, paren).Trim();
                string tech = raw.Substring(paren).Trim('(', ')', ' ');
                return new Result(headline, string.IsNullOrWhiteSpace(tech) ? null : tech);
            }
            return new Result(raw, null);
        }

        private static Result FormatStorage(string raw)
        {
            // Servicio entrega "Samsung PM9A1 (1024 GB SSD)". Titular = capacidad + tipo, técnico = modelo.
            var match = Regex.Match(raw, @"^(?<model>.*?)\s*\(\s*(?<gb>\d+(?:[.,]\d+)?)\s*GB\s*(?<type>[A-Za-z]*)\s*\)\s*$");
            if (match.Success)
            {
                string model = match.Groups["model"].Value.Trim();
                string type = match.Groups["type"].Value.Trim();
                if (double.TryParse(match.Groups["gb"].Value.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out double gb))
                {
                    string size = gb >= 1000
                        ? $"{Math.Round(gb / 1024.0, gb % 1024 == 0 ? 0 : 1)} TB"
                        : $"{Math.Round(gb)} GB";
                    // Tipo solo si aporta (SSD/HDD); ignora "Desconocido".
                    bool usefulType = type.Length > 0 && !type.Equals("Desconocido", StringComparison.OrdinalIgnoreCase);
                    string headline = usefulType ? $"{type} {size}" : size;
                    return new Result(headline, string.IsNullOrWhiteSpace(model) ? null : model);
                }
            }
            return new Result(raw, null);
        }

        private static Result FormatScreen(string raw)
        {
            // Servicio entrega "2560 x 1600". Titular = nombre comercial; técnico = la resolución.
            var m = Regex.Match(raw, @"(?<w>\d{3,5})\s*[x×]\s*(?<h>\d{3,5})");
            if (!m.Success) return new Result(raw, null);

            int w = int.Parse(m.Groups["w"].Value);
            int h = int.Parse(m.Groups["h"].Value);

            string commercial =
                w >= 7680 ? "8K UHD"
                : w >= 3840 ? "4K UHD"
                : w >= 3440 ? "QHD Ultrawide"
                : w >= 2560 ? (h >= 1600 ? "QHD+" : "QHD")
                : w >= 1920 ? (h >= 1200 ? "Full HD+" : "Full HD")
                : w >= 1600 ? "HD+"
                : w >= 1280 ? "HD"
                : $"{w}×{h}";

            string res = $"{w} × {h} px";
            return new Result(commercial, res);
        }
    }
}
