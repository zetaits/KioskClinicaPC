using System.Collections.Generic;

namespace KioskClinicaPC.Core.Config
{
    /// <summary>Shared fallback content used by the kiosk and event preview.</summary>
    public static class KioskContentDefaults
    {
        public static IReadOnlyDictionary<string, string> Texts { get; } = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(new Dictionary<string, string>
        {
            ["attract.cta"] = "TOCA PARA ANALIZAR ESTE EQUIPO",
            ["attract.hint"] = "O ESPERA · EL RECORRIDO ARRANCA SOLO",

            ["scan.logTitle"] = "// CLINICAPC :: SCAN LOG",
            ["scan.progress"] = "PROGRESO",

            ["hud.detected"] = "EQUIPO DETECTADO",
            ["hud.productView"] = "// VISTA DEL EQUIPO",
            ["hud.components"] = "// COMPONENTES · TOCA PARA VER EL DETALLE",
            ["hud.tileCta"] = "VER DETALLE →",
            ["hud.photoHint"] = "ARRASTRA UNA FOTO DEL EQUIPO · PNG",
            ["hud.statScore"] = "PUNTUACIÓN GLOBAL",
            ["hud.statScoreVal"] = "92",
            ["hud.statScoreMax"] = "/100",
            ["hud.statGen"] = "GEN. COMPONENTES",
            ["hud.statGenVal"] = "2023",
            ["hud.statCycles"] = "CICLOS BATERÍA",
            ["hud.statCyclesVal"] = "47",
            ["hud.statCyclesMax"] = "/300",
            ["hud.statTests"] = "PRUEBAS PASADAS",
            ["hud.statTestsVal"] = "38",
            ["hud.statTestsMax"] = "/38",

            ["card.systemScan"] = "SYSTEM SCAN · 100%",
            ["card.verified"] = "VERIFICADO · GRADO A+",

            ["price.label"] = "// Precio en tienda",
            ["price.installments"] = "FINÁNCIALO",
            ["price.installmentsPrefix"] = "4 × ",
            ["price.noInterest"] = "SIN INTERESES",
            ["price.scanTitle"] = "ESCANEA Y GUARDA LA FICHA",
            ["price.scanText"] = "Toda la info de este equipo en tu móvil, en PDF.",
        });

        public static string Text(IReadOnlyDictionary<string, string>? overrides, string key)
        {
            if (overrides != null && overrides.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value)) return value;
            return Texts.TryGetValue(key, out var fallback) ? fallback : key;
        }

        // Textos por defecto del Attract para equipos de ocasión / reacondicionados.
        public static List<AttractSlide> SlidesUsed() => new List<AttractSlide>
        {
            new AttractSlide { Eyebrow = "CLINICAPC", Title1 = "ESTE EQUIPO", Title2 = "TE ESTÁ OBSERVANDO", Subtitle = "Conéctate · escanea · descubre cada componente en 30 segundos" },
            new AttractSlide { Eyebrow = "SIN TECNICISMOS", Title1 = "LO ENTIENDES", Title2 = "AUNQUE NO SEAS TÉCNICO", Subtitle = "Te traducimos cada spec a lenguaje de calle" },
            new AttractSlide { Eyebrow = "REACONDICIONADOS CON CABEZA", Title1 = "HASTA 60% MENOS", Title2 = "QUE COMPRARLO NUEVO", Subtitle = "Probado, limpiado y con 12 meses de garantía" }
        };

        // Textos por defecto del Attract para equipos nuevos.
        public static List<AttractSlide> SlidesNew() => new List<AttractSlide>
        {
            new AttractSlide { Eyebrow = "CLINICAPC", Title1 = "ESTE EQUIPO", Title2 = "TE ESTÁ OBSERVANDO", Subtitle = "Conéctate · escanea · descubre cada componente en 30 segundos" },
            new AttractSlide { Eyebrow = "SIN TECNICISMOS", Title1 = "LO ENTIENDES", Title2 = "AUNQUE NO SEAS TÉCNICO", Subtitle = "Te traducimos cada spec a lenguaje de calle" },
            new AttractSlide { Eyebrow = "A ESTRENAR", Title1 = "PRECINTADO", Title2 = "Y LISTO PARA TI", Subtitle = "Equipo nuevo con 3 años de garantía y licencia oficial" }
        };
    }
}
