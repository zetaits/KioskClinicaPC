using System.Windows.Input;

namespace KioskClinicaPC.Core.Platform
{
    /// <summary>Acción global que corresponde a una pulsación en la ventana principal.</summary>
    public enum KioskKeyAction
    {
        None,
        Shutdown,
        OpenSettings,
        ToggleWindowsKey,
        GoToAttract,
        StartScan,
        GoToMain
    }

    /// <summary>
    /// Decide la navegación de teclado sin depender del foco del control. La ventana aplica esta
    /// política en PreviewKeyDown, antes de que un Button pueda consumir Espacio o Enter.
    /// </summary>
    public static class KioskKeyPolicy
    {
        public static KioskKeyAction Resolve(
            int currentScreen,
            bool editMode,
            Key key,
            ModifierKeys modifiers)
        {
            if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
            {
                if (key == Key.K) return KioskKeyAction.Shutdown;
                if (key == Key.S) return KioskKeyAction.OpenSettings;
                if (key == Key.P) return KioskKeyAction.ToggleWindowsKey;
            }

            if (editMode) return KioskKeyAction.None;

            if (key == Key.Escape && currentScreen > 0)
                return KioskKeyAction.GoToAttract;

            if (!IsPlainInteractionKey(key, modifiers))
                return KioskKeyAction.None;

            return currentScreen switch
            {
                0 => KioskKeyAction.StartScan,
                3 => KioskKeyAction.GoToMain,
                _ => KioskKeyAction.None
            };
        }

        private static bool IsPlainInteractionKey(Key key, ModifierKeys modifiers) =>
            modifiers == ModifierKeys.None
            && key != Key.Escape
            && key != Key.System
            && key != Key.LeftCtrl
            && key != Key.RightCtrl
            && key != Key.LeftShift
            && key != Key.RightShift
            && key != Key.LWin
            && key != Key.RWin;
    }
}
