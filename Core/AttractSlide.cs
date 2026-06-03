using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace KioskClinicaPC.Core
{
    public class AttractSlide : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private bool SetProperty(ref string? storage, string? value, [CallerMemberName] string? propertyName = null)
        {
            if (storage == value) return false;
            storage = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }

        private string? _eyebrow;
        public string? Eyebrow { get => _eyebrow; set => SetProperty(ref _eyebrow, value); }

        private string? _title1;
        public string? Title1 { get => _title1; set => SetProperty(ref _title1, value); }

        private string? _title2;
        public string? Title2 { get => _title2; set => SetProperty(ref _title2, value); }

        private string? _subtitle;
        public string? Subtitle { get => _subtitle; set => SetProperty(ref _subtitle, value); }
    }
}
