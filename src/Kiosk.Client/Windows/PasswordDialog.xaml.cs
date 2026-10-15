using System.Windows;
using System.Windows.Input;
using System.Net.Http;
using System.Threading.Tasks;
using KioskClinicaPC.Core;
using KioskClinicaPC.Services;
using KioskClinicaPC.Windows;

namespace KioskClinicaPC
{
    public partial class PasswordDialog : Window
    {
        public PasswordDialog()
        {
            InitializeComponent();
            PasswordInput.Focus(); // Pone el cursor en el cuadro de contraseña
#if !DEBUG
            // Kiosco táctil sin teclado físico: abre el teclado en pantalla para poder escribir.
            TouchKeyboard.Show();
#endif
        }

        private async void OkButton_Click(object sender, RoutedEventArgs e)
        {
            await CheckPasswordAsync();
        }

        private async void PasswordInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await CheckPasswordAsync();
            }
        }

        private async Task CheckPasswordAsync()
        {
            if (!OkButton.IsEnabled) return;
            OkButton.IsEnabled = false;
            CancelButton.IsEnabled = false;
            var settings = KioskSettings.Load(App.SettingsFilePath);
            using var http = new HttpClient { Timeout = System.TimeSpan.FromSeconds(5) };
            var (allowed, error) = await new PanelPasswordAuthenticator(http)
                .VerifyAsync(settings, PasswordInput.Password, App.SettingsFilePath);
            if (allowed)
            {
                this.DialogResult = true;
                this.Close();
            }
            else
            {
                KioskDialog.Alert(this, "Acceso restringido", error, danger: true);
                PasswordInput.Clear();
                PasswordInput.Focus();
                OkButton.IsEnabled = true;
                CancelButton.IsEnabled = true;
            }
        }
    }
}
