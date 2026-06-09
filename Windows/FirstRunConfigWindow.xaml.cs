using System.Windows;
using KioskClinicaPC.Core;

namespace KioskClinicaPC.Windows
{
    public partial class FirstRunConfigWindow : Window
    {
        public AppConfig? ConfigData { get; private set; }
        
        public FirstRunConfigWindow()
        {
            InitializeComponent();
            PriceTextBox.Focus();
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(PriceTextBox.Text))
            {
                ErrorText.Visibility = Visibility.Visible;
                return;
            }

            ErrorText.Visibility = Visibility.Collapsed;
            
            this.ConfigData = new AppConfig
            {
                Price = PriceTextBox.Text,
                DiscountedPrice = DiscountedPriceTextBox.Text,
                Sku = SkuTextBox.Text.Trim(),
                Condition = NewRadio.IsChecked == true ? Warranty.New : Warranty.Used,
                // Sello "Reacondicionado" = derivado del estado: solo en equipos de ocasión.
                ShowRefurbished = UsedRadio.IsChecked == true
            };
            
            this.DialogResult = true;
            this.Close();
        }
    }
}