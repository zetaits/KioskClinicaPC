using System;

namespace KioskClinicaPC.Core
{
    /// <summary>
    /// Reglas de fusión entre la configuración guardada (overrides manuales) y el hardware detectado.
    /// Centralizadas aquí porque MainViewModel (modo edición) y SettingsWindow las duplicaban con
    /// criterios distintos (OS sin normalizar, placeholders sin filtrar) → datos incoherentes según
    /// dónde se guardara. Lógica pura y testeable.
    /// </summary>
    public static class ConfigMerger
    {
        /// <summary>Valor a MOSTRAR: el override manual si existe; si no, el detectado.</summary>
        public static string? Display(string? manual, string? detected)
            => !string.IsNullOrWhiteSpace(manual) ? manual : detected;

        /// <summary>Valor a GUARDAR como override: null si está vacío o coincide con lo detectado
        /// (no tiene sentido persistir un "override" igual al hardware real).</summary>
        public static string? Override(string? manual, string? detected)
            => (string.IsNullOrWhiteSpace(manual) || string.Equals(manual, detected, StringComparison.OrdinalIgnoreCase))
                ? null
                : manual;

        /// <summary>Componentes opcionales: descarta vacíos y los textos placeholder "No detectada/o".</summary>
        public static string? NoPlaceholder(string? v)
            => (string.IsNullOrWhiteSpace(v) || v == "No detectada" || v == "No detectado") ? null : v;

        /// <summary>Normaliza el SO detectado quitando el sufijo "(NOMBRE-PC)" que añade la detección.</summary>
        public static string? NormalizeOs(string? rawOs)
            => rawOs?.Split('(')[0].Trim();
    }
}
