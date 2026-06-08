using System.Windows;

namespace KioskClinicaPC.Services
{
    /// <summary>Implementación de <see cref="IDialogService"/> con <see cref="MessageBox"/> de WPF.</summary>
    public sealed class MessageBoxDialogService : IDialogService
    {
        public void Warn(string message, string title)
            => MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);

        public bool Confirm(string message, string title)
            => MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
    }
}
