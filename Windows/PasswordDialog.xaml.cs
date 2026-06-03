using System.Windows;
using System.Windows.Input;
using KioskClinicaPC.Core;
using KioskClinicaPC.Windows;

namespace KioskClinicaPC
{
    public partial class PasswordDialog : Window
    {
        public PasswordDialog()
        {
            InitializeComponent();
            PasswordInput.Focus(); // Pone el cursor en el cuadro de contraseña
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            CheckPassword();
        }

        private void PasswordInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                CheckPassword();
            }
        }

        private void CheckPassword()
        {
            var settings = KioskSettings.Load(App.SettingsFilePath);
            if (PasswordService.Verify(PasswordInput.Password, settings.PasswordHash))
            {
                this.DialogResult = true;
                this.Close();
            }
            else
            {
                KioskDialog.Alert(this, "Acceso restringido", "Contraseña incorrecta.", danger: true);
                PasswordInput.Clear();
                PasswordInput.Focus();
            }
        }
    }
}