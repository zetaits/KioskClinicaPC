using System.Windows;
using System.Windows.Input;

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
            if (PasswordInput.Password == "clinicapc2025")
            {
                this.DialogResult = true;
                this.Close();
            }
            else
            {
                MessageBox.Show("Contraseña incorrecta.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                PasswordInput.Clear();
                PasswordInput.Focus();
            }
        }
    }
}