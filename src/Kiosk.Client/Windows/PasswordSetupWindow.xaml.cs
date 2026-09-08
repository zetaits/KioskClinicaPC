using System.Windows;
using KioskClinicaPC.Core.Config;

namespace KioskClinicaPC.Windows
{
    public partial class PasswordSetupWindow : Window
    {
        private readonly KioskSettings _settings;
        private readonly bool _hasExistingPassword;

        public PasswordSetupWindow(KioskSettings settings)
        {
            _settings = settings;
            _hasExistingPassword = !string.IsNullOrWhiteSpace(settings.PasswordHash);
            InitializeComponent();

            CurrentPasswordSection.Visibility = _hasExistingPassword ? Visibility.Visible : Visibility.Collapsed;
            TitleText.Text = _hasExistingPassword ? "Renueva la contraseña" : "Protege los ajustes del kiosko";
            IntroText.Text = _hasExistingPassword
                ? "Esta actualización retira la contraseña compartida antigua. Confirma la clave actual y elige una nueva; solo se pedirá esta vez."
                : "Antes de iniciar el kiosko, crea una contraseña propia para acceder a Ajustes y poder salir de la aplicación.";

            (_hasExistingPassword ? CurrentPasswordBox : NewPasswordBox).Focus();
#if !DEBUG
            TouchKeyboard.Show();
#endif
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_settings.TrySetPassword(
                    _hasExistingPassword ? CurrentPasswordBox.Password : null,
                    NewPasswordBox.Password,
                    ConfirmPasswordBox.Password,
                    out string error))
            {
                ErrorText.Text = error;
                ErrorText.Visibility = Visibility.Visible;
                return;
            }

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
