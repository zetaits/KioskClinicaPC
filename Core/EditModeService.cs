using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace KioskClinicaPC.Core
{
    /// <summary>Estado global del modo de edición libre. Singleton observable.</summary>
    public sealed class EditModeService : INotifyPropertyChanged
    {
        public static EditModeService Instance { get; } = new EditModeService();

        private EditModeService() { }

        public event PropertyChangedEventHandler? PropertyChanged;

        private bool Set<T>(ref T storage, T value, [CallerMemberName] string? name = null)
        {
            if (System.Collections.Generic.EqualityComparer<T>.Default.Equals(storage, value)) return false;
            storage = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            return true;
        }

        private bool _isActive;
        public bool IsActive { get => _isActive; set => Set(ref _isActive, value); }

        private bool _isDirty;
        public bool IsDirty { get => _isDirty; set => Set(ref _isDirty, value); }
    }
}
