using System.Windows;

namespace KioskClinicaPC.Windows
{
    public partial class InfoModal : Window
    {
        public InfoModal(string title, string definitionText)
        {
            InitializeComponent();

            TitleText.Text = title;
            DefinitionText.Text = definitionText;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
