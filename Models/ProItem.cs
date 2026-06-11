using KioskClinicaPC.Core;

namespace KioskClinicaPC.Models
{
    public class ProItem : ObservableObject
    {
        public string? Index { get; set; }

        private string? _text;
        public string? Text { get => _text; set => SetProperty(ref _text, value); }
    }
}
