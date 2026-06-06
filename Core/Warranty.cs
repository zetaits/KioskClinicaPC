using System;

namespace KioskClinicaPC.Core
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

        /// <summary>Texto del distintivo: "36 MESES DE GARANTÍA".</summary>
        public static string Label(string? condition) => $"{Months(condition)} MESES DE GARANTÍA";
    }
}
