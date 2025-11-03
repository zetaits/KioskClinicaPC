using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace KioskClinicaPC.Windows
{
    public partial class SpecTile : UserControl
    {
        public SpecTile()
        {
            InitializeComponent();
            DataContext = this;
        }

        public static readonly DependencyProperty IconSourceProperty =
            DependencyProperty.Register("IconSource", typeof(ImageSource), typeof(SpecTile), new PropertyMetadata(null));

        public ImageSource IconSource
        {
            get { return (ImageSource)GetValue(IconSourceProperty); }
            set { SetValue(IconSourceProperty, value); }
        }


        public static readonly DependencyProperty LabelProperty =
            DependencyProperty.Register("Label", typeof(string), typeof(SpecTile), new PropertyMetadata(string.Empty));

        public string Label
        {
            get { return (string)GetValue(LabelProperty); }
            set { SetValue(LabelProperty, value); }
        }


        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register("Value", typeof(string), typeof(SpecTile), new PropertyMetadata(string.Empty));

        public string Value
        {
            get { return (string)GetValue(ValueProperty); }
            set { SetValue(ValueProperty, value); }
        }

        public static readonly DependencyProperty TagProperty =
            DependencyProperty.Register("Tag", typeof(string), typeof(SpecTile), new PropertyMetadata(string.Empty));

        public string Tag
        {
            get { return (string)GetValue(TagProperty); }
            set { SetValue(TagProperty, value); }
        }


        public static readonly DependencyProperty BenefitProperty =
            DependencyProperty.Register("Benefit", typeof(string), typeof(SpecTile), new PropertyMetadata(string.Empty));

        public string Benefit
        {
            get { return (string)GetValue(BenefitProperty); }
            set { SetValue(BenefitProperty, value); }
        }

        // Let's bind the UI elements directly in XAML to the properties
        // We need to update the SpecTile.xaml to use bindings.
    }
}
