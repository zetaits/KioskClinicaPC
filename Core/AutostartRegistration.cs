using System;
using System.Diagnostics;
using Microsoft.Win32;
using Serilog;

namespace KioskClinicaPC.Core
{
    /// <summary>
    /// Registra el arranque automático del kiosko en HKCU\...\Run (extraído de MainWindow para
    /// sacar la lógica de registro de la vista). HKCU no requiere privilegios de administrador.
    /// </summary>
    public static class AutostartRegistration
    {
        private const string AppName = "KioskHardwareDisplay";
        private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

        public static void Register()
        {
            try
            {
                // MainModule.FileName es seguro bajo publicación single-file (Assembly.Location
                // devuelve "" ahí → registraría una ruta vacía y rompería el autostart).
                string? exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath))
                {
                    Log.Warning("No se pudo resolver la ruta del ejecutable; autostart no registrado.");
                    return;
                }
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
                key?.SetValue(AppName, $"\"{exePath}\"");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error al intentar registrar la aplicación en el inicio de Windows.");
            }
        }
    }
}
