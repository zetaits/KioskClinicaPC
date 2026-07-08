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

        /// <summary>Cuota mensual del precio efectivo repartido en <paramref name="months"/> meses (por
        /// defecto 6). "" si no es número o los meses no son válidos.</summary>
        public static string Monthly(string? effectivePrice, int months = 6)
        {
            if (string.IsNullOrWhiteSpace(effectivePrice) || months <= 0) return "";
            if (double.TryParse(effectivePrice, NumberStyles.Any, CultureInfo.InvariantCulture, out double p))
                return (p / months).ToString("C2", EsCulture);
            return "";
        }
    }
}
