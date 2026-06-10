using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Serilog;

namespace KioskClinicaPC.Core
{
    public static class KioskManager
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint SetThreadExecutionState(uint esFlags);

        // Mantiene la pantalla y el sistema despiertos mientras el kiosco corre.
        // ES_CONTINUOUS persiste el estado hasta una nueva llamada (no necesita refresco).
        private const uint ES_CONTINUOUS = 0x80000000;
        private const uint ES_SYSTEM_REQUIRED = 0x00000001;
        private const uint ES_DISPLAY_REQUIRED = 0x00000002;

        private const int SW_HIDE = 0;
        private const int SW_SHOW = 5;

        private const string MainTaskbarClass = "Shell_TrayWnd";
        private const string SecondaryTaskbarClass = "Shell_SecondaryTrayWnd";

        private const string RegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Policies\System";
        private const string DisableTaskMgrValueName = "DisableTaskMgr";

        /// <summary>
        /// Activa todas las protecciones del modo kiosco.
        /// </summary>
        public static void Protect()
        {
            try
            {
                SetTaskbarVisibility(false);
                SetTaskManagerEnabled(false);
                SetDisplaySleepEnabled(false);
                Log.Information("Protecciones de Kiosco activadas.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error al activar las protecciones de kiosco.");
            }
        }

        /// <summary>
        /// Restaura el sistema a su estado normal.
        /// </summary>
        public static void Release()
        {
            try
            {
                SetTaskbarVisibility(true);
                SetTaskManagerEnabled(true);
                SetDisplaySleepEnabled(true);
                Log.Information("Protecciones de Kiosco desactivadas.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error al desactivar las protecciones de kiosco.");
            }
        }

        private static void SetTaskbarVisibility(bool visible)
        {
            int cmd = visible ? SW_SHOW : SW_HIDE;
            IntPtr mainHandle = FindWindow(MainTaskbarClass, null);
            IntPtr secondaryHandle = FindWindow(SecondaryTaskbarClass, null);

            if (mainHandle != IntPtr.Zero) ShowWindow(mainHandle, cmd);
            if (secondaryHandle != IntPtr.Zero) ShowWindow(secondaryHandle, cmd);
        }

        /// <summary>
        /// Evita (o restaura) el apagado automático de la pantalla y la suspensión del sistema.
        /// No modifica el plan de energía de Windows: el estado se revierte al cerrar la app.
        /// </summary>
        private static void SetDisplaySleepEnabled(bool sleepEnabled)
        {
            try
            {
                uint flags = sleepEnabled
                    ? ES_CONTINUOUS
                    : ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED;

                if (SetThreadExecutionState(flags) == 0)
                {
                    Log.Warning("SetThreadExecutionState devolvió 0 (no se pudo fijar el estado de energía).");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error al fijar el estado de energía de la pantalla.");
            }
        }

        private static void SetTaskManagerEnabled(bool enabled)
        {
            try
            {
                // HKCU no requiere privilegios de administrador (la app corre como asInvoker).
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryKeyPath))
                {
                    if (enabled)
                    {
                        key.DeleteValue(DisableTaskMgrValueName, false);
                    }
                    else
                    {
                        key.SetValue(DisableTaskMgrValueName, 1, RegistryValueKind.DWord);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error al modificar el estado del Administrador de Tareas en el registro.");
            }
        }
    }
}