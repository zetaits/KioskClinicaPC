using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Serilog;

namespace KioskClinicaPC.Core
{
    public static class KioskManager
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

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

        private static void SetTaskManagerEnabled(bool enabled)
        {
            try
            {
                // Note: This requires admin privileges as configured in app.manifest
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