using System;
using System.Windows.Media;
using Serilog;

namespace KioskClinicaPC.Core
{
    /// <summary>
    /// Decide en arranque si el equipo corre con calidad gráfica completa o en modo ligero.
    /// El kiosko abusa de <c>DropShadowEffect</c>/<c>BlurEffect</c> (pixel shaders); en GPU dedicada
    /// vuelan, pero en iGPU de portátil — y sobre todo en render por software (RDP, VM, drivers viejos,
    /// <see cref="RenderCapability.Tier"/> &lt; 2) — corren en CPU cada frame y la app va "laggy".
    ///
    /// En modo ligero se reduce el nº de partículas (cada una era un blur animado independiente). Los
    /// efectos estáticos se siguen pintando pero quedan cacheados (BitmapCache en el fondo), así que el
    /// look apenas cambia en equipos potentes y la fluidez se recupera en los débiles.
    /// </summary>
    public static class GraphicsQuality
    {
        /// <summary>true → modo ligero (degradar efectos pesados).</summary>
        public static bool IsLow { get; private set; }

        /// <summary>Partículas decorativas a generar: 26 en alta, 0 en baja (eran el mayor coste por frame).</summary>
        public static int ParticleCount => IsLow ? 0 : 26;

        /// <summary>Resuelve el modo efectivo a partir del ajuste y la capacidad de render del equipo.</summary>
        /// <param name="setting">"Auto" (def.), "High" o "Low" desde <see cref="KioskSettings.GraphicsMode"/>.</param>
        public static void Initialize(string? setting)
        {
            // Tier: bits altos = 0 (software), 1 (HW parcial), 2 (HW completo). <2 ⇒ blur en CPU.
            int tier = RenderCapability.Tier >> 16;
            bool hardwareWeak = tier < 2;

            IsLow = (setting?.Trim().ToLowerInvariant()) switch
            {
                "high" => false,
                "low" => true,
                _ => hardwareWeak,   // Auto
            };

            Log.Information("GraphicsQuality: ajuste={Setting} tier={Tier} → modo={Mode}",
                setting ?? "Auto", tier, IsLow ? "LIGERO" : "ALTO");
        }
    }
}
