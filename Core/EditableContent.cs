using System.Collections.Generic;
using System.ComponentModel;

namespace KioskClinicaPC.Core
{
    /// <summary>
    /// Textos de "chrome" (etiquetas fijas de UI). Los valores por defecto viven en código;
    /// solo se persisten las sobreescrituras (AppConfig.UiTexts). Expone un indexer ligable
    /// desde XAML con notificación de cambio para edición inline.
    /// </summary>
    public class EditableContent : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private static readonly Dictionary<string, string> Defaults = new()
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
        };

        private readonly Dictionary<string, string> _overrides;

        public EditableContent(Dictionary<string, string>? overrides)
        {
            _overrides = overrides ?? new Dictionary<string, string>();
        }

        public string this[string key]
        {
            get
            {
                if (_overrides.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v)) return v;
                return Defaults.TryGetValue(key, out var d) ? d : key;
            }
            set
            {
                _overrides[key] = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            }
        }

        /// <summary>Sobreescrituras persistibles (lo que se guarda en AppConfig.UiTexts).</summary>
        public Dictionary<string, string> Overrides => _overrides;
    }
}
