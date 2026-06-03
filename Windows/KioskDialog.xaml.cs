using System.Windows;
using System.Windows.Media;

namespace KioskClinicaPC.Windows
{
    public enum KioskDialogResult { Primary, Secondary, Cancel }

    public partial class KioskDialog : Window
    {
        private KioskDialogResult _result = KioskDialogResult.Cancel;

        private KioskDialog()
        {
            InitializeComponent();
        }

        private void PrimaryBtn_Click(object sender, RoutedEventArgs e) { _result = KioskDialogResult.Primary; Close(); }
        private void SecondaryBtn_Click(object sender, RoutedEventArgs e) { _result = KioskDialogResult.Secondary; Close(); }
        private void CancelBtn_Click(object sender, RoutedEventArgs e) { _result = KioskDialogResult.Cancel; Close(); }

        /// <summary>Diálogo temático genérico. Devuelve qué botón se pulsó.</summary>
        public static KioskDialogResult Show(Window owner, string title, string message,
            string primaryText = "Aceptar", string? secondaryText = null, string? cancelText = null, bool danger = false)
        {
            var dlg = new KioskDialog
            {
                Owner = owner ?? (Application.Current.MainWindow != null && Application.Current.MainWindow.IsVisible ? Application.Current.MainWindow : null)
            };
            if (dlg.Owner == null) dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;

            dlg.TitleText.Text = title;
            dlg.MessageText.Text = message;
            dlg.PrimaryBtn.Content = primaryText;

            if (secondaryText != null)
            {
                dlg.SecondaryBtn.Content = secondaryText;
                dlg.SecondaryBtn.Visibility = Visibility.Visible;
            }
            if (cancelText != null)
            {
                dlg.CancelBtn.Content = cancelText;
                dlg.CancelBtn.Visibility = Visibility.Visible;
            }

            if (danger)
            {
                dlg.PrimaryBtn.Style = (Style)Application.Current.FindResource("DangerButton");
                dlg.PrimaryBtn.Padding = new Thickness(22, 9, 22, 9);
                dlg.AccentDot.Background = new SolidColorBrush(Color.FromRgb(0xDA, 0x30, 0x30));
            }

            dlg.ShowDialog();
            return dlg._result;
        }

        /// <summary>Aviso de un solo botón.</summary>
        public static void Alert(Window owner, string title, string message, bool danger = false)
            => Show(owner, title, message, "Aceptar", danger: danger);

        /// <summary>Confirmación Aceptar/Cancelar. Devuelve true si se aceptó.</summary>
        public static bool Confirm(Window owner, string title, string message, string primaryText = "Aceptar", bool danger = false)
            => Show(owner, title, message, primaryText, cancelText: "Cancelar", danger: danger) == KioskDialogResult.Primary;
    }
}
