using System.Windows;

namespace KioskClinicaPC.Controls
{
    /// <summary>
    /// Propiedades adjuntas para marcar qué textos participan en el modo de edición libre.
    /// Por defecto IsOn = true ("editar absolutamente todo"); se desactiva explícitamente
    /// en textos calculados/dinámicos (precios formateados, reloj, contadores...).
    /// </summary>
    public static class Editable
    {
        public static readonly DependencyProperty IsOnProperty =
            DependencyProperty.RegisterAttached(
                "IsOn", typeof(bool), typeof(Editable),
                new PropertyMetadata(true));

        public static void SetIsOn(DependencyObject element, bool value) => element.SetValue(IsOnProperty, value);
        public static bool GetIsOn(DependencyObject element) => (bool)element.GetValue(IsOnProperty);

        public static readonly DependencyProperty MultilineProperty =
            DependencyProperty.RegisterAttached(
                "Multiline", typeof(bool), typeof(Editable),
                new PropertyMetadata(false));

        public static void SetMultiline(DependencyObject element, bool value) => element.SetValue(MultilineProperty, value);
        public static bool GetMultiline(DependencyObject element) => (bool)element.GetValue(MultilineProperty);
    }
}
