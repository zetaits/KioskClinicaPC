using System;
using System.Globalization;

namespace KioskClinicaPC.Core
{
    /// <summary>
    /// Lógica pura de formato de precios (extraída de MainViewModel para poder testearla).
    /// Acepta precios crudos como cadena (los introduce el usuario en edición), parsea en cultura
    /// invariante y muestra en es-ES.
    /// </summary>
    public static class PriceFormatter
    {
        private static readonly CultureInfo EsCulture = CultureInfo.GetCultureInfo("es-ES");

        /// <summary>Precio a moneda sin decimales ("1299" → "1.299 €"). Si no es número, lo devuelve tal cual.</summary>
        public static string Format(string? price)
        {
            if (string.IsNullOrWhiteSpace(price)) return "";
            if (double.TryParse(price, NumberStyles.Any, CultureInfo.InvariantCulture, out double val))
                return val.ToString("C0", EsCulture);
            return price;
        }

        /// <summary>Porcentaje de descuento entre precio original y rebajado ("-15%"). "" si falta alguno o no son números.</summary>
        public static string Discount(string? price, string? discountedPrice)
        {
            if (string.IsNullOrWhiteSpace(price) || string.IsNullOrWhiteSpace(discountedPrice)) return "";
            if (double.TryParse(price, NumberStyles.Any, CultureInfo.InvariantCulture, out double p) &&
                double.TryParse(discountedPrice, NumberStyles.Any, CultureInfo.InvariantCulture, out double d) &&
                p != 0)
            {
                double pct = Math.Round((1 - (d / p)) * 100);
                return $"-{pct}%";
            }
            return "";
        }

        /// <summary>Cuota mensual a 12 meses del precio efectivo dado. "" si no es número.</summary>
        public static string Monthly(string? effectivePrice)
        {
            if (string.IsNullOrWhiteSpace(effectivePrice)) return "";
            if (double.TryParse(effectivePrice, NumberStyles.Any, CultureInfo.InvariantCulture, out double p))
                return (p / 12).ToString("C2", EsCulture);
            return "";
        }
    }
}
