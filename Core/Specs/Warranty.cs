using System;

namespace KioskClinicaPC.Core.Specs
{
    /// <summary>
    /// Garantía derivada del estado del equipo (lógica pura, testeable). Equipo nuevo = 3 años;
    /// de ocasión / reacondicionado = 1 año. La etiqueta se muestra en el distintivo de la ficha.
    /// </summary>
    public static class Warranty
    {
        public const string New = "Nuevo";
        public const string Used = "Ocasion";

        public static bool IsNew(string? condition)
            => string.Equals(condition?.Trim(), New, StringComparison.OrdinalIgnoreCase);

        /// <summary>Meses de garantía: 36 si es nuevo, 12 si es de ocasión.</summary>
        public static int Months(string? condition) => IsNew(condition) ? 36 : 12;

        /// <summary>Años de garantía: 3 si es nuevo, 1 si es de ocasión.</summary>
        public static int Years(string? condition) => Months(condition) / 12;

        /// <summary>Texto del distintivo: "3 AÑOS DE GARANTÍA" (singular/plural según corresponda).</summary>
        public static string Label(string? condition)
        {
            int years = Years(condition);
            return $"{years} {(years == 1 ? "AÑO" : "AÑOS")} DE GARANTÍA";
        }
    }
}
