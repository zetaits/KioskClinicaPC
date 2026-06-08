namespace KioskClinicaPC.Core
{
    /// <summary>Estado global del modo de edición libre. Singleton observable.</summary>
    public sealed class EditModeService : ObservableObject
    {
        public static EditModeService Instance { get; } = new EditModeService();

        private EditModeService() { }

        private bool _isActive;
        public bool IsActive { get => _isActive; set => SetProperty(ref _isActive, value); }

        private bool _isDirty;
        public bool IsDirty { get => _isDirty; set => SetProperty(ref _isDirty, value); }
    }
}
