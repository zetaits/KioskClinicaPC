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

        private readonly Dictionary<string, string> _overrides;

        public EditableContent(Dictionary<string, string>? overrides)
        {
            _overrides = overrides ?? new Dictionary<string, string>();
        }

        public string this[string key]
        {
            get
            {
                return Config.KioskContentDefaults.Text(_overrides, key);
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
